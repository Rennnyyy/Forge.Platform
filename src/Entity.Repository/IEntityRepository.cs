namespace Forge.Entity.Repository;

/// <summary>
/// Typed application-facing facade. One per registered entity type. Delegates to
/// <see cref="IEntityStore"/> after type-checking the call.
/// </summary>
public interface IEntityRepository<T> where T : class, IEntity
{
    ValueTask<T?> FindAsync(string iri, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="FindAsync"/> but throws if not found.</summary>
    ValueTask<T> LoadAsync(string iri, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default);

    IAsyncEnumerable<T> QueryAllAsync(CancellationToken cancellationToken = default);
}
