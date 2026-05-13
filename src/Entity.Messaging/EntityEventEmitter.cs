using Forge.Entity;
using Forge.Execution;
using Forge.Messaging.Abstractions;

namespace Forge.Entity.Messaging;

/// <summary>
/// Concrete, strongly-typed <see cref="IEntityEventEmitter"/> for entity type <typeparamref name="TEntity"/>.
/// Publishes <see cref="EntityChangedEnvelope{TEntity}"/> wrapped in a
/// <see cref="MessageEnvelope{TValue}"/> to the history and state topics for this type.
/// See root ADR-0021.
/// </summary>
/// <typeparam name="TEntity">The entity type whose changes this emitter publishes.</typeparam>
internal sealed class EntityEventEmitter<TEntity> : IEntityEventEmitter
    where TEntity : class, IEntity
{
    private readonly IMessageProducer<string, EntityChangedEnvelope<TEntity>> _producer;
    private readonly string _typeName;
    private readonly string _typeIri;
    private readonly string _historyTopic;
    private readonly string _stateTopic;

    public EntityEventEmitter(
        IMessageProducer<string, EntityChangedEnvelope<TEntity>> producer,
        string typeName,
        string typeIri,
        string historyTopic,
        string stateTopic)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateTopic);

        _producer = producer;
        _typeName = typeName;
        _typeIri = typeIri;
        _historyTopic = historyTopic;
        _stateTopic = stateTopic;
    }

    /// <inheritdoc/>
    public Type EntityType => typeof(TEntity);

    /// <inheritdoc/>
    public async ValueTask EmitAsync(
        IEntity entity,
        EntityChangeOperation operation,
        string? namedGraph,
        ExecutionCorrelation correlation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var typedEntity = (TEntity)entity;
        var payload = new EntityChangedEnvelope<TEntity>(
            Iri: typedEntity.Iri,
            TypeName: _typeName,
            TypeIri: _typeIri,
            Operation: operation,
            BranchIri: namedGraph ?? string.Empty,
            Dto: typedEntity,
            Correlation: correlation,
            TimestampUtc: DateTimeOffset.UtcNow);

        await PublishToTopicsAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask EmitDeleteAsync(
        string iri,
        string? namedGraph,
        ExecutionCorrelation correlation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);

        var payload = new EntityChangedEnvelope<TEntity>(
            Iri: iri,
            TypeName: _typeName,
            TypeIri: _typeIri,
            Operation: EntityChangeOperation.Deleted,
            BranchIri: namedGraph ?? string.Empty,
            Dto: null,
            Correlation: correlation,
            TimestampUtc: DateTimeOffset.UtcNow);

        await PublishToTopicsAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async ValueTask PublishToTopicsAsync(
        EntityChangedEnvelope<TEntity> payload,
        CancellationToken cancellationToken)
    {
        // History topic — append-only, infinite retention.
        var historyMsg = new MessageEnvelope<EntityChangedEnvelope<TEntity>>(
            Topic: _historyTopic,
            PartitionKey: payload.Iri,
            Payload: payload,
            Correlation: payload.Correlation,
            TimestampUtc: payload.TimestampUtc);

        // State topic — compacted, latest-wins per IRI.
        // A Kafka implementation would send a null-value tombstone for Deleted; the
        // InMemory implementation sends the envelope with Dto = null to the same channel.
        var stateMsg = new MessageEnvelope<EntityChangedEnvelope<TEntity>>(
            Topic: _stateTopic,
            PartitionKey: payload.Iri,
            Payload: payload,
            Correlation: payload.Correlation,
            TimestampUtc: payload.TimestampUtc);

        await _producer.ProduceAsync(historyMsg, cancellationToken).ConfigureAwait(false);
        await _producer.ProduceAsync(stateMsg, cancellationToken).ConfigureAwait(false);
    }
}
