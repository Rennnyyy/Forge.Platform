namespace Forge.Structure;

/// <summary>
/// The caller-supplied configuration that describes the active variant to resolve.
/// Passed to <see cref="StructureScope.Use"/> to make it ambient for an async call chain,
/// and consumed by <see cref="ConditionSet.IsSatisfiedBy"/> during tree traversal.
/// See Variant ADR-0003.
/// </summary>
/// <param name="BranchIri">
/// IRI of the named graph (branch) from which the structure tree is read.
/// An empty string signals "use the default branch" — the caller is responsible for
/// resolving the default branch IRI before opening a <see cref="StructureScope"/>.
/// </param>
/// <param name="Options">
/// Maps variant dimension IRI → the value selected by the caller for that dimension.
/// Dimensions not present in this dictionary are treated as "unspecified". Whether an
/// unspecified dimension satisfies a condition is governed by
/// <see cref="FlagOptionCondition.IsRequired"/> /
/// <see cref="EnumerationOptionCondition.IsRequired"/> (see ADR-0002).
/// </param>
/// <param name="ReferenceDate">
/// Evaluation instant for <see cref="TimeCondition"/>. When <c>null</c>,
/// <see cref="DateTimeOffset.UtcNow"/> is used at evaluation time.
/// </param>
public sealed record StructureConfiguration(
    string BranchIri,
    IReadOnlyDictionary<string, OptionValue> Options,
    DateTimeOffset? ReferenceDate = null);
