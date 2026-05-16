using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="TimeCondition"/>: open windows, closed windows,
/// boundary inclusion, and ReferenceDate defaulting.
/// </summary>
public sealed class TimeConditionTests
{
    private static StructureConfiguration ConfigAtDate(DateTimeOffset date) =>
        new(BranchIri: "https://forge-it.net/branches/main",
            Options: new Dictionary<string, OptionValue>(),
            ReferenceDate: date);

    private static readonly StructureConfiguration NoDate = new(
        BranchIri: "https://forge-it.net/branches/main",
        Options: new Dictionary<string, OptionValue>(),
        ReferenceDate: null);

    [Fact]
    public void Open_window_both_null_is_always_satisfied()
    {
        var condition = new TimeCondition(validFrom: null, validTo: null);

        condition.IsSatisfiedBy(NoDate).ShouldBeTrue();
    }

    [Fact]
    public void Date_before_ValidFrom_is_not_satisfied()
    {
        var from = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: from, validTo: null);
        var early = ConfigAtDate(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        condition.IsSatisfiedBy(early).ShouldBeFalse();
    }

    [Fact]
    public void Date_equal_to_ValidFrom_is_satisfied()
    {
        var from = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: from, validTo: null);

        condition.IsSatisfiedBy(ConfigAtDate(from)).ShouldBeTrue();
    }

    [Fact]
    public void Date_after_ValidFrom_is_satisfied()
    {
        var from = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: from, validTo: null);
        var after = ConfigAtDate(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        condition.IsSatisfiedBy(after).ShouldBeTrue();
    }

    [Fact]
    public void Date_after_ValidTo_is_not_satisfied()
    {
        var to = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: null, validTo: to);
        var after = ConfigAtDate(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        condition.IsSatisfiedBy(after).ShouldBeFalse();
    }

    [Fact]
    public void Date_equal_to_ValidTo_is_satisfied()
    {
        var to = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: null, validTo: to);

        condition.IsSatisfiedBy(ConfigAtDate(to)).ShouldBeTrue();
    }

    [Fact]
    public void Date_inside_closed_window_is_satisfied()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var inside = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: from, validTo: to);

        condition.IsSatisfiedBy(ConfigAtDate(inside)).ShouldBeTrue();
    }

    [Fact]
    public void Date_outside_closed_window_is_not_satisfied()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var outside = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(validFrom: from, validTo: to);

        condition.IsSatisfiedBy(ConfigAtDate(outside)).ShouldBeFalse();
    }

    [Fact]
    public void Null_ReferenceDate_uses_UtcNow_and_satisfies_open_window()
    {
        var condition = new TimeCondition(validFrom: null, validTo: null);

        // Open window must always pass regardless of current time
        condition.IsSatisfiedBy(NoDate).ShouldBeTrue();
    }

    [Fact]
    public void ValidFrom_and_ValidTo_are_stored_correctly()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var condition = new TimeCondition(from, to);

        condition.ValidFrom.ShouldBe(from);
        condition.ValidTo.ShouldBe(to);
    }
}
