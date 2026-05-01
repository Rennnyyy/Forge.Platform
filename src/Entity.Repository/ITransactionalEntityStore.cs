namespace Forge.Entity.Repository;

/// <summary>
/// Opt-in extension of <see cref="IEntityStore"/> for stores that support
/// multi-operation ACID transactions. See Entity ADR-0015.
/// </summary>
/// <remarks>
/// Implementations:
/// <list type="bullet">
///   <item><see cref="Forge.Entity.Repository.InMemory.InMemoryEntityStore"/> — SemaphoreSlim
///         mutual exclusion + snapshot/restore rollback (see InMemory ADR-0001).</item>
///   <item><c>GraphDbEntityStore</c> — GraphDB REST Transactions API (see GraphDb ADR-0002).</item>
/// </list>
/// </remarks>
public interface ITransactionalEntityStore : IEntityStore
{
    /// <summary>
    /// Executes <paramref name="operations"/> as a single ACID transaction.
    /// All operations are applied or none are (atomicity). Concurrent transactions
    /// are isolated from one another.
    /// </summary>
    /// <param name="operations">The ordered list of operations to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default);
}
