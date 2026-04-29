namespace Forge.Entity;

/// <summary>
/// Optional extension to <see cref="IEntityLoader"/> that resolves the IRI list of a relation
/// collection on demand. Implement alongside <see cref="IEntityLoader"/> to enable deferred
/// (lazy) collections declared with <c>[Owning("predicate", Lazy = true)]</c>.
/// </summary>
public interface ICollectionLoader
{
    /// <summary>
    /// Returns the IRIs of all <typeparamref name="T"/> members owned by
    /// <paramref name="ownerIri"/> via <paramref name="predicate"/>.
    /// Called once per deferred <see cref="EntityRefCollection{T}"/> on first access.
    /// </summary>
    IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri,
        string predicate,
        CancellationToken cancellationToken = default)
        where T : class, IEntity;
}
