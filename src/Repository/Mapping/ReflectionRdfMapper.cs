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

    public T? Hydrate(string iri, RdfGraph subjectGraph)
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
        sink.Add(new RdfTriple(subj, RdfVocab.RdfTypeIri, RdfTerm.Iri(typeIri)));

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
        IReadOnlyList<CollectionProp> OwningCollections);

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
        var identityAttr = t.GetCustomAttribute<IdentityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type {t.FullName} is missing the required [Identity(...)] attribute.");

        var strategy = identityAttr.Generator switch
        {
            IdentityGenerator.Random => IdentityStrategy.UuidV4,
            IdentityGenerator.PropertyBasedEncoded => IdentityStrategy.UuidV5,
            _ => IdentityStrategy.Path,
        };

        var guidCtor = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(c => c.GetParameters() is [{ ParameterType: { } p }] && p == typeof(Guid));

        var hydrateIri = typeof(EntityBase).GetMethod("HydrateIri",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("EntityBase.HydrateIri not found.");

        var data = new List<DataProp>();
        var single = new List<RefProp>();
        var coll = new List<CollectionProp>();

        var predPath = entityAttr.PredicatePath ?? entityAttr.Path;

        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var owning = prop.GetCustomAttribute<OwningAttribute>();
            var inverse = prop.GetCustomAttribute<InverseAttribute>();
            var idPart = prop.GetCustomAttribute<IdentityPartAttribute>();
            var pred = prop.GetCustomAttribute<PredicateAttribute>();

            // Inverse refs/collections are not projected, not hydrated (ADR-0013, v1).
            if (inverse is not null) continue;

            // Owning ref / collection
            if (owning is not null)
            {
                var (kind, target) = ClassifyRef(prop.PropertyType);
                if (target is null) continue;
                var resolved = PredicateResolver.Resolve(owning.Predicate, predPath);
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
                    partField = t.GetField($"__forge_part_{prop.Name}",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                var resolved = PredicateResolver.Resolve(pred.Predicate, predPath);
                data.Add(new DataProp(prop, resolved, idPart is not null, partField));
            }
        }

        return new TypePlan(strategy, entityAttr.Path, predPath, guidCtor, hydrateIri,
            data, single, coll);
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
