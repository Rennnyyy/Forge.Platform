namespace Forge.Branch.Merge;

/// <summary>
/// The result of a directional diff between two named graphs, expressed as a list
/// of entity-level changes from source to target. See Branch ADR-0004.
/// </summary>
/// <param name="SourceGraphIri">The named graph used as the source of truth.</param>
/// <param name="TargetGraphIri">The named graph being compared against.</param>
/// <param name="Entries">
/// All entities present in the source graph that are either absent from or present in
/// the target graph. Entities present only in the target are not included (upsert policy).
/// </param>
public sealed record EntityGraphDelta(
    string SourceGraphIri,
    string TargetGraphIri,
    IReadOnlyList<EntityDeltaEntry> Entries)
{
    /// <summary>True when no entities in the source graph differ from the target.</summary>
    public bool IsEmpty => Entries.Count == 0;
}
