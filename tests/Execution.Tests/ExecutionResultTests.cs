using Forge.Execution;
using Shouldly;

namespace Forge.Execution.Tests;

public sealed class ExecutionResultTests
{
    private sealed record MyResponse(string Value);

    [Fact]
    public void Ok_carries_response_and_empty_events_by_default()
    {
        var result = new ExecutionResult<MyResponse>.Ok(new MyResponse("hello"));

        result.Response.Value.ShouldBe("hello");
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Ok_with_events_round_trips()
    {
        var result = new ExecutionResult<MyResponse>.Ok(new MyResponse("world"))
        {
            Events = ["event-1", 42],
        };

        result.Events.Count.ShouldBe(2);
        result.Events[0].ShouldBe("event-1");
        result.Events[1].ShouldBe(42);
    }

    [Fact]
    public void Fail_carries_error_and_empty_events_by_default()
    {
        var result = new ExecutionResult<MyResponse>.Fail(
            new ExecutionError("NOT_FOUND", "Resource not found"));

        result.Error.Code.ShouldBe("NOT_FOUND");
        result.Error.Message.ShouldBe("Resource not found");
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Pattern_match_distinguishes_ok_from_fail()
    {
        ExecutionResult<MyResponse> ok   = new ExecutionResult<MyResponse>.Ok(new MyResponse("v"));
        ExecutionResult<MyResponse> fail = new ExecutionResult<MyResponse>.Fail(
            new ExecutionError("ERR", "bad"));

        var okBranch = ok switch
        {
            ExecutionResult<MyResponse>.Ok o   => o.Response.Value,
            ExecutionResult<MyResponse>.Fail f => f.Error.Code,
            _                                  => "unexpected",
        };
        var failBranch = fail switch
        {
            ExecutionResult<MyResponse>.Ok o   => o.Response.Value,
            ExecutionResult<MyResponse>.Fail f => f.Error.Code,
            _                                  => "unexpected",
        };

        okBranch.ShouldBe("v");
        failBranch.ShouldBe("ERR");
    }
}
