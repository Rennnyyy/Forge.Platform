using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="ConditionSet"/>: AND evaluation, vacuous truth for
/// empty sets, and short-circuit on first unsatisfied condition.
/// </summary>
public sealed class ConditionSetTests
{
    private static readonly StructureConfiguration AnyConfig = new(
        BranchIri: "https://forge-it.net/branches/main",
        Options: new Dictionary<string, OptionValue>());

    [Fact]
    public void Empty_ConditionSet_is_always_satisfied()
    {
        ConditionSet.Empty.IsSatisfiedBy(AnyConfig).ShouldBeTrue();
    }

    [Fact]
    public void ConditionSet_with_no_conditions_is_satisfied()
    {
        var set = new ConditionSet([]);

        set.IsSatisfiedBy(AnyConfig).ShouldBeTrue();
    }

    [Fact]
    public void ConditionSet_is_satisfied_when_all_conditions_pass()
    {
        var refDate = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var set = new ConditionSet([
            new TimeCondition(
                validFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                validTo:   new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            new TimeCondition(validFrom: null, validTo: null) // always true
        ]);

        var config = new StructureConfiguration(
            BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>(),
            ReferenceDate: refDate);

        set.IsSatisfiedBy(config).ShouldBeTrue();
    }

    [Fact]
    public void ConditionSet_is_not_satisfied_when_any_condition_fails()
    {
        var past = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var set = new ConditionSet([
            new TimeCondition(validFrom: null, validTo: null), // always true
            new TimeCondition(
                validFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                validTo:   null) // requires 2025+
        ]);

        var config = new StructureConfiguration(
            BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>(),
            ReferenceDate: past);

        set.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void ConditionSet_exposes_conditions_list()
    {
        var c1 = new TimeCondition(null, null);
        var set = new ConditionSet([c1]);

        set.Conditions.ShouldContain(c1);
        set.Conditions.Count.ShouldBe(1);
    }

    [Fact]
    public void ConditionSet_throws_when_config_is_null()
    {
        var set = new ConditionSet([]);

        Should.Throw<ArgumentNullException>(() => set.IsSatisfiedBy(null!));
    }
}
