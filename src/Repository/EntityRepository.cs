using Forge.Entity;
using System.Runtime.CompilerServices;

namespace Forge.Repository;

/// <summary>Default implementation of <see cref="IEntityRepository{T}"/>.</summary>
public sealed class EntityRepository<T> : IEntityRepository<T> where T : class, IEntity
{
    private readonly IEntityStore _store;

    public EntityRepository(IEntityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ValueTask<T?> FindAsync(string iri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        return _store.LoadAsync<T>(iri, cancellationToken);
    }

    public async ValueTask<T> LoadAsync(string iri, CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(iri, cancellationToken).ConfigureAwait(false);
        return found ?? throw new EntityNotFoundException(typeof(T), iri);
    }

    public async IAsyncEnumerable<T> QueryAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _store.QueryByTypeAsync<T>(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}

/// <summary>Thrown by <see cref="IEntityRepository{T}.LoadAsync"/> when the IRI is absent.</summary>
public sealed class EntityNotFoundException : Exception
{
    public Type EntityType { get; }
    public string Iri { get; }

    public EntityNotFoundException(Type entityType, string iri)
        : base($"Entity of type '{entityType.Name}' with IRI '{iri}' was not found in the store.")
    {
        EntityType = entityType;
        Iri = iri;
    }
}
