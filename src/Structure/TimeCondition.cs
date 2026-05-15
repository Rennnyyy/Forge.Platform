using System.Text.Json.Serialization;

namespace Forge.Structure;

/// <summary>
/// A time-window applicability condition. A <see cref="Usage"/> carrying this condition
/// is active when the <see cref="StructureConfiguration.ReferenceDate"/> (or
/// <see cref="DateTimeOffset.UtcNow"/> when not specified) falls within the window
/// <c>[<see cref="ValidFrom"/>, <see cref="ValidTo"/>]</c>.
/// <para>
/// Both bounds are optional (open). A null <see cref="ValidFrom"/> means "from the
/// beginning of time"; a null <see cref="ValidTo"/> means "until the end of time".
/// A <see cref="TimeCondition"/> with both bounds null is always satisfied.
/// </para>
/// <para>
/// This is a v1 implementation using absolute <see cref="DateTimeOffset"/> values.
/// A future ADR will extend this to support snapshot-lineage windows coupled to
/// <c>Forge.Branch</c>. See Variant ADR-0002.
/// </para>
/// </summary>
public sealed class TimeCondition : IStructureCondition
{
    /// <summary>Inclusive start of the valid window. Null = open start.</summary>
    public DateTimeOffset? ValidFrom { get; }

    /// <summary>Inclusive end of the valid window. Null = open end.</summary>
    public DateTimeOffset? ValidTo { get; }

    /// <param name="validFrom">Inclusive start of the valid window, or <c>null</c> for open start.</param>
    /// <param name="validTo">Inclusive end of the valid window, or <c>null</c> for open end.</param>
    [JsonConstructor]
    public TimeCondition(DateTimeOffset? validFrom, DateTimeOffset? validTo)
    {
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    /// <inheritdoc />
    public bool IsSatisfiedBy(StructureConfiguration config)
    {
        var reference = config.ReferenceDate ?? DateTimeOffset.UtcNow;
        if (ValidFrom.HasValue && reference < ValidFrom.Value)
            return false;
        if (ValidTo.HasValue && reference > ValidTo.Value)
            return false;
        return true;
    }
}
