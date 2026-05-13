using Forge.Entity;
using Forge.Execution;

namespace Forge.Entity.Messaging;

/// <summary>
/// Type-erased contract for emitting entity change events for a specific entity type.
/// One implementation per registered entity type. Resolved via DI as
/// <c>IEnumerable&lt;IEntityEventEmitter&gt;</c> and indexed by <see cref="EntityType"/>
/// in <see cref="EntityEventEmitterRegistry"/>.
/// See root ADR-0021.
/// </summary>
internal interface IEntityEventEmitter
{
    /// <summary>The CLR type of the entity this emitter handles.</summary>
    Type EntityType { get; }

    /// <summary>
    /// Emits a create or update event for <paramref name="entity"/>.
    /// The entity must be assignable to <see cref="EntityType"/>.
    /// </summary>
    ValueTask EmitAsync(
        IEntity entity,
        EntityChangeOperation operation,
        string? namedGraph,
        ExecutionCorrelation correlation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a delete event for the entity identified by <paramref name="iri"/>.
    /// The dto in the resulting envelope will be <c>null</c>.
    /// </summary>
    ValueTask EmitDeleteAsync(
        string iri,
        string? namedGraph,
        ExecutionCorrelation correlation,
        CancellationToken cancellationToken = default);
}
