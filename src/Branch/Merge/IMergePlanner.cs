using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Branch.Merge;

/// <summary>
/// Hydrates source entities from a <see cref="EntityGraphDelta"/>, determines
/// create-vs-update against the target graph, and returns a topologically ordered
/// list of <see cref="TransactionOperation"/> instances safe for a single
/// <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/> call.
/// See Branch ADR-0006.
/// </summary>
public interface IMergePlanner
{
    /// <summary>
    /// Produces an ordered list of <see cref="TransactionOperation"/> instances from
    /// <paramref name="delta"/>. Each entry is hydrated from <paramref name="sourceStore"/>
    /// (scoped to <c>delta.SourceGraphIri</c>) and checked for existence in
    /// <paramref name="targetStore"/> (scoped to <c>delta.TargetGraphIri</c>) to decide
    /// between <c>CreateOperation&lt;T&gt;</c> and <c>UpdateOperation&lt;T&gt;</c>.
    /// Operations are sorted so that an entity with <c>[Owning]</c> references to other
    /// entities in the same batch is written <em>after</em> its owned targets.
    /// </summary>
    /// <exception cref="MergePlanCycleException">
    /// Thrown when a circular owning-dependency is detected. This indicates a
    /// corrupted entity graph; circular owning is forbidden by the mapper.
    /// </exception>
    /// <exception cref="MergePlanHydrationException">
    /// Thrown when an entity IRI in <paramref name="delta"/> cannot be loaded from
    /// <paramref name="sourceStore"/>.
    /// </exception>
    /// <exception cref="MergePlanUnresolvableTypeException">
    /// Thrown when an <c>rdf:type</c> IRI in <paramref name="delta"/> has no registered
    /// mapper. Ensure the entity type is registered at DI time.
    /// </exception>
    Task<IReadOnlyList<TransactionOperation>> PlanAsync(
        EntityGraphDelta delta,
        IEntityStore sourceStore,
        IEntityStore targetStore,
        CancellationToken cancellationToken = default);
}
