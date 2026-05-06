using Forge.Execution;
using Shouldly;

namespace Forge.Execution.Tests;

public sealed class ExecutionCorrelationTests
{
    [Fact]
    public void Default_constructs_with_new_execution_id_and_null_caller()
    {
        var a = new ExecutionCorrelation();
        var b = new ExecutionCorrelation();

        a.ExecutionId.ShouldNotBe(Guid.Empty);
        b.ExecutionId.ShouldNotBe(Guid.Empty);
        a.ExecutionId.ShouldNotBe(b.ExecutionId);
        a.CallerCorrelationId.ShouldBeNull();
    }

    [Fact]
    public void CallerCorrelationId_can_be_set()
    {
        var id = Guid.NewGuid();
        var correlation = new ExecutionCorrelation
        {
            CallerCorrelationId = id,
        };

        correlation.CallerCorrelationId.ShouldBe(id);
    }
}
