using Forge.Execution;
using Shouldly;

namespace Forge.Execution.Tests;

public sealed class ExecutionScopeTests
{
    [Fact]
    public void Current_is_null_without_active_scope()
    {
        ExecutionScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Use_sets_current_and_dispose_clears_it()
    {
        var correlation = new ExecutionCorrelation { CallerCorrelationId = Guid.NewGuid() };

        using (ExecutionScope.Use(correlation))
        {
            ExecutionScope.Current.ShouldBeSameAs(correlation);
        }

        ExecutionScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Use_throws_for_null_correlation()
    {
        Should.Throw<ArgumentNullException>(() => ExecutionScope.Use(null!));
    }

    [Fact]
    public async Task Scope_is_preserved_across_await_boundaries()
    {
        var correlation = new ExecutionCorrelation();
        using (ExecutionScope.Use(correlation))
        {
            await Task.Yield();
            ExecutionScope.Current.ShouldBeSameAs(correlation);
        }

        ExecutionScope.Current.ShouldBeNull();
    }

    [Fact]
    public async Task Nested_scopes_on_independent_async_flows_do_not_bleed()
    {
        ExecutionCorrelation? innerSeen = null;

        var outerCorrelation = new ExecutionCorrelation();
        using (ExecutionScope.Use(outerCorrelation))
        {
            var innerTask = Task.Run(async () =>
            {
                var innerCorrelation = new ExecutionCorrelation();
                using (ExecutionScope.Use(innerCorrelation))
                {
                    await Task.Yield();
                    innerSeen = ExecutionScope.Current;
                }
            });

            await innerTask;

            // Outer scope must be unaffected by the inner Task.Run scope.
            ExecutionScope.Current.ShouldBeSameAs(outerCorrelation);
        }

        innerSeen.ShouldNotBeNull();
        innerSeen.ShouldNotBeSameAs(outerCorrelation);
    }
}
