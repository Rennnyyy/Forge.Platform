using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Validation;

/// <summary>
/// Default <see cref="IOperationGuard"/> implementation that unconditionally authorizes
/// every transaction and query — the "allow all" stub. See Validation ADR-0001.
/// </summary>
/// <remarks>
/// Use <see cref="Instance"/> rather than constructing a new instance. This is the
/// recommended guard when no authorization enforcement is required; it is also the
/// default wired by <c>AddForgeValidation()</c> when no custom guard is supplied.
/// </remarks>
public sealed class AllowAllOperationGuard : IOperationGuard
{
    /// <summary>The singleton allow-all guard instance.</summary>
    public static readonly AllowAllOperationGuard Instance = new();

    private AllowAllOperationGuard() { }

    /// <inheritdoc/>
    /// <remarks>Always returns a completed <see cref="ValueTask"/>; the operations are
    /// never inspected.</remarks>
    public ValueTask AuthorizeTransactionAsync(
        string agentToken,
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    /// <remarks>Always returns a completed <see cref="ValueTask"/>; neither token is
    /// inspected.</remarks>
    public ValueTask AuthorizeQueryAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default)
        => default;
}
