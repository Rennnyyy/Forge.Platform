using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="FlagOptionCondition"/>: matching, mismatching,
/// absent-dimension open-world vs. required semantics, and wrong value type.
/// </summary>
public sealed class FlagOptionConditionTests
{
    private const string EvDimension = "https://forge-it.net/dimensions/ev";
    private const string OtherDimension = "https://forge-it.net/dimensions/other";

    private static StructureConfiguration WithFlag(string dimensionIri, bool value) =>
        new(BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>
            {
                [dimensionIri] = new FlagOptionValue(value)
            });

    private static readonly StructureConfiguration Empty = new(
        BranchIri: "https://forge-it.net/branches/main",
        Options: new Dictionary<string, OptionValue>());

    [Fact]
    public void Condition_satisfied_when_flag_matches_expected_true()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true);

        condition.IsSatisfiedBy(WithFlag(EvDimension, true)).ShouldBeTrue();
    }

    [Fact]
    public void Condition_satisfied_when_flag_matches_expected_false()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: false);

        condition.IsSatisfiedBy(WithFlag(EvDimension, false)).ShouldBeTrue();
    }

    [Fact]
    public void Condition_not_satisfied_when_flag_is_opposite()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true);

        condition.IsSatisfiedBy(WithFlag(EvDimension, false)).ShouldBeFalse();
    }

    [Fact]
    public void Absent_dimension_is_satisfied_when_not_required()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true, isRequired: false);

        condition.IsSatisfiedBy(Empty).ShouldBeTrue();
    }

    [Fact]
    public void Absent_dimension_is_not_satisfied_when_required()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true, isRequired: true);

        condition.IsSatisfiedBy(Empty).ShouldBeFalse();
    }

    [Fact]
    public void Different_dimension_does_not_satisfy_required_condition()
    {
        // Condition requires EvDimension; config only provides OtherDimension.
        // With IsRequired = true, the absent dimension must not be satisfied.
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true, isRequired: true);
        var config = WithFlag(OtherDimension, true);

        condition.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void Different_dimension_satisfies_optional_condition()
    {
        // Condition requires EvDimension (optional); config only provides OtherDimension.
        // Open-world default: absent dimension = don't care → satisfied.
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true, isRequired: false);
        var config = WithFlag(OtherDimension, true);

        condition.IsSatisfiedBy(config).ShouldBeTrue();
    }

    [Fact]
    public void Wrong_value_type_enumeration_does_not_satisfy_flag_condition()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: true);
        var config = new StructureConfiguration(
            BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>
            {
                [EvDimension] = new EnumerationOptionValue("https://forge-it.net/ev/yes")
            });

        condition.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void DimensionIri_and_ExpectedValue_are_stored_correctly()
    {
        var condition = new FlagOptionCondition(EvDimension, expectedValue: false, isRequired: true);

        condition.DimensionIri.ShouldBe(EvDimension);
        condition.ExpectedValue.ShouldBeFalse();
        condition.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void Null_or_empty_DimensionIri_throws()
    {
        Should.Throw<ArgumentException>(() => new FlagOptionCondition(string.Empty, true));
        Should.Throw<ArgumentException>(() => new FlagOptionCondition(null!, true));
    }
}
