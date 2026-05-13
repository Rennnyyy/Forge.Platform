using Forge.Execution;
using Forge.Messaging.Abstractions;

namespace Forge.Capability.Messaging;

/// <summary>
/// Default implementation of <see cref="ICapabilityMessageConsumer{TCommand,TResponse}"/>.
/// Delegates to the in-process <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>
/// and publishes a <see cref="CapabilityReplyEnvelope{TResponse}"/> when the originating
/// command envelope requested a reply. See root ADR-0022.
/// </summary>
internal sealed class CapabilityMessageConsumer<TCommand, TResponse>
    : ICapabilityMessageConsumer<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly ICapabilityDispatcher<TCommand, TResponse> _dispatcher;
    private readonly IMessageProducer<string, CapabilityReplyEnvelope<TResponse>> _replyProducer;

    public CapabilityMessageConsumer(
        ICapabilityDispatcher<TCommand, TResponse> dispatcher,
        IMessageProducer<string, CapabilityReplyEnvelope<TResponse>> replyProducer)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(replyProducer);
        _dispatcher = dispatcher;
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

        var result = await _dispatcher.DispatchAsync(
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
