namespace Forge.Capability.Messaging;

/// <summary>
/// Configuration for a single <typeparamref name="TCommand"/>/<typeparamref name="TResponse"/>
/// capability messaging registration. See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class CapabilityMessagingOptions<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    /// <summary>
    /// Topic on which commands are published by the producer and consumed by the consumer.
    /// Must be set before the service provider is built.
    /// </summary>
    public string CommandTopic { get; set; } = string.Empty;

    /// <summary>
    /// Topic onto which the consumer publishes reply envelopes after handling a command.
    /// Required for request-reply dispatch (<see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}.PublishAndWaitAsync"/>).
    /// May be left empty when only fire-and-forget dispatch is used.
    /// </summary>
    public string ReplyTopic { get; set; } = string.Empty;

    /// <summary>
    /// Default timeout applied by <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}.PublishAndWaitAsync"/>
    /// when the caller does not supply an explicit timeout. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultReplyTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
