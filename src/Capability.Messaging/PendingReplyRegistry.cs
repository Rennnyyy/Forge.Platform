using System.Collections.Concurrent;
using Forge.Execution;

namespace Forge.Capability.Messaging;

/// <summary>
/// In-process registry that maps an <see cref="ExecutionCorrelation.ExecutionId"/> to a
/// <see cref="TaskCompletionSource{TResult}"/> waiting for the corresponding capability
/// reply. Created per <typeparamref name="TCommand"/>/<typeparamref name="TResponse"/>
/// pair and registered as a singleton by the DI extension.
/// <para>
/// Thread-safe. <see cref="CapabilityReplyListener{TCommand,TResponse}"/> calls
/// <see cref="TryComplete"/> when a reply arrives; the awaiting caller receives it via
/// the <see cref="Task{TResult}"/> returned by <see cref="Register"/>.
/// </para>
/// See root ADR-0022.
/// </summary>
/// <typeparam name="TCommand">The command type (used only for DI type discrimination).</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class PendingReplyRegistry<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly ConcurrentDictionary<Guid, (TaskCompletionSource<ExecutionResult<TResponse>> Tcs, CancellationTokenRegistration Registration)>
        _pending = new();

    /// <summary>
    /// Registers a pending reply for the given <paramref name="executionId"/> and returns
    /// a <see cref="Task{TResult}"/> that completes when <see cref="TryComplete"/> is called
    /// with the same id. Cancellation via <paramref name="cancellationToken"/> removes the
    /// registration and cancels the task.
    /// </summary>
    public Task<ExecutionResult<TResponse>> Register(Guid executionId, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ExecutionResult<TResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(executionId, out var removed))
            {
                removed.Registration.Dispose();
                removed.Tcs.TrySetCanceled(cancellationToken);
            }
        });

        _pending[executionId] = (tcs, registration);

        return tcs.Task;
    }

    /// <summary>
    /// Completes the pending <see cref="Task{TResult}"/> registered under
    /// <paramref name="executionId"/> with <paramref name="result"/>.
    /// Returns <c>false</c> when no matching registration exists (e.g. already timed out).
    /// </summary>
    public bool TryComplete(Guid executionId, ExecutionResult<TResponse> result)
    {
        if (_pending.TryRemove(executionId, out var entry))
        {
            entry.Registration.Dispose();
            entry.Tcs.TrySetResult(result);
            return true;
        }

        return false;
    }
}
