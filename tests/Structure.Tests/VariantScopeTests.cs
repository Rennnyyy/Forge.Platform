using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="StructureScope"/>: ambient current value, nested
/// scopes, disposal restoration, and null-before-open guard.
/// </summary>
public sealed class StructureScopeTests
{
    private static StructureConfiguration MakeConfig(string branch = "https://forge-it.net/branches/main") =>
        new(BranchIri: branch, Options: new Dictionary<string, OptionValue>());

    [Fact]
    public void Current_is_null_before_any_scope_is_opened()
    {
        // This test assumes no ambient scope from a prior test bleeds over;
        // AsyncLocal is per-ExecutionContext so xUnit task isolation guarantees this.
        StructureScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Current_reflects_active_configuration_inside_scope()
    {
        var config = MakeConfig();

        using (StructureScope.Use(config))
        {
            StructureScope.Current.ShouldBeSameAs(config);
        }
    }

    [Fact]
    public void Current_returns_to_null_after_scope_is_disposed()
    {
        var config = MakeConfig();

        using (StructureScope.Use(config))
        {
            _ = StructureScope.Current;
        }

        StructureScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Nested_scope_overrides_outer_scope()
    {
        var outer = MakeConfig("https://forge-it.net/branches/outer");
        var inner = MakeConfig("https://forge-it.net/branches/inner");

        using (StructureScope.Use(outer))
        {
            StructureScope.Current.ShouldBeSameAs(outer);

            using (StructureScope.Use(inner))
            {
                StructureScope.Current.ShouldBeSameAs(inner);
            }

            StructureScope.Current.ShouldBeSameAs(outer);
        }

        StructureScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Scope_restores_previous_configuration_on_dispose()
    {
        var first  = MakeConfig("https://forge-it.net/branches/first");
        var second = MakeConfig("https://forge-it.net/branches/second");

        using var outerScope = StructureScope.Use(first);
        var innerScope = StructureScope.Use(second);
        innerScope.Dispose();

        StructureScope.Current.ShouldBeSameAs(first);

        outerScope.Dispose();
        StructureScope.Current.ShouldBeNull();
    }

    [Fact]
    public void Use_throws_when_configuration_is_null()
    {
        Should.Throw<ArgumentNullException>(() => StructureScope.Use(null!));
    }

    [Fact]
    public async Task Current_flows_into_async_continuation()
    {
        var config = MakeConfig();

        using (StructureScope.Use(config))
        {
            await Task.Yield();
            StructureScope.Current.ShouldBeSameAs(config);
        }
    }
}
