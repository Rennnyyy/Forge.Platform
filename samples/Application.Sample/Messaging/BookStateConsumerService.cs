using Forge.Entity.Messaging;
using Forge.Messaging.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Forge.Application.Sample;

/// <summary>
/// Background service that consumes <see cref="Book"/> state-topic events from the
/// in-memory message broker and upserts them into <see cref="EntityStateCache"/>.
/// <para>
/// Subscribes to <c>forge.entities.book.state</c>.  This topic models a Kafka
/// compacted log: for each entity IRI (partition key) only the most recent event
/// is retained.  <see cref="EntityStateCache"/> mirrors that semantic in-process —
/// every inbound event overwrites the previous value for the same IRI.
/// </para>
/// <para>
/// Exposed via <c>GET /api/diagnostics/entity-events/latest?iri=…</c>.
/// </para>
/// See sample ADR-0010 and root ADR-0021.
/// </summary>
internal sealed class BookStateConsumerService(
    IMessageConsumer<string, EntityChangedEnvelope<Book>> consumer,
    EntityStateCache cache) : BackgroundService
{
    internal const string Topic = "forge.entities.book.state";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in consumer.ConsumeAsync(Topic, stoppingToken).ConfigureAwait(false))
        {
            cache.Upsert(new EntityEventLogEntry(
                EntityIri: envelope.Payload.Iri.ToString(),
                TypeName: envelope.Payload.TypeName,
                Operation: envelope.Payload.Operation.ToString(),
                BranchIri: envelope.Payload.BranchIri,
                Topic: Topic,
                TimestampUtc: envelope.TimestampUtc));
        }
    }
}
