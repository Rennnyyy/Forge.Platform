using System.Text.Json.Serialization;

namespace Forge.Structure;

/// <summary>
/// An enumeration-value applicability condition. A <see cref="Usage"/> carrying this
/// condition is active when the <see cref="StructureConfiguration.Options"/> dictionary
/// contains an <see cref="EnumerationOptionValue"/> for <see cref="DimensionIri"/>
/// whose <see cref="EnumerationOptionValue.ValueIri"/> equals
/// <see cref="EnumerationValueIri"/> (ordinal comparison).
/// <para>
/// When the dimension is absent from the configuration, the behaviour depends on
/// <see cref="IsRequired"/>: if <see langword="false"/> (default, "open world"), the
/// condition is satisfied; if <see langword="true"/>, it is not. See Variant ADR-0002.
/// </para>
/// </summary>
public sealed class EnumerationOptionCondition : IStructureCondition
{
    /// <summary>IRI that identifies the enumeration option dimension.</summary>
    public string DimensionIri { get; }

    /// <summary>IRI of the enumeration individual that must be selected.</summary>
    public string EnumerationValueIri { get; }

    /// <summary>
    /// When <see langword="true"/>, the condition fails if <see cref="DimensionIri"/> is
    /// absent from the configuration. When <see langword="false"/> (default), an absent
    /// dimension is treated as "don't care" and the condition is satisfied.
    /// </summary>
    public bool IsRequired { get; }

    /// <param name="dimensionIri">IRI of the enumeration option dimension. Must not be null or whitespace.</param>
    /// <param name="enumerationValueIri">IRI of the required enumeration individual. Must not be null or whitespace.</param>
    /// <param name="isRequired">Whether the dimension must be present in the configuration.</param>
    [JsonConstructor]
    public EnumerationOptionCondition(string dimensionIri, string enumerationValueIri, bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimensionIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(enumerationValueIri);
        DimensionIri = dimensionIri;
        EnumerationValueIri = enumerationValueIri;
        IsRequired = isRequired;
    }

    /// <inheritdoc />
    public bool IsSatisfiedBy(StructureConfiguration config)
    {
        if (!config.Options.TryGetValue(DimensionIri, out var value))
            return !IsRequired;

        return value is EnumerationOptionValue enumValue &&
               string.Equals(enumValue.ValueIri, EnumerationValueIri, StringComparison.Ordinal);
    }
}
