using Forge.Execution;
using Shouldly;

namespace Forge.Execution.Tests;

public sealed class ExecutionErrorTests
{
    [Fact]
    public void Constructor_sets_code_and_message()
    {
        var error = new ExecutionError("NOT_FOUND", "Resource not found");

        error.Code.ShouldBe("NOT_FOUND");
        error.Message.ShouldBe("Resource not found");
    }

    [Fact]
    public void Record_equality_is_value_based()
    {
        var a = new ExecutionError("CODE", "msg");
        var b = new ExecutionError("CODE", "msg");
        var c = new ExecutionError("OTHER", "msg");

        (a == b).ShouldBeTrue();
        (a == c).ShouldBeFalse();
    }
}
