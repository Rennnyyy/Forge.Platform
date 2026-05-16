using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="EnumerationOptionCondition"/>: matching, mismatching,
/// absent-dimension open-world vs. required, ordinal IRI comparison, and wrong value type.
/// </summary>
public sealed class EnumerationOptionConditionTests
{
    private const string ColorDimension = "https://forge-it.net/dimensions/color";
    private const string RedIri = "https://forge-it.net/colors/red";
    private const string BlueIri = "https://forge-it.net/colors/blue";
    private const string OtherDimension = "https://forge-it.net/dimensions/other";

    private static StructureConfiguration WithEnum(string dimensionIri, string valueIri) =>
        new(BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>
            {
                [dimensionIri] = new EnumerationOptionValue(valueIri)
            });

    private static readonly StructureConfiguration Empty = new(
        BranchIri: "https://forge-it.net/branches/main",
        Options: new Dictionary<string, OptionValue>());

    [Fact]
    public void Condition_satisfied_when_value_iri_matches()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri);

        condition.IsSatisfiedBy(WithEnum(ColorDimension, RedIri)).ShouldBeTrue();
    }

    [Fact]
    public void Condition_not_satisfied_when_different_value_selected()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri);

        condition.IsSatisfiedBy(WithEnum(ColorDimension, BlueIri)).ShouldBeFalse();
    }

    [Fact]
    public void Absent_dimension_is_satisfied_when_not_required()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri, isRequired: false);

        condition.IsSatisfiedBy(Empty).ShouldBeTrue();
    }

    [Fact]
    public void Absent_dimension_is_not_satisfied_when_required()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri, isRequired: true);

        condition.IsSatisfiedBy(Empty).ShouldBeFalse();
    }

    [Fact]
    public void Different_dimension_does_not_satisfy_required_condition()
    {
        // Condition requires ColorDimension; config only provides OtherDimension.
        // With IsRequired = true, the absent dimension must not be satisfied.
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri, isRequired: true);
        var config = WithEnum(OtherDimension, RedIri);

        condition.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void Different_dimension_satisfies_optional_condition()
    {
        // Condition requires ColorDimension (optional); config only provides OtherDimension.
        // Open-world default: absent dimension = don't care → satisfied.
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri, isRequired: false);
        var config = WithEnum(OtherDimension, RedIri);

        condition.IsSatisfiedBy(config).ShouldBeTrue();
    }

    [Fact]
    public void Iri_comparison_is_ordinal_case_sensitive()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri);
        var config = WithEnum(ColorDimension, RedIri.ToUpperInvariant());

        condition.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void Wrong_value_type_flag_does_not_satisfy_enum_condition()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri);
        var config = new StructureConfiguration(
            BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>
            {
                [ColorDimension] = new FlagOptionValue(true)
            });

        condition.IsSatisfiedBy(config).ShouldBeFalse();
    }

    [Fact]
    public void Properties_are_stored_correctly()
    {
        var condition = new EnumerationOptionCondition(ColorDimension, RedIri, isRequired: true);

        condition.DimensionIri.ShouldBe(ColorDimension);
        condition.EnumerationValueIri.ShouldBe(RedIri);
        condition.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void Null_or_empty_DimensionIri_throws()
    {
        Should.Throw<ArgumentException>(() => new EnumerationOptionCondition(string.Empty, RedIri));
        Should.Throw<ArgumentException>(() => new EnumerationOptionCondition(null!, RedIri));
    }

    [Fact]
    public void Null_or_empty_EnumerationValueIri_throws()
    {
        Should.Throw<ArgumentException>(() => new EnumerationOptionCondition(ColorDimension, string.Empty));
        Should.Throw<ArgumentException>(() => new EnumerationOptionCondition(ColorDimension, null!));
    }
}
