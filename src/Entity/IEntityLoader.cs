namespace Forge.Entity;

/// <summary>
/// Loads entities by IRI on demand. Implementations are typically backed by a triple store,
/// repository, or in-memory registry (used in tests via NSubstitute).
/// </summary>
public interface IEntityLoader
{
    /// <summary>
    /// Resolve an entity of type <typeparamref name="T"/> by its IRI.
    /// Returns <c>null</c> if the entity is known to be absent in the underlying store.
    /// </summary>
    ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity;
}
