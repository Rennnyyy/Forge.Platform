using Forge.Execution;

namespace Forge.Capability.Messaging;

/// <summary>
/// Envelope published by an <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}"/>
/// onto the command topic. Carries everything the consumer side needs to re-invoke the
/// capability handler without any ambient context from the producer process.
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <param name="Command">The command payload.</param>
/// <param name="Correlation">
/// Correlation from the originating dispatch.
/// <see cref="ExecutionCorrelation.ExecutionId"/> doubles as the correlation key for
/// matching replies back to their <see cref="PendingReplyRegistry{TCommand,TResponse}"/> entry.
/// </param>
/// <param name="AspectIri">
/// Optional aspect IRI forwarded to the in-process dispatcher on the consumer side.
/// <c>null</c> means fully permissive execution.
/// </param>
/// <param name="ReplyToTopic">
/// Topic onto which the consumer must publish the <see cref="CapabilityReplyEnvelope{TResponse}"/>.
/// <c>null</c> for fire-and-forget dispatches; the consumer skips reply publication.
/// </param>
/// <param name="TimestampUtc">UTC timestamp at which the envelope was created.</param>
public sealed record CapabilityCommandEnvelope<TCommand>(
    TCommand Command,
    ExecutionCorrelation Correlation,
    string? AspectIri,
    string? ReplyToTopic,
    DateTimeOffset TimestampUtc)
    where TCommand : class;
