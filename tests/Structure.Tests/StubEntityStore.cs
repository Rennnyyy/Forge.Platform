using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Structure.Tests;

/// <summary>
/// Minimal <see cref="IEntityStore"/> stub that serves a fixed set of entities for
/// <see cref="QueryByTypeAsync{T}"/>. All other operations are no-ops.
/// </summary>
internal sealed class StubEntityStore : ITransactionalEntityStore
{
    private readonly IReadOnlyList<object> _entities;

    public string? NamedGraph => null;

    public StubEntityStore(params object[] entities) => _entities = entities;

    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _entities.OfType<T>().ToAsyncEnumerable();

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => new((T?)null);

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => default;

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => default;

    public ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
        => default;

    public ValueTask DisposeAsync() => default;

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => new((T?)null);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => AsyncEnumerable.Empty<string>();
}

/// <summary>
/// Extension helpers for test readability.
/// </summary>
internal static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        => new SyncAsyncEnumerable<T>(source);

    private sealed class SyncAsyncEnumerable<T>(IEnumerable<T> source) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new SyncAsyncEnumerator<T>(source.GetEnumerator());
    }

    private sealed class SyncAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => new(inner.MoveNext());
        public ValueTask DisposeAsync() { inner.Dispose(); return default; }
    }
}
