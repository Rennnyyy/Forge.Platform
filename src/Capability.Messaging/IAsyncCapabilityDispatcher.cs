using Forge.Execution;

namespace Forge.Capability.Messaging;

/// <summary>
/// Publishes capability commands to a broker topic asynchronously.
/// Supports both fire-and-forget and request-reply dispatch patterns.
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IAsyncCapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    /// <summary>
    /// Publishes <paramref name="command"/> to the configured command topic and returns
    /// immediately. No reply is awaited.
    /// </summary>
    /// <param name="command">The command to publish.</param>
    /// <param name="aspectIri">
    /// Optional aspect IRI forwarded to the consumer-side dispatcher.
    /// <c>null</c> for permissive execution.
    /// </param>
    /// <param name="cancellationToken">Propagated to the broker producer.</param>
    ValueTask PublishAsync(
        TCommand command,
        string? aspectIri = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes <paramref name="command"/> to the configured command topic and waits
    /// for the consumer-side reply to arrive on the configured reply topic.
    /// </summary>
    /// <param name="command">The command to publish.</param>
    /// <param name="aspectIri">
    /// Optional aspect IRI forwarded to the consumer-side dispatcher.
    /// </param>
    /// <param name="timeout">
    /// Maximum time to wait for the reply. When no reply arrives before the timeout
    /// expires the returned result is <see cref="ExecutionResult{TResponse}.Fail"/>
    /// with error code <c>BrokerReplyTimeout</c>. Defaults to 30 seconds when
    /// <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels both the broker produce call and the reply wait.
    /// </param>
    Task<ExecutionResult<TResponse>> PublishAndWaitAsync(
        TCommand command,
        string? aspectIri = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
