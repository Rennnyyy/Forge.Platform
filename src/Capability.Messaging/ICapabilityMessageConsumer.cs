using Forge.Messaging.Abstractions;

namespace Forge.Capability.Messaging;

/// <summary>
/// Handles a single inbound command message: delegates to the in-process
/// <see cref="ICapabilityDispatcher{TCommand,TResponse}"/> and — when the command
/// envelope carries a <see cref="CapabilityCommandEnvelope{TCommand}.ReplyToTopic"/> —
/// publishes a <see cref="CapabilityReplyEnvelope{TResponse}"/> back.
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICapabilityMessageConsumer<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    /// <summary>
    /// Processes one command envelope:
    /// <list type="number">
    ///   <item>Restores the originating <see cref="Forge.Execution.ExecutionCorrelation"/> as the ambient correlation.</item>
    ///   <item>Calls <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/>.</item>
    ///   <item>Publishes a <see cref="CapabilityReplyEnvelope{TResponse}"/> when <see cref="CapabilityCommandEnvelope{TCommand}.ReplyToTopic"/> is non-null.</item>
    /// </list>
    /// All existing aspect validation, <see cref="Forge.Capability.CapabilityContext"/> event
    /// collection, and <see cref="Forge.Execution.ExecutionResult{TResponse}"/> semantics execute
    /// inside the handler — unchanged.
    /// </summary>
    ValueTask ConsumeOneAsync(
        MessageEnvelope<CapabilityCommandEnvelope<TCommand>> envelope,
        CancellationToken cancellationToken = default);
}
