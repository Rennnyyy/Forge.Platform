using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Validation;

/// <summary>
/// Authorization contract for transactions and queries. Implementations decide whether
/// a given agent (identified by <paramref name="agentToken"/>) may perform an operation
/// under a given validation policy (identified by <paramref name="aspectToken"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Default behavior</strong> — use <see cref="AllowAllOperationGuard.Instance"/>
/// to allow every transaction and query unconditionally. Additional authorization is an
/// explicit configuration choice; see Validation ADR-0001.
/// </para>
/// <para>
/// <strong>Denial</strong> — throw any exception to deny. There is no prescribed exception
/// type; implementations choose what is most appropriate for their context.
/// </para>
/// <para>
/// <strong>Agent token</strong> — supply the caller's identity via
/// <see cref="ValidationContext.Use"/>. The guarded store passes
/// <see cref="ValidationContext.CurrentAgentToken"/> (or an empty string when no scope
/// is active) as <paramref name="agentToken"/>; see Validation ADR-0002.
/// </para>
/// </remarks>
public interface IOperationGuard
{
    /// <summary>
    /// Authorizes all operations in a pending transaction <em>before</em> any of them
    /// are applied to the store. If this method returns normally, all operations proceed;
    /// if it throws, the store is never contacted and all operations are discarded.
    /// </summary>
    /// <remarks>
    /// The aspect token for each individual operation is accessible via
    /// <c>operation.Aspect.Name</c> on each element of <paramref name="operations"/>.
    /// </remarks>
    /// <param name="agentToken">The identity token of the calling agent.</param>
    /// <param name="operations">The complete, ordered list of operations to be applied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AuthorizeTransactionAsync(
        string agentToken,
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes a read or query operation before it is executed against the store.
    /// If this method returns normally, the read proceeds; if it throws, the store is
    /// never contacted.
    /// </summary>
    /// <param name="agentToken">The identity token of the calling agent.</param>
    /// <param name="aspectToken">
    /// The name of the validation policy that applies to this query. For basic
    /// <c>LoadAsync</c> / <c>QueryByTypeAsync</c> calls without an explicit query aspect,
    /// the guard receives <c>"noop"</c> (i.e. <see cref="Aspect.NoOp"/>.Name).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AuthorizeQueryAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default);
}
