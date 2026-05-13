using Forge.Execution;

namespace Forge.Capability.Messaging;

/// <summary>
/// Envelope published by <see cref="ICapabilityMessageConsumer{TCommand,TResponse}"/>
/// onto the reply topic after the capability handler returns.
/// Received by <see cref="CapabilityReplyListener{TCommand,TResponse}"/> which completes
/// the corresponding <see cref="PendingReplyRegistry{TCommand,TResponse}"/> entry.
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="Result">The handler outcome (Ok or Fail).</param>
/// <param name="Correlation">
/// Correlation echoed from the originating command envelope.
/// Used by the reply listener to locate the pending <see cref="System.Threading.Tasks.TaskCompletionSource{TResult}"/>.
/// </param>
/// <param name="TimestampUtc">UTC timestamp at which the reply envelope was created.</param>
public sealed record CapabilityReplyEnvelope<TResponse>(
    ExecutionResult<TResponse> Result,
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc)
    where TResponse : class;
