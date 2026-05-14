namespace Forge.Branch.Merge;

/// <summary>
/// The result of a <see cref="BranchMergeService.MergeAsync"/> operation. See Branch ADR-0007.
/// </summary>
/// <param name="SourceBranchIri">The IRI of the branch used as the merge source.</param>
/// <param name="TargetBranchIri">The IRI of the branch that was updated.</param>
/// <param name="CreatedCount">Number of entities that were new to the target and were created.</param>
/// <param name="UpdatedCount">Number of entities that already existed in the target and were replaced.</param>
public sealed record BranchMergeResult(
    string SourceBranchIri,
    string TargetBranchIri,
    int CreatedCount,
    int UpdatedCount)
{
    /// <summary>Total number of entity mutations applied.</summary>
    public int TotalCount => CreatedCount + UpdatedCount;

    /// <summary>True when no entities were mutated (source and target were already identical).</summary>
    public bool IsEmpty => TotalCount == 0;

    /// <summary>Constructs a result representing a no-op merge (empty diff).</summary>
    public static BranchMergeResult Empty(string sourceBranchIri, string targetBranchIri) =>
        new(sourceBranchIri, targetBranchIri, 0, 0);
}
