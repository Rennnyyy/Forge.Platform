using System.Reflection;
using Forge.Entity.Repository;

namespace Forge.Entity.Sparql;

/// <summary>
/// Per-type read-only map that drives the LINQ-to-SPARQL translator: discovers the
/// <c>[Predicate]</c>-annotated scalar data properties of <typeparamref name="T"/> and
/// resolves the entity's <c>rdf:type</c> IRI.
/// </summary>
/// <remarks>
/// Mirrors the data-property scan performed by <c>ReflectionRdfMapper&lt;T&gt;</c> but
/// keeps the read-only metadata accessible to non-Repository callers. Built once per
/// type and cached.
/// </remarks>
internal sealed class EntityPredicateMap<T> where T : class, IEntity
{
    private static readonly Lazy<EntityPredicateMap<T>> _instance = new(Build);
    public static EntityPredicateMap<T> Instance => _instance.Value;

    public string TypeIri { get; }
    public IReadOnlyDictionary<string, PropertyBinding> Properties { get; }

    private EntityPredicateMap(string typeIri, IReadOnlyDictionary<string, PropertyBinding> props)
    {
        TypeIri = typeIri;
        Properties = props;
    }

    public bool TryGet(string propertyName, out PropertyBinding binding) =>
        Properties.TryGetValue(propertyName, out binding!);

    private static EntityPredicateMap<T> Build()
    {
        var t = typeof(T);
        var entityAttr = t.GetCustomAttribute<EntityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type {t.FullName} is not decorated with [Entity]; it cannot be queried.");

        var predPath = entityAttr.PredicatePath ?? entityAttr.Path;

        // Match Repository's default type-IRI resolution.
        var typeIri = new EntityRepositoryOptions().ResolveTypeIri(t.Name, entityAttr.Path);

        var dict = new Dictionary<string, PropertyBinding>(StringComparer.Ordinal);
        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var pred = prop.GetCustomAttribute<PredicateAttribute>();
            if (pred is null) continue;
            var iri = PredicateResolverShim.Resolve(pred.Predicate, predPath);
            dict[prop.Name] = new PropertyBinding(prop.Name, iri, prop.PropertyType);
        }

        return new EntityPredicateMap<T>(typeIri, dict);
    }
}

/// <summary>One queryable property of an entity.</summary>
internal sealed record PropertyBinding(string Name, string PredicateIri, Type ClrType)
{
    /// <summary>Stable SPARQL variable name used to bind this property's value.</summary>
    public string Variable => "v_" + Name;
}

/// <summary>
/// Predicate-IRI resolver that mirrors <c>Forge.Entity.Repository.PredicateResolver</c>'s
/// rules without taking an internal dependency on it.
/// </summary>
internal static class PredicateResolverShim
{
    public static string Resolve(string declared, string? predicatePath)
    {
        if (string.IsNullOrWhiteSpace(declared))
            throw new ArgumentException("Predicate must not be empty.", nameof(declared));
        if (declared.Contains(':')) return declared;
        var basePart = EntityOptions.Current.PredicateBaseIri.TrimEnd('/');
        if (string.IsNullOrEmpty(predicatePath))
            return $"{basePart}/{declared}";
        return $"{basePart}/{predicatePath!.Trim('/')}/{declared}";
    }
}
