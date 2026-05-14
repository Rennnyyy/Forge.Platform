using Forge.Execution;
using Forge.Capability.Messaging;
using Shouldly;

namespace Forge.Capability.Messaging.Tests;

/// <summary>
/// Unit tests for <see cref="PendingReplyRegistry{TCommand,TResponse}"/>.
/// Exercises the <see cref="CancellationTokenRegistration"/> disposal fix introduced
/// alongside root ADR-0022.
/// </summary>
public sealed class PendingReplyRegistryTests
{
    private readonly PendingReplyRegistry<PingCommand, PongResponse> _registry = new();

    private static ExecutionResult<PongResponse> OkResult() =>
        new ExecutionResult<PongResponse>.Ok(new PongResponse());

    private sealed class PingCommand;
    private sealed class PongResponse;

    // ── Register / TryComplete ────────────────────────────────────────────────

    [Fact]
    public async Task TryComplete_resolves_the_registered_task_with_the_given_result()
    {
        var id = Guid.NewGuid();
        var task = _registry.Register(id, CancellationToken.None);
        var expected = OkResult();

        var completed = _registry.TryComplete(id, expected);

        completed.ShouldBeTrue();
        var actual = await task;
        actual.ShouldBe(expected);
    }

    [Fact]
    public void TryComplete_returns_false_for_unknown_execution_id()
    {
        _registry.TryComplete(Guid.NewGuid(), OkResult()).ShouldBeFalse();
    }

    [Fact]
    public void TryComplete_returns_false_after_entry_already_completed()
    {
        var id = Guid.NewGuid();
        _registry.Register(id, CancellationToken.None);
        _registry.TryComplete(id, OkResult()).ShouldBeTrue();

        // Second call on the same ID after removal must return false.
        _registry.TryComplete(id, OkResult()).ShouldBeFalse();
    }

    // ── Cancellation before completion ────────────────────────────────────────

    [Fact]
    public async Task Cancelling_token_before_TryComplete_cancels_the_task()
    {
        var id = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var task = _registry.Register(id, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(task);
    }

    [Fact]
    public void TryComplete_returns_false_after_cancellation_removed_the_entry()
    {
        var id = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        _registry.Register(id, cts.Token);

        cts.Cancel();

        // The cancellation callback already removed the entry.
        _registry.TryComplete(id, OkResult()).ShouldBeFalse();
    }

    // ── CancellationTokenRegistration disposal (the bug fix) ─────────────────

    [Fact]
    public async Task Cancelling_token_after_TryComplete_does_not_throw_or_cancel_the_result()
    {
        // Arrange: register, complete successfully, then cancel the token.
        // Before the fix, the cancellation callback would attempt TryRemove on an already-
        // removed entry — harmless but the registration was never disposed, leaking resources.
        // Now the registration is disposed in TryComplete so the callback never fires.
        var id = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var task = _registry.Register(id, cts.Token);
        var expected = OkResult();

        _registry.TryComplete(id, expected);

        // Cancel after completion — must be a no-op; the task result must be the Ok result.
        await cts.CancelAsync();

        var result = await task;
        result.ShouldBe(expected);
        task.IsCanceled.ShouldBeFalse();
    }

    [Fact]
    public async Task Multiple_registrations_are_independent_and_each_completes_separately()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();

        var taskA = _registry.Register(idA, ctsA.Token);
        var taskB = _registry.Register(idB, ctsB.Token);
        var resultA = OkResult();
        var resultB = OkResult();

        _registry.TryComplete(idA, resultA);
        _registry.TryComplete(idB, resultB);

        (await taskA).ShouldBe(resultA);
        (await taskB).ShouldBe(resultB);
    }
}
