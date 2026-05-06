using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Entity;
using Forge.Execution;

namespace Forge.Operations.Http;

/// <summary>
/// Reflection-based helper that binds a <see cref="JsonObject"/> request body to an
/// entity instance.
/// </summary>
/// <remarks>
/// Only scalar <c>[Predicate]</c>-annotated properties are populated. Navigation
/// properties (<c>[Owning]</c>, <c>[Inverse]</c>) are silently skipped. JSON keys
/// are matched to C# property names case-insensitively.
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
        var identityAttr = t.GetCustomAttribute<IdentityAttribute>()
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

        return new EntityBindingPlan(identityAttr.Generator, guidCtor, bindableProps);
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
    }

    private static string ExtractIriSuffix(string iri)
    {
        var idx = iri.LastIndexOf('/');
        return idx >= 0 && idx + 1 < iri.Length ? iri[(idx + 1)..] : iri;
    }
}

// ────────────────────────────────────────────────────── Cached reflection plan

/// <summary>Per-type reflection plan used by <see cref="OperationEntityBinder"/>.</summary>
internal sealed record EntityBindingPlan(
    IdentityGenerator Generator,
    ConstructorInfo? GuidCtor,
    IReadOnlyList<BindableProp> Properties);

/// <summary>A single bindable scalar property on an entity type.</summary>
internal sealed record BindableProp(
    PropertyInfo Property,
    bool IsIdentityPart,
    FieldInfo? BackingField);
