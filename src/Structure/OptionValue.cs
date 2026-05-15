namespace Forge.Structure;

/// <summary>
/// A discriminated-union value representing the caller-specified value for one variant
/// dimension within a <see cref="StructureConfiguration"/>.
/// </summary>
/// <remarks>
/// The hierarchy is closed: only <see cref="FlagOptionValue"/> and
/// <see cref="EnumerationOptionValue"/> are valid subtypes. A new value kind
/// requires a new Variant ADR.
/// </remarks>
public abstract class OptionValue
{
    private protected OptionValue() { }
}

/// <summary>
/// A boolean variant value for a flag-type option dimension.
/// </summary>
public sealed class FlagOptionValue : OptionValue
{
    /// <summary>The flag value selected by the caller.</summary>
    public bool Value { get; }

    /// <param name="value">The Boolean flag selection.</param>
    public FlagOptionValue(bool value) => Value = value;
}

/// <summary>
/// An enumeration variant value for an enumeration-type option dimension. The selected
/// value is represented as an IRI corresponding to one of the enumeration's named individuals.
/// </summary>
public sealed class EnumerationOptionValue : OptionValue
{
    /// <summary>IRI of the selected enumeration individual.</summary>
    public string ValueIri { get; }

    /// <param name="valueIri">IRI of the selected enumeration individual. Must not be null or whitespace.</param>
    public EnumerationOptionValue(string valueIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueIri);
        ValueIri = valueIri;
    }
}
