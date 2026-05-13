using Forge.Capability;
using Forge.Execution;

namespace Forge.Application.Sample;

// ── AsyncProcess  POST /api/capabilities/demo/async-process ──────────────────

/// <summary>
/// Command for the async capability messaging demonstration.
/// </summary>
/// <param name="Input">Arbitrary string the caller wants processed.</param>
public sealed record AsyncProcessCommand(string Input);

/// <summary>
/// Response returned by the <see cref="AsyncProcessHandler"/> after it handles
/// the command, whether dispatched directly via HTTP or via the async message bus.
/// </summary>
/// <param name="Result">Echo of the input with a processing marker.</param>
/// <param name="CorrelationId">
/// The <see cref="ExecutionCorrelation.ExecutionId"/> from the execution scope,
/// proving that the correlation propagated correctly across the broker boundary
/// in the async-messaging path.
/// </param>
/// <param name="ProcessedAt">UTC instant at which the handler ran.</param>
public sealed record AsyncProcessResponse(
    string Result,
    string CorrelationId,
    DateTimeOffset ProcessedAt);

/// <summary>
/// Simple capability handler that echoes its input back with metadata.
/// <para>
/// Used in two ways in Application.Sample (see sample ADR-0011):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Direct HTTP dispatch</b> — the handler is reached synchronously through the
///     normal Capability.Http pipeline:
///     <c>POST /api/capabilities/demo/async-process</c>
///   </item>
///   <item>
///     <b>Async message-bus dispatch</b> — a command is published to
///     <c>forge.capabilities.demo.async-process.commands</c> via
///     <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}"/>.
///     The generic <c>CapabilityCommandPumpService</c> (wired automatically by
///     <c>AddForgeCapabilityMessaging()</c>) picks it up, calls
///     <see cref="ICapabilityMessageConsumer{TCommand,TResponse}.ConsumeOneAsync"/>,
///     which delegates to this handler and publishes the reply back to
///     <c>forge.capabilities.demo.async-process.replies</c>.
///     The generic <c>CapabilityReplyPumpService</c> then completes the in-process
///     <see cref="PendingReplyRegistry{TCommand,TResponse}"/> entry, unblocking the
///     caller of <c>PublishAndWaitAsync</c>.
///   </item>
/// </list>
/// </summary>
[Capability("demo.async-process")]
public sealed class AsyncProcessHandler : ICapabilityHandler<AsyncProcessCommand, AsyncProcessResponse>
{
    public ValueTask<ExecutionResult<AsyncProcessResponse>> HandleAsync(
        AsyncProcessCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var response = new AsyncProcessResponse(
            Result: $"processed: {command.Input}",
            CorrelationId: ExecutionScope.Current?.ExecutionId.ToString() ?? "(no correlation)",
            ProcessedAt: DateTimeOffset.UtcNow);

        return ValueTask.FromResult<ExecutionResult<AsyncProcessResponse>>(
            new ExecutionResult<AsyncProcessResponse>.Ok(response));
    }
}
