using Forge.Entity;

namespace Forge.Branch;

/// <summary>
/// An immutable, versioned snapshot of a named graph. A subtype of <see cref="Branch"/>:
/// it shares the same IRI path (<c>/branches/{Name}</c>), identity strategy, and
/// management-graph storage, but its named graph content is frozen after creation and
/// protected by <see cref="SnapshotGuardedTransactionalStore"/>.
///
/// All SemVer properties are optional. At least <see cref="SnapshotAt"/> must be set by
/// the application service at creation time. SemVer uniqueness per source branch is
/// enforced by <c>BranchSeedingService</c> (Branch ADR-0003), not here.
///
/// See Branch ADR-0002.
/// </summary>
[Entity(PredicatePath = "snapshot")]
public partial class Snapshot : Branch
{
    /// <summary>
    /// Logical moment the snapshot content was frozen. May differ from
    /// <see cref="Branch.CreatedAt"/> when representing a past point in time.
    /// Always set by the application service at creation time.
    /// </summary>
    [Predicate("snapshotAt")]
    public DateTimeOffset SnapshotAt { get; init; }

    /// <summary>Semantic version major component. Null when no SemVer bound is declared.</summary>
    [Predicate("semVerMajor")]
    public int? SemVerMajor { get; init; }

    /// <summary>Semantic version minor component. Null when no SemVer bound is declared.</summary>
    [Predicate("semVerMinor")]
    public int? SemVerMinor { get; init; }

    /// <summary>Semantic version patch component. Null when no SemVer bound is declared.</summary>
    [Predicate("semVerPatch")]
    public int? SemVerPatch { get; init; }

    /// <summary>
    /// Semantic version pre-release label (e.g. <c>"alpha.1"</c>).
    /// Null when no pre-release tag is declared.
    /// </summary>
    [Predicate("semVerPreRelease")]
    public string? SemVerPreRelease { get; init; }
}
