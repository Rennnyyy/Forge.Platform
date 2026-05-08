using Forge.Entity;
using System.Collections.Concurrent;
using System.Reflection;
using Forge.Repository.Rdf;

namespace Forge.Repository.Mapping;

/// <summary>
/// Reflection-based default mapper for <typeparamref name="T"/>. Discovers
/// <c>[Entity]</c>, <c>[Identity]</c>, <c>[IdentityPart]</c>, <c>[Predicate]</c>,
/// <c>[Owning]</c>, <c>[Inverse]</c>, then hydrates / projects accordingly.
/// </summary>
/// <remarks>
/// See ADR-0013. Phase-2 will introduce a generator-emitted alternative implementing
/// the same interface; both can coexist, with the generated mapper taking precedence
/// in the registry.
/// </remarks>
public sealed class ReflectionRdfMapper<T> : IRdfMapper<T> where T : class, IEntity
{
    private static readonly Lazy<TypePlan> _plan = new(BuildPlan);

    public Type EntityType => typeof(T);
    public string? EntityPath => _plan.Value.EntityPath;
    public string? PredicatePath => _plan.Value.PredicatePath;

    public string ResolveTypeIri(EntityRepositoryOptions options) =>
        options.ResolveTypeIri(typeof(T).Name, _plan.Value.EntityPath);

    /// <inheritdoc/>
    public void ProjectEntity(IEntity entity, IRdfTripleSink sink, string typeIri) =>
        Project((T)entity, sink, typeIri);

    // --------------------------------------------------------------- Hydrate

    public async ValueTask<T?> HydrateAsync(
        string iri,
        RdfGraph subjectGraph,
        IInverseRefLoader? inverseLoader = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        ArgumentNullException.ThrowIfNull(subjectGraph);
        if (subjectGraph.Count == 0) return null;

        var plan = _plan.Value;

        // 1. Construct an instance, hydrating identity-state correctly per strategy.
        var instance = ConstructHydrated(iri, subjectGraph, plan);

        // 2. Set [Predicate] scalar properties.
        foreach (var dp in plan.DataProperties)
        {
            var literal = subjectGraph.FirstObjectOf(dp.PredicateIri);
            if (literal is null) continue;
            object? value = LiteralCodec.Decode(literal.Value, dp.Property.PropertyType);
            // Identity-part properties have init-only setters that go through GuardIdentityMutation.
            // At this point the IRI is sealed, so writing through the public setter would throw —
            // we instead set the private __forge_part_{Name} backing field directly.
            if (dp.IsIdentityPart && dp.IdentityPartField is not null)
                dp.IdentityPartField.SetValue(instance, value);
            else
                dp.Property.SetValue(instance, value);
        }

        // 3. Set owning single refs (EntityRef<T>?).
        foreach (var or in plan.OwningSingleRefs)
        {
            var obj = subjectGraph.FirstObjectOf(or.PredicateIri);
            if (obj is null || !obj.Value.IsIri) continue;
            var refInstance = MakeEntityRefForIri(or.TargetType, obj.Value.Value);
            or.Property.SetValue(instance, refInstance);
        }

        // 4. Pre-populate eager owning collections by parsing rdf:List membership.
        foreach (var oc in plan.OwningCollections)
        {
            if (oc.IsLazy) continue; // deferred — leave to ICollectionLoader
            var collection = oc.Property.GetValue(instance);
            if (collection is null) continue;
            var members = ReadOrderedList(subjectGraph, oc.PredicateIri);
            foreach (var memberIri in members)
                AddStubToCollection(collection, oc.TargetType, memberIri);
        }

        // 5. Populate inverse single refs (ADR-0017) by querying the store in reverse.
        if (inverseLoader is not null)
        {
            foreach (var ir in plan.InverseSingleRefs)
            {
                var ownerIri = await inverseLoader
                    .LoadInverseRefIriAsync(iri, ir.PredicateIri, cancellationToken)
                    .ConfigureAwait(false);
                if (ownerIri is null) continue;
                var refInstance = MakeEntityRefForIri(ir.TargetType, ownerIri);
                ir.BackingField?.SetValue(instance, refInstance);
            }
        }

        return instance;
    }

    private static T ConstructHydrated(string iri, RdfGraph subjectGraph, TypePlan plan)
    {
        switch (plan.Strategy)
        {
            case IdentityStrategy.UuidV4 or IdentityStrategy.UuidV5:
                {
                    // Use the generator-emitted internal ctor `internal {Type}(Guid persistedUuid)`
                    // when the suffix is a parseable GUID, else fall through to parameterless + HydrateIri.
                    var suffix = ExtractIriSuffix(iri);
                    if (Guid.TryParse(suffix, out var g) && plan.GuidCtor is not null)
                        return (T)plan.GuidCtor.Invoke(new object[] { g });
                    goto case IdentityStrategy.Path;
                }

            case IdentityStrategy.Path:
            default:
                {
                    var inst = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
                    // EntityBase.HydrateIri is protected internal — invoke via reflection.
                    plan.HydrateIriMethod.Invoke(inst, new object?[] { iri });
                    return inst;
                }
        }
    }

    private static string ExtractIriSuffix(string iri)
    {
        var slash = iri.LastIndexOf('/');
        return slash >= 0 && slash + 1 < iri.Length ? iri.Substring(slash + 1) : iri;
    }

    // --------------------------------------------------------------- Project

    public void Project(T entity, IRdfTripleSink sink, string typeIri)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIri);
        var plan = _plan.Value;

        var subj = RdfTerm.Iri(entity.Iri);
        // Emit rdf:type for the concrete type, then strip one path segment per ancestor level
        // to emit ancestor type IRIs (ADR-0016: child type IRI = {parent-type-IRI}/{ChildClass}).
        var currentTypeIri = typeIri;
        sink.Add(new RdfTriple(subj, RdfVocab.RdfTypeIri, RdfTerm.Iri(currentTypeIri)));
        for (int i = 0; i < plan.EntityAncestorCount; i++)
        {
            var lastSlash = currentTypeIri.LastIndexOf('/');
            if (lastSlash <= 0) break;
            currentTypeIri = currentTypeIri[..lastSlash];
            sink.Add(new RdfTriple(subj, RdfVocab.RdfTypeIri, RdfTerm.Iri(currentTypeIri)));
        }

        // [Predicate] data
        foreach (var dp in plan.DataProperties)
        {
            var raw = dp.Property.GetValue(entity);
            if (raw is null) continue;
            if (raw is string s && s.Length == 0) continue; // skip empty strings — keep the graph clean
            sink.Add(new RdfTriple(subj, RdfTerm.Iri(dp.PredicateIri),
                LiteralCodec.Encode(raw, dp.Property.PropertyType)));
        }

        // [Owning] singles
        foreach (var or in plan.OwningSingleRefs)
        {
            var refInstance = or.Property.GetValue(entity);
            if (refInstance is null) continue;
            // Use IRI getter on EntityRef<U>
            var iri = (string)or.IriGetter!.Invoke(refInstance, null)!;
            sink.Add(new RdfTriple(subj, RdfTerm.Iri(or.PredicateIri), RdfTerm.Iri(iri)));
        }

        // [Owning] collections — emit as rdf:List preserving order.
        foreach (var oc in plan.OwningCollections)
        {
            var collection = oc.Property.GetValue(entity);
            if (collection is null) continue;
            // Get IRIs (preserve insertion order of EntityRefCollectionImpl).
            var iris = (System.Collections.IEnumerable)oc.IrisGetter!.Invoke(collection, null)!;
            EmitRdfList(sink, subj, RdfTerm.Iri(oc.PredicateIri), iris);
        }
    }

    private static void EmitRdfList(IRdfTripleSink sink, RdfTerm subject, RdfTerm predicate,
        System.Collections.IEnumerable iris)
    {
        var list = new List<string>();
        foreach (var x in iris) if (x is string s) list.Add(s);
        if (list.Count == 0)
        {
            sink.Add(new RdfTriple(subject, predicate, RdfVocab.RdfNilIri));
            return;
        }
        var head = RdfTerm.Blank(sink.NewBlankNodeLabel());
        sink.Add(new RdfTriple(subject, predicate, head));
        for (int i = 0; i < list.Count; i++)
        {
            sink.Add(new RdfTriple(head, RdfVocab.RdfFirstIri, RdfTerm.Iri(list[i])));
            if (i + 1 < list.Count)
            {
                var next = RdfTerm.Blank(sink.NewBlankNodeLabel());
                sink.Add(new RdfTriple(head, RdfVocab.RdfRestIri, next));
                head = next;
            }
            else
            {
                sink.Add(new RdfTriple(head, RdfVocab.RdfRestIri, RdfVocab.RdfNilIri));
            }
        }
    }

    private static List<string> ReadOrderedList(RdfGraph graph, string predicateIri)
    {
        var result = new List<string>();
        var head = graph.FirstObjectOf(predicateIri);
        while (head is { } node && !(node.IsIri && node.Value == RdfVocab.Nil))
        {
            var key = RdfGraph.KeyOf(node);
            var first = graph.FirstObjectOf(key, RdfVocab.First);
            if (first is null) break;
            if (first.Value.IsIri) result.Add(first.Value.Value);
            var rest = graph.FirstObjectOf(key, RdfVocab.Rest);
            head = rest;
        }
        return result;
    }

    // --------------------------------------------------------------- Helpers

    /// <summary>Build <c>EntityRef&lt;TTarget&gt;.ForIri(iri)</c> via reflection.</summary>
    private static object MakeEntityRefForIri(Type targetType, string iri)
    {
        var refType = typeof(EntityRef<>).MakeGenericType(targetType);
        var forIri = refType.GetMethod("ForIri", BindingFlags.Public | BindingFlags.Static)!;
        return forIri.Invoke(null, new object[] { iri })!;
    }

    /// <summary>Add a <c>EntityRef&lt;TTarget&gt;.ForIri(iri)</c>-anchored stub to a collection synchronously.</summary>
    private static void AddStubToCollection(object collection, Type targetType, string memberIri)
    {
        // EntityRefCollectionImpl<TTarget> exposes Iris via the interface; we want to add an IRI without
        // a value. Use reflection on the impl's internal dictionary via TryAddIri helper, or fall back
        // to AddAsync(stub-instance). Cleanest: use the public EntityRefCollection<T> AddAsync via a
        // lazy stub built from EntityRef<T>.ForIri. But AddAsync expects the entity (not a ref).
        // Trade-off: we know the impl is EntityRefCollectionImpl<T>; reach into its private _byIri.
        var implType = collection.GetType();
        var byIriField = implType.GetField("_byIri",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (byIriField?.GetValue(collection) is System.Collections.IDictionary dict)
            dict[memberIri] = null; // null value means "IRI known, entity not yet loaded"
    }

    // --------------------------------------------------------------- Plan (built once per type)

    private sealed record TypePlan(
        IdentityStrategy Strategy,
        string? EntityPath,
        string? PredicatePath,
        ConstructorInfo? GuidCtor,
        MethodInfo HydrateIriMethod,
        IReadOnlyList<DataProp> DataProperties,
        IReadOnlyList<RefProp> OwningSingleRefs,
        IReadOnlyList<CollectionProp> OwningCollections,
        IReadOnlyList<InverseRefProp> InverseSingleRefs,
        int EntityAncestorCount);

    private sealed record DataProp(
        PropertyInfo Property,
        string PredicateIri,
        bool IsIdentityPart,
        FieldInfo? IdentityPartField);

    private sealed record RefProp(
        PropertyInfo Property,
        Type TargetType,
        string PredicateIri,
        MethodInfo? IriGetter);

    private sealed record InverseRefProp(
        PropertyInfo Property,
        Type TargetType,
        string PredicateIri,
        FieldInfo? BackingField);

    private sealed record CollectionProp(
        PropertyInfo Property,
        Type TargetType,
        string PredicateIri,
        bool IsLazy,
        MethodInfo? IrisGetter);

    private static TypePlan BuildPlan()
    {
        var t = typeof(T);
        var entityAttr = t.GetCustomAttribute<EntityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type {t.FullName} is not decorated with [Entity].");

        // Walk the type chain to find [Identity] — child entities do not declare it themselves.
        var identityAttr = FindAttributeOnTypeOrBases<IdentityAttribute>(t)
            ?? throw new InvalidOperationException(
                $"Type {t.FullName} (or any of its base entity types) is missing the required [Identity(...)] attribute.");

        var strategy = identityAttr.Generator switch
        {
            IdentityGenerator.Random => IdentityStrategy.UuidV4,
            IdentityGenerator.PropertyBasedEncoded => IdentityStrategy.UuidV5,
            _ => IdentityStrategy.Path,
        };

        // EntityPath for type IRI: for subtypes this builds "{parentPath}/{ChildName}" recursively.
        var entityPath = ComputeEntityPathForTypeIri(t);

        // Count ancestor entity types for multi-type rdf:type projection.
        var ancestorCount = CountEntityAncestors(t);

        var guidCtor = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(c => c.GetParameters() is [{ ParameterType: { } p }] && p == typeof(Guid));

        var hydrateIri = typeof(EntityBase).GetMethod("HydrateIri",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("EntityBase.HydrateIri not found.");

        var data = new List<DataProp>();
        var single = new List<RefProp>();
        var coll = new List<CollectionProp>();
        var inverseRefs = new List<InverseRefProp>();

        // PredicatePath for the mapper metadata: use child's own PredicatePath/Path if set.
        var predPath = entityAttr.PredicatePath ?? entityAttr.Path;

        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var owning = prop.GetCustomAttribute<OwningAttribute>();
            var inverse = prop.GetCustomAttribute<InverseAttribute>();
            var idPart = prop.GetCustomAttribute<IdentityPartAttribute>();
            var pred = prop.GetCustomAttribute<PredicateAttribute>();

            // Inverse refs: single refs are hydrated via IInverseRefLoader (ADR-0017);
            // inverse collections are handled by DeferredEntityRefCollectionImpl (ADR-0009).
            if (inverse is not null)
            {
                var (invKind, invTarget) = ClassifyRef(prop.PropertyType);
                if (invKind == RefKindLite.Single && invTarget is not null)
                {
                    // The predicate must resolve to the SAME IRI as the owning side stores,
                    // so use the owning type's PredicatePath (mirrors how owning side resolves it).
                    var owningEntityAttr = invTarget.GetCustomAttribute<EntityAttribute>();
                    var owningPredPath = owningEntityAttr?.PredicatePath ?? owningEntityAttr?.Path;
                    var resolved = PredicateResolver.Resolve(inverse.Predicate, owningPredPath);
                    var backingField = GetFieldFromTypeHierarchy(t, $"__forge_inv_{prop.Name}");
                    inverseRefs.Add(new InverseRefProp(prop, invTarget, resolved, backingField));
                }
                continue; // inverse collections handled by generator-emitted deferred impl
            }

            // Resolve predicate IRI from the DECLARING type to handle inheritance correctly:
            // a property declared on Artist uses Artist's PredicatePath, not FeaturedArtist's.
            var declaringEntityAttr = prop.DeclaringType?.GetCustomAttribute<EntityAttribute>(inherit: false);
            var propPredPath = declaringEntityAttr?.PredicatePath ?? declaringEntityAttr?.Path;

            // Owning ref / collection
            if (owning is not null)
            {
                var (kind, target) = ClassifyRef(prop.PropertyType);
                if (target is null) continue;
                var resolved = PredicateResolver.Resolve(owning.Predicate, propPredPath);
                if (kind == RefKindLite.Single)
                {
                    var refType = typeof(EntityRef<>).MakeGenericType(target);
                    var iriGetter = refType.GetProperty(nameof(EntityRef<EntityBase>.Iri))!.GetMethod;
                    single.Add(new RefProp(prop, target, resolved, iriGetter));
                }
                else
                {
                    var collType = typeof(Forge.Entity.EntityRefCollection<>).MakeGenericType(target);
                    var irisGetter = collType.GetProperty(nameof(Forge.Entity.EntityRefCollection<EntityBase>.Iris))!.GetMethod;
                    coll.Add(new CollectionProp(prop, target, resolved, owning.Lazy, irisGetter));
                }
                continue;
            }

            // Scalar data — only with explicit [Predicate].
            if (pred is not null)
            {
                FieldInfo? partField = null;
                if (idPart is not null)
                    // Search the entire type hierarchy for the backing field — it may be on a base class.
                    partField = GetFieldFromTypeHierarchy(t, $"__forge_part_{prop.Name}");
                var resolved = PredicateResolver.Resolve(pred.Predicate, propPredPath);
                data.Add(new DataProp(prop, resolved, idPart is not null, partField));
            }
        }

        return new TypePlan(strategy, entityPath, predPath, guidCtor, hydrateIri,
            data, single, coll, inverseRefs, ancestorCount);
    }

    // --------------------------------------------------------------- Type-chain helpers

    /// <summary>
    /// Walk the type hierarchy (including <paramref name="t"/> itself) to find the first
    /// attribute of type <typeparamref name="TAttr"/> declared directly on any class in the chain.
    /// Uses <c>inherit: false</c> to match how the generator reads attributes.
    /// </summary>
    private static TAttr? FindAttributeOnTypeOrBases<TAttr>(Type t) where TAttr : Attribute
    {
        var current = t;
        while (current != null && current != typeof(object))
        {
            var attr = current.GetCustomAttribute<TAttr>(inherit: false);
            if (attr is not null) return attr;
            current = current.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Compute the entity path component used for the <c>rdf:type</c> IRI.
    /// For root entities this equals <c>[Entity(Path)]</c> (or the type name if Path is null).
    /// For subtype entities (<c>[Entity]</c> without <c>Path</c>), the path is
    /// <c>{parentPath}/{ChildClassName}</c>, recursively applied.
    /// </summary>
    private static string ComputeEntityPathForTypeIri(Type t)
    {
        var entityAttr = t.GetCustomAttribute<EntityAttribute>(inherit: false);
        if (entityAttr == null) return t.Name;

        // If an explicit Path is set this is the root entity (or a root without a meaningful parent).
        if (!string.IsNullOrEmpty(entityAttr.Path)) return entityAttr.Path!;

        // Walk up to find the nearest entity-annotated ancestor.
        var baseType = t.BaseType;
        while (baseType != null && baseType != typeof(EntityBase) && baseType != typeof(object))
        {
            if (baseType.GetCustomAttribute<EntityAttribute>(inherit: false) != null)
                return $"{ComputeEntityPathForTypeIri(baseType)}/{t.Name}";
            baseType = baseType.BaseType;
        }

        // Root entity with no Path: fall back to the type name.
        return t.Name;
    }

    /// <summary>Count entity-annotated (via <c>[Entity]</c>) types in the base-type chain.</summary>
    private static int CountEntityAncestors(Type t)
    {
        int count = 0;
        var current = t.BaseType;
        while (current != null && current != typeof(EntityBase) && current != typeof(object))
        {
            if (current.GetCustomAttribute<EntityAttribute>(inherit: false) != null)
                count++;
            current = current.BaseType;
        }
        return count;
    }

    /// <summary>
    /// Search <paramref name="t"/> and all base types for a non-public instance field
    /// with the given name.
    /// </summary>
    private static FieldInfo? GetFieldFromTypeHierarchy(Type t, string fieldName)
    {
        var current = t;
        while (current != null && current != typeof(object))
        {
            var f = current.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null) return f;
            current = current.BaseType;
        }
        return null;
    }

    private enum RefKindLite { Single, Collection }

    private static (RefKindLite Kind, Type? Target) ClassifyRef(Type propType)
    {
        var t = Nullable.GetUnderlyingType(propType) ?? propType;
        if (!t.IsGenericType) return (RefKindLite.Single, null);
        var def = t.GetGenericTypeDefinition();
        if (def == typeof(EntityRef<>))
            return (RefKindLite.Single, t.GetGenericArguments()[0]);
        if (def == typeof(Forge.Entity.EntityRefCollection<>))
            return (RefKindLite.Collection, t.GetGenericArguments()[0]);
        return (RefKindLite.Single, null);
    }

    private enum IdentityStrategy { Path, UuidV4, UuidV5 }
}
