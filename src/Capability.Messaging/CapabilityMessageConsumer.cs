using Forge.Execution;
using Forge.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Capability.Messaging;

/// <summary>
/// Default implementation of <see cref="ICapabilityMessageConsumer{TCommand,TResponse}"/>.
/// Creates a DI scope per message so that scoped handler dependencies (e.g. repository
/// services) are properly managed. Publishes a <see cref="CapabilityReplyEnvelope{TResponse}"/>
/// when the originating command envelope requested a reply. See root ADR-0022.
/// </summary>
internal sealed class CapabilityMessageConsumer<TCommand, TResponse>
    : ICapabilityMessageConsumer<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageProducer<string, CapabilityReplyEnvelope<TResponse>> _replyProducer;

    public CapabilityMessageConsumer(
        IServiceScopeFactory scopeFactory,
        IMessageProducer<string, CapabilityReplyEnvelope<TResponse>> replyProducer)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(replyProducer);
        _scopeFactory = scopeFactory;
        _replyProducer = replyProducer;
    }

    /// <inheritdoc/>
    public async ValueTask ConsumeOneAsync(
        MessageEnvelope<CapabilityCommandEnvelope<TCommand>> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var cmd = envelope.Payload;

        // Restore the originating correlation for the duration of handler execution.
        using var _ = ExecutionScope.Use(cmd.Correlation);

        // Create a scope per message so scoped handler dependencies are properly resolved.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<ICapabilityDispatcher<TCommand, TResponse>>();

        var result = await dispatcher.DispatchAsync(
            cmd.Command, cmd.AspectIri, cancellationToken).ConfigureAwait(false);

        if (cmd.ReplyToTopic is not null)
        {
            var replyPayload = new CapabilityReplyEnvelope<TResponse>(
                Result: result,
                Correlation: cmd.Correlation,
                TimestampUtc: DateTimeOffset.UtcNow);

            var replyEnvelope = new MessageEnvelope<CapabilityReplyEnvelope<TResponse>>(
                Topic: cmd.ReplyToTopic,
                PartitionKey: cmd.Correlation.ExecutionId.ToString(),
                Payload: replyPayload,
                Correlation: cmd.Correlation,
                TimestampUtc: replyPayload.TimestampUtc);

            await _replyProducer.ProduceAsync(replyEnvelope, cancellationToken)
                                .ConfigureAwait(false);
        }
    }
}
