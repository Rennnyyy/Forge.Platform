using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Branch.Tests;

/// <summary>
/// Test double for <see cref="ITransactionalEntityStore"/> that records method invocations.
/// All write operations succeed silently; reads return null/empty.
/// </summary>
internal class CapturingStore : ITransactionalEntityStore
{
    public bool ExecuteTransactionCalled { get; private set; }
    public bool DeleteAsyncCalled { get; private set; }
    public string? NamedGraphValue { get; set; }

    /// <summary>All operations batches submitted via <see cref="ExecuteTransactionAsync"/>.</summary>
    public List<IReadOnlyList<TransactionOperation>> CapturedOperations { get; } = new();

    public string? NamedGraph => NamedGraphValue;

    public ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ExecuteTransactionCalled = true;
        CapturedOperations.Add(operations);
        return default;
    }

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => new((T?)null);

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => default;

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        DeleteAsyncCalled = true;
        return default;
    }

    public virtual IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => AsyncEnumerable.Empty<T>();

    public ValueTask DisposeAsync() => default;

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => new((T?)null);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => AsyncEnumerable.Empty<string>();
}
