using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Entity;
using Forge.Execution;
using Forge.Operations;

namespace Forge.Operations.Http;

/// <summary>
/// Reflection-based helper that binds a <see cref="JsonObject"/> request body to an
/// entity instance.
/// </summary>
/// <remarks>
/// Scalar <c>[Predicate]</c>-annotated properties and owned-relation properties
/// (<c>[Owning]</c>) are both populated. <c>[Inverse]</c> navigation properties are
/// skipped (the owning side is the single source of truth).
/// JSON keys are matched to C# property names case-insensitively.
/// <para>
/// Owned single refs (<c>EntityRef&lt;T&gt;?</c>) are bound from a JSON string IRI.
/// Owned collections (<c>EntityRefCollection&lt;T&gt;</c>) are bound from a JSON array
/// of IRI strings; IRI-only stubs are added to the collection (entities are not loaded
/// from the store — existence is not validated at bind time).
/// </para>
/// <para>
/// For <see cref="IdentityGenerator.Random"/> entities a <c>Create</c> call invokes the
/// public parameterless constructor (which seals a new UUID-based IRI). An <c>Update</c>
/// call reflects the generator-emitted <c>internal T(Guid persistedUuid)</c> constructor
/// so the existing IRI is preserved.
/// </para>
/// <para>
/// For <see cref="IdentityGenerator.PropertyBasedEncoded"/> and
/// <see cref="IdentityGenerator.PropertyBasedPlain"/> entities an <c>Update</c> creates
/// a fresh instance, sets all properties from the JSON body, then accesses
/// <c>entity.Iri</c> to trigger lazy identity materialisation.  If the computed IRI does
/// not match the <c>?iri=</c> query parameter an
/// <see cref="ExecutionError">IRI_MISMATCH</see> error is returned.
/// </para>
/// Per-type <see cref="EntityBindingPlan"/> objects are cached in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </remarks>
internal static class OperationEntityBinder
{
    /// <summary>
    /// Backing-field prefix emitted by <c>Forge.Entity.Generators</c> for init-only
    /// <c>[IdentityPart]</c> properties.
    /// </summary>
    private const string PartFieldPrefix = "__forge_part_";

    private static readonly MethodInfo HydrateIriMethod =
        typeof(EntityBase).GetMethod("HydrateIri", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not reflect EntityBase.HydrateIri.");

    private static readonly ConcurrentDictionary<Type, EntityBindingPlan> Plans = new();

    // ──────────────────────────────────────────────────────────── Plan cache

    internal static EntityBindingPlan GetPlan(Type entityType)
        => Plans.GetOrAdd(entityType, BuildPlan);

    private static EntityBindingPlan BuildPlan(Type t)
    {
        // IdentityAttribute has Inherited = false; walk the base-type chain manually so
        // that entity subtypes (which must not redeclare [Identity] per ADR-0016) are
        // accepted here.
        var identityAttr = FindAttributeOnTypeOrBases<IdentityAttribute>(t)
            ?? throw new InvalidOperationException(
                $"Type '{t.Name}' is missing the [Identity] attribute.");

        ConstructorInfo? guidCtor = null;
        if (identityAttr.Generator == IdentityGenerator.Random)
        {
            guidCtor = t.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(Guid)]);
        }

        var bindableProps = t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<PredicateAttribute>() is not null)
            .Select(p =>
            {
                var isIdentityPart = p.GetCustomAttribute<IdentityPartAttribute>() is not null;
                var isInitOnly = p.SetMethod is not null
                    && p.SetMethod.ReturnParameter
                           .GetRequiredCustomModifiers()
                           .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

                FieldInfo? backingField = null;
                if (isIdentityPart && isInitOnly)
                {
                    backingField = t.GetField(
                        PartFieldPrefix + p.Name,
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                return new BindableProp(p, isIdentityPart, backingField);
            })
            .ToList();

        var owningSingles = new List<OwningRefProp>();
        var owningCollections = new List<OwningCollProp>();

        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<OwningAttribute>() is null) continue;
            if (prop.GetCustomAttribute<InverseAttribute>() is not null) continue;

            var (kind, target) = ClassifyRef(prop.PropertyType);
            if (target is null) continue;

            if (kind == RefKindLite.Single)
                owningSingles.Add(new OwningRefProp(prop, target));
            else
                owningCollections.Add(new OwningCollProp(prop, target));
        }

        return new EntityBindingPlan(identityAttr.Generator, guidCtor, bindableProps, owningSingles, owningCollections);
    }

    private enum RefKindLite { Single, Collection }

    private static (RefKindLite Kind, Type? Target) ClassifyRef(Type propType)
    {
        var t = Nullable.GetUnderlyingType(propType) ?? propType;
        if (!t.IsGenericType) return (RefKindLite.Single, null);
        var def = t.GetGenericTypeDefinition();
        if (def == typeof(EntityRef<>))
            return (RefKindLite.Single, t.GetGenericArguments()[0]);
        if (def == typeof(EntityRefCollection<>))
            return (RefKindLite.Collection, t.GetGenericArguments()[0]);
        return (RefKindLite.Single, null);
    }

    /// <summary>Build <c>EntityRef&lt;TTarget&gt;.ForIri(iri)</c> via reflection.</summary>
    internal static object MakeEntityRefForIri(Type targetType, string iri)
    {
        var refType = typeof(EntityRef<>).MakeGenericType(targetType);
        var forIri = refType.GetMethod("ForIri", BindingFlags.Public | BindingFlags.Static)!;
        return forIri.Invoke(null, [iri])!;
    }

    /// <summary>
    /// Add an IRI-only stub to an <c>EntityRefCollectionImpl&lt;T&gt;</c> or
    /// <c>DeferredEntityRefCollectionImpl&lt;T&gt;</c> by reaching into the private
    /// <c>_byIri</c> dictionary — the same approach used by
    /// <c>ReflectionRdfMapper.AddStubToCollection</c>.
    /// </summary>
    internal static void AddStubToCollection(object collection, string memberIri)
    {
        var byIriField = collection.GetType().GetField("_byIri",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (byIriField?.GetValue(collection) is IDictionary dict)
            dict[memberIri] = null;
    }

    // ──────────────────────────────────────────────────────────── Create

    /// <summary>
    /// Creates a new entity of type <typeparamref name="T"/> from the JSON body.
    /// For <see cref="IdentityGenerator.Random"/> entities the public parameterless
    /// constructor seals a freshly generated UUID-based IRI.
    /// For property-based identity strategies identity parts are set from the body
    /// so the IRI is computed on first access.
    /// </summary>
    internal static T CreateFromJson<T>(JsonObject body) where T : class, IEntity
    {
        var plan = GetPlan(typeof(T));
        // Public parameterless ctor: Random seals a new UUID; PropertyBased leaves IRI unset.
        var entity = (T)Activator.CreateInstance(typeof(T), nonPublic: false)!;
        PopulateProperties(entity, body, plan, skipIdentityParts: false);
        return entity;
    }

    // ──────────────────────────────────────────────────────────── Update

    /// <summary>
    /// Constructs an entity for update, hydrating it from <paramref name="iri"/> and
    /// <paramref name="body"/>.
    /// </summary>
    /// <returns>
    /// A tuple of (entity, null) on success, or (null, error) when the IRI is invalid or
    /// the body's identity parts disagree with the provided IRI.
    /// </returns>
    internal static (T? Entity, ExecutionError? Error) UpdateFromJson<T>(
        string iri, JsonObject body)
        where T : class, IEntity
    {
        var plan = GetPlan(typeof(T));

        if (plan.Generator == IdentityGenerator.Random)
        {
            var suffix = ExtractIriSuffix(iri);
            if (!Guid.TryParse(suffix, out var uuid))
                return (null, new ExecutionError("INVALID_IRI",
                    $"IRI '{iri}' does not end with a parseable UUID."));

            if (plan.GuidCtor is null)
                return (null, new ExecutionError("INTERNAL_ERROR",
                    $"Type '{typeof(T).Name}' is missing the generator-emitted internal(Guid) constructor."));

            var entity = (T)plan.GuidCtor.Invoke([uuid]);
            PopulateProperties(entity, body, plan, skipIdentityParts: true);
            return (entity, null);
        }
        else
        {
            // PropertyBasedPlain / PropertyBasedEncoded:
            // Materialise the entity from the body identical to a Create, then verify its
            // computed IRI matches the request's ?iri= parameter.
            var entity = (T)Activator.CreateInstance(typeof(T), nonPublic: false)!;
            PopulateProperties(entity, body, plan, skipIdentityParts: false);

            var computedIri = entity.Iri;
            if (!string.Equals(computedIri, iri, StringComparison.Ordinal))
                return (null, new ExecutionError("IRI_MISMATCH",
                    $"The IRI computed from the request body ('{computedIri}') " +
                    $"does not match the provided IRI ('{iri}'). " +
                    "Ensure the identity properties in the body are consistent with the target IRI."));

            return (entity, null);
        }
    }

    // ──────────────────────────────────────────────────────────── Helpers

    private static void PopulateProperties<T>(
        T entity, JsonObject body, EntityBindingPlan plan, bool skipIdentityParts)
        where T : class
    {
        foreach (var prop in plan.Properties)
        {
            if (skipIdentityParts && prop.IsIdentityPart) continue;

            // Match JSON key to C# property name, trying PascalCase first then camelCase.
            if (!body.TryGetPropertyValue(prop.Property.Name, out var node))
            {
                var camelCase = char.ToLowerInvariant(prop.Property.Name[0])
                    + prop.Property.Name[1..];
                if (!body.TryGetPropertyValue(camelCase, out node))
                    continue;
            }

            if (node is null) continue;

            object? value;
            try
            {
                value = node.Deserialize(prop.Property.PropertyType);
            }
            catch
            {
                continue; // skip values that cannot be coerced to the target type
            }

            if (value is null && prop.Property.PropertyType.IsValueType) continue;

            if (prop.BackingField is not null)
                prop.BackingField.SetValue(entity, value);
            else if (prop.Property.SetMethod is { IsPublic: true })
                prop.Property.SetValue(entity, value);
        }

        // ── Owning single refs: bind from JSON string IRI ──────────────────────
        foreach (var ownSingle in plan.OwningSingles)
        {
            if (!body.TryGetPropertyValue(ownSingle.Property.Name, out var node))
            {
                var camelCase = char.ToLowerInvariant(ownSingle.Property.Name[0])
                    + ownSingle.Property.Name[1..];
                if (!body.TryGetPropertyValue(camelCase, out node))
                    continue;
            }

            if (node is null || node.GetValueKind() != JsonValueKind.String) continue;
            var iri = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(iri)) continue;

            var refValue = MakeEntityRefForIri(ownSingle.TargetType, iri);
            if (ownSingle.Property.SetMethod is { IsPublic: true })
                ownSingle.Property.SetValue(entity, refValue);
        }

        // ── Owning collections: bind from JSON array of IRI strings ────────────
        foreach (var ownColl in plan.OwningCollections)
        {
            if (!body.TryGetPropertyValue(ownColl.Property.Name, out var node))
            {
                var camelCase = char.ToLowerInvariant(ownColl.Property.Name[0])
                    + ownColl.Property.Name[1..];
                if (!body.TryGetPropertyValue(camelCase, out node))
                    continue;
            }

            if (node is not JsonArray arr) continue;
            var collection = ownColl.Property.GetValue(entity);
            if (collection is null) continue;

            foreach (var element in arr)
            {
                if (element?.GetValueKind() != JsonValueKind.String) continue;
                var memberIri = element.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(memberIri))
                    AddStubToCollection(collection, memberIri);
            }
        }
    }

    // ──────────────────────────────────────────────────────────── Relation validation

    private static readonly MethodInfo CheckStoreExistsOpenMethod =
        typeof(OperationEntityBinder)
            .GetMethod(nameof(CheckStoreExistsAsync), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not reflect CheckStoreExistsAsync.");

    /// <summary>
    /// Validates all <c>[Owning]</c> relation IRIs present on <paramref name="entity"/>
    /// after binding. Single refs (<c>EntityRef&lt;T&gt;?</c>) and collection stubs
    /// (<c>EntityRefCollection&lt;T&gt;.Iris</c>) are checked against the ambient store
    /// for regular entity targets, or against the static <c>All</c> vocabulary for
    /// <see cref="EnumerationAttribute"/> targets.
    /// Returns the first <c>RELATION_NOT_FOUND</c> error on failure, or
    /// <see langword="null"/> when every IRI resolves.
    /// </summary>
    internal static async ValueTask<ExecutionError?> ValidateOwningRelationsAsync<T>(
        T entity, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var plan = GetPlan(typeof(T));

        foreach (var single in plan.OwningSingles)
        {
            var refObj = single.Property.GetValue(entity);
            if (refObj is null) continue;

            var iriProp = refObj.GetType().GetProperty("Iri")!;
            var iri = (string)iriProp.GetValue(refObj)!;

            var error = await CheckRelationIriAsync(single.TargetType, iri, cancellationToken);
            if (error is not null) return error;
        }

        foreach (var coll in plan.OwningCollections)
        {
            var collObj = coll.Property.GetValue(entity);
            if (collObj is null) continue;

            var collInterface = typeof(EntityRefCollection<>).MakeGenericType(coll.TargetType);
            var irisProp = collInterface.GetProperty("Iris")!;
            var iris = (IReadOnlyCollection<string>)irisProp.GetValue(collObj)!;

            foreach (var iri in iris)
            {
                var error = await CheckRelationIriAsync(coll.TargetType, iri, cancellationToken);
                if (error is not null) return error;
            }
        }

        return null;
    }

    private static async ValueTask<ExecutionError?> CheckRelationIriAsync(
        Type targetType, string iri, CancellationToken cancellationToken)
    {
        if (targetType.GetCustomAttribute<EnumerationAttribute>() is not null)
        {
            var allProp = targetType.GetProperty("All",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (allProp?.GetValue(null) is IEnumerable<IEntity> all && all.Any(e => e.Iri == iri))
                return null;
            return new ExecutionError("RELATION_NOT_FOUND",
                $"'{iri}' is not a known {targetType.Name} IRI.");
        }

        var method = CheckStoreExistsOpenMethod.MakeGenericMethod(targetType);
        var task = (Task<bool>)method.Invoke(null, [iri, cancellationToken])!;
        return await task
            ? null
            : new ExecutionError("RELATION_NOT_FOUND",
                $"{targetType.Name} '{iri}' does not exist.");
    }

    private static async Task<bool> CheckStoreExistsAsync<TTarget>(
        string iri, CancellationToken ct)
        where TTarget : class, IEntity
        => await EntityOperations.ReadAsync<TTarget>(iri, ct) is not null;

    private static string ExtractIriSuffix(string iri)
    {
        var idx = iri.LastIndexOf('/');
        return idx >= 0 && idx + 1 < iri.Length ? iri[(idx + 1)..] : iri;
    }

    /// <summary>
    /// Walks <paramref name="type"/> and its base types to find a custom attribute of
    /// type <typeparamref name="TAttr"/>. Required because <see cref="IdentityAttribute"/>
    /// has <c>Inherited = false</c>, so <c>GetCustomAttribute(inherit: true)</c> does not
    /// traverse the hierarchy.
    /// </summary>
    private static TAttr? FindAttributeOnTypeOrBases<TAttr>(Type type)
        where TAttr : Attribute
    {
        var t = type;
        while (t is not null)
        {
            var attr = t.GetCustomAttribute<TAttr>();
            if (attr is not null) return attr;
            t = t.BaseType;
        }
        return null;
    }
}

// ────────────────────────────────────────────────────── Cached reflection plan

/// <summary>Per-type reflection plan used by <see cref="OperationEntityBinder"/>.</summary>
internal sealed record EntityBindingPlan(
    IdentityGenerator Generator,
    ConstructorInfo? GuidCtor,
    IReadOnlyList<BindableProp> Properties,
    IReadOnlyList<OwningRefProp> OwningSingles,
    IReadOnlyList<OwningCollProp> OwningCollections);

/// <summary>A single bindable scalar property on an entity type.</summary>
internal sealed record BindableProp(
    PropertyInfo Property,
    bool IsIdentityPart,
    FieldInfo? BackingField);

/// <summary>An owning single-ref property (<c>EntityRef&lt;T&gt;?</c>) bindable from a JSON IRI string.</summary>
internal sealed record OwningRefProp(PropertyInfo Property, Type TargetType);

/// <summary>An owning collection property (<c>EntityRefCollection&lt;T&gt;</c>) bindable from a JSON array of IRI strings.</summary>
internal sealed record OwningCollProp(PropertyInfo Property, Type TargetType);
