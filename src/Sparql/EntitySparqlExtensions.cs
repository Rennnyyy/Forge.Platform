using Forge.Entity;
using Forge.Repository;

namespace Forge.Sparql;

/// <summary>
/// Entry-points for the LINQ-to-SPARQL provider. See ADR-0001 of this slice.
/// </summary>
public static class EntitySparqlExtensions
{
    /// <summary>
    /// Open a typed <see cref="IQueryable{T}"/> rooted at <paramref name="store"/>. The
    /// store must additionally implement <see cref="ISparqlQueryStore"/> (the back-end's
    /// SPARQL execution capability). Throws <see cref="NotSupportedException"/> otherwise.
    /// </summary>
    public static IQueryable<T> Query<T>(this IEntityStore store) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store is not ISparqlQueryStore sparql)
            throw new NotSupportedException(
                $"Entity store of type '{store.GetType().FullName}' does not implement " +
                $"ISparqlQueryStore. LINQ queries require a SPARQL-capable backend.");
        var provider = new SparqlQueryProvider<T>(store, sparql);
        return new SparqlQueryable<T>(provider);
    }
}
