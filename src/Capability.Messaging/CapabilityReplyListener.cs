using Forge.Execution;
using Forge.Messaging.Abstractions;

namespace Forge.Capability.Messaging;

/// <summary>
/// Listens on the reply topic and routes arriving
/// <see cref="CapabilityReplyEnvelope{TResponse}"/> messages to the in-process
/// <see cref="PendingReplyRegistry{TCommand,TResponse}"/>, completing the awaiting
/// <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}.PublishAndWaitAsync"/> calls.
/// <para>
/// The hosting loop is owned by the application (or a hosted service wrapper).
/// The SDK does not force any particular hosting model — call <see cref="ListenAsync"/>
/// from an <c>IHostedService.ExecuteAsync</c> or similar.
/// </para>
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type (used for DI type discrimination).</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class CapabilityReplyListener<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly IMessageConsumer<string, CapabilityReplyEnvelope<TResponse>> _consumer;
    private readonly PendingReplyRegistry<TCommand, TResponse> _registry;
    private readonly CapabilityMessagingOptions<TCommand, TResponse> _options;

    internal CapabilityReplyListener(
        IMessageConsumer<string, CapabilityReplyEnvelope<TResponse>> consumer,
        PendingReplyRegistry<TCommand, TResponse> registry,
        CapabilityMessagingOptions<TCommand, TResponse> options)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        _consumer = consumer;
        _registry = registry;
        _options = options;
    }

    /// <summary>
    /// Starts the reply-consumer loop. Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task ListenAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var msg in _consumer.ConsumeAsync(_options.ReplyTopic, cancellationToken)
                                           .ConfigureAwait(false))
        {
            var reply = msg.Payload;
            _registry.TryComplete(reply.Correlation.ExecutionId, reply.Result);
        }
    }
}
