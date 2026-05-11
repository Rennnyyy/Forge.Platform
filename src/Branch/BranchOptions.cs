namespace Forge.Branch;

/// <summary>
/// Configuration options for the branch management subsystem.
/// Bound from the <c>Forge:Branch</c> configuration section.
/// See Branch ADR-0001.
/// </summary>
public sealed class BranchOptions
{
    /// <summary>
    /// IRI of the named graph that holds all <see cref="Branch"/> entity metadata.
    /// All branch-management reads and writes target this graph exclusively.
    /// Default: <c>https://forge-it.net/management</c>.
    /// </summary>
    public string ManagementGraphIri { get; set; } = "https://forge-it.net/management";

    /// <summary>
    /// IRI of the default branch named graph. Used by <c>BranchScope</c>-unaware
    /// callers and by store implementations when no ambient scope is active.
    /// Default: <c>https://forge-it.net/branches/main</c>.
    /// </summary>
    public string DefaultBranchIri { get; set; } = "https://forge-it.net/branches/main";
}
