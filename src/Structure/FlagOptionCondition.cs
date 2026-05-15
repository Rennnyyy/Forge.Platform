using System.Text.Json.Serialization;

namespace Forge.Structure;

/// <summary>
/// A boolean-flag applicability condition. A <see cref="Usage"/> carrying this condition
/// is active when the <see cref="StructureConfiguration.Options"/> dictionary contains a
/// <see cref="FlagOptionValue"/> for <see cref="DimensionIri"/> whose
/// <see cref="FlagOptionValue.Value"/> equals <see cref="ExpectedValue"/>.
/// <para>
/// When the dimension is absent from the configuration, the behaviour depends on
/// <see cref="IsRequired"/>: if <see langword="false"/> (default, "open world"), the
/// condition is considered satisfied; if <see langword="true"/>, it is not satisfied.
/// See Variant ADR-0002.
/// </para>
/// </summary>
public sealed class FlagOptionCondition : IStructureCondition
{
    /// <summary>IRI that identifies the boolean option dimension.</summary>
    public string DimensionIri { get; }

    /// <summary>The flag value that must be selected for this condition to be satisfied.</summary>
    public bool ExpectedValue { get; }

    /// <summary>
    /// When <see langword="true"/>, the condition fails if <see cref="DimensionIri"/> is
    /// absent from the configuration. When <see langword="false"/> (default), an absent
    /// dimension is treated as "don't care" and the condition is satisfied.
    /// </summary>
    public bool IsRequired { get; }

    /// <param name="dimensionIri">IRI of the boolean option dimension. Must not be null or whitespace.</param>
    /// <param name="expectedValue">The required flag value.</param>
    /// <param name="isRequired">Whether the dimension must be present in the configuration.</param>
    [JsonConstructor]
    public FlagOptionCondition(string dimensionIri, bool expectedValue, bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimensionIri);
        DimensionIri = dimensionIri;
        ExpectedValue = expectedValue;
        IsRequired = isRequired;
    }

    /// <inheritdoc />
    public bool IsSatisfiedBy(StructureConfiguration config)
    {
        if (!config.Options.TryGetValue(DimensionIri, out var value))
            return !IsRequired;

        return value is FlagOptionValue flag && flag.Value == ExpectedValue;
    }
}
