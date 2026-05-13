using Forge.Entity.Messaging;
using Forge.Messaging.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Forge.Application.Sample;

/// <summary>
/// Background service that consumes <see cref="Book"/> history-topic events from the
/// in-memory message broker and appends them to <see cref="EntityEventLog"/>.
/// <para>
/// Subscribes to <c>forge.entities.book.history</c> — the append-only audit log
/// published by <c>AddForgeEntityMessaging&lt;Book&gt;()</c> after every committed
/// entity transaction.  Every mutation (Create, Update, Delete) produces one entry
/// that is never overwritten.  Exposed via
/// <c>GET /api/diagnostics/entity-events[?iri=…]</c>.
/// </para>
/// See sample ADR-0010 and root ADR-0021.
/// </summary>
internal sealed class BookHistoryConsumerService(
    IMessageConsumer<string, EntityChangedEnvelope<Book>> consumer,
    EntityEventLog log) : BackgroundService
{
    internal const string Topic = "forge.entities.book.history";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in consumer.ConsumeAsync(Topic, stoppingToken).ConfigureAwait(false))
        {
            log.Add(new EntityEventLogEntry(
                EntityIri: envelope.Payload.Iri.ToString(),
                TypeName: envelope.Payload.TypeName,
                Operation: envelope.Payload.Operation.ToString(),
                BranchIri: envelope.Payload.BranchIri,
                Topic: Topic,
                TimestampUtc: envelope.TimestampUtc));
        }
    }
}
