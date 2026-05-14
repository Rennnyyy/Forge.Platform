using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Branch.Merge;

/// <summary>
/// Orchestrates the full branch merge pipeline: diff → plan → transact.
/// See Branch ADR-0007.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline for <see cref="MergeAsync"/>:
/// </para>
/// <list type="number">
///   <item>Compute diff via <see cref="IBranchDiffEngine"/>.</item>
///   <item>Short-circuit when the delta is empty (no-op).</item>
///   <item>Produce a topologically-ordered <see cref="TransactionOperation"/> list via
///     <see cref="IMergePlanner"/>.</item>
///   <item>Execute the list as a single atomic transaction scoped to the target branch via
///     <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/>.</item>
/// </list>
/// <para>
/// Merge semantics: source overwrites (upsert). Entities present only in the target
/// are untouched. <c>EntityChangedEnvelope</c> events (ADR-0021) are emitted
/// automatically by the <c>EventEmittingEntityStore</c> decorator during
/// <c>ExecuteTransactionAsync</c>.
/// </para>
/// <para>
/// Registered as a <b>scoped</b> service by
/// <c>BranchServiceCollectionExtensions.AddForgeBranch()</c>.
/// </para>
/// </remarks>
public sealed class BranchMergeService
{
    private readonly IBranchDiffEngine _diffEngine;
    private readonly IMergePlanner _planner;
    private readonly ITransactionalEntityStore _store;

    public BranchMergeService(
        IBranchDiffEngine diffEngine,
        IMergePlanner planner,
        ITransactionalEntityStore store)
    {
        ArgumentNullException.ThrowIfNull(diffEngine);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(store);
        _diffEngine = diffEngine;
        _planner = planner;
        _store = store;
    }

    /// <summary>
    /// Merges all entities from <paramref name="sourceBranchIri"/> into
    /// <paramref name="targetBranchIri"/> as a single atomic transaction.
    /// </summary>
    /// <param name="sourceBranchIri">IRI of the source named graph (read-only during merge).</param>
    /// <param name="targetBranchIri">IRI of the target named graph to be updated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BranchMergeResult"/> describing the number of created and updated entities.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceBranchIri"/> equals <paramref name="targetBranchIri"/>
    /// or either is null/whitespace.
    /// </exception>
    /// <exception cref="MergePlanUnresolvableTypeException">
    /// Propagated from <see cref="IMergePlanner"/> when an entity type is not registered.
    /// </exception>
    /// <exception cref="MergePlanHydrationException">
    /// Propagated from <see cref="IMergePlanner"/> when an entity cannot be loaded from the source.
    /// </exception>
    /// <exception cref="MergePlanCycleException">
    /// Propagated from <see cref="IMergePlanner"/> when a circular owning dependency is detected.
    /// </exception>
    public async Task<BranchMergeResult> MergeAsync(
        string sourceBranchIri,
        string targetBranchIri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBranchIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranchIri);
        if (string.Equals(sourceBranchIri, targetBranchIri, StringComparison.Ordinal))
            throw new ArgumentException(
                "Source and target branch IRIs must be different.",
                nameof(targetBranchIri));

        // 1. Compute diff.
        var delta = await _diffEngine
            .ComputeDiffAsync(sourceBranchIri, targetBranchIri, cancellationToken)
            .ConfigureAwait(false);

        // 2. Short-circuit on empty diff.
        if (delta.IsEmpty)
            return BranchMergeResult.Empty(sourceBranchIri, targetBranchIri);

        // 3. Plan — hydrate source entities, topo-sort.
        // The same store is used for both source reads and target existence checks;
        // BranchScope inside PlanAsync scopes each read to the correct named graph.
        var operations = await _planner
            .PlanAsync(delta, _store, _store, cancellationToken)
            .ConfigureAwait(false);

        // 4. Execute as a single transaction scoped to the target branch.
        using (BranchScope.Use(targetBranchIri))
        {
            await _store
                .ExecuteTransactionAsync(operations, cancellationToken)
                .ConfigureAwait(false);
        }

        // 5. Build result counts from operation types.
        int created = 0, updated = 0;
        foreach (var op in operations)
        {
            if (op is EntityWriteOperation write)
            {
                if (write.Mode == WriteMode.Create) created++;
                else updated++;
            }
        }

        return new BranchMergeResult(sourceBranchIri, targetBranchIri, created, updated);
    }
}
