namespace Forge.Branch.Merge;

/// <summary>
/// Describes how an entity differs between the source and target named graph.
/// See Branch ADR-0004.
/// </summary>
public enum EntityDeltaKind
{
    /// <summary>The entity exists in the source graph but not in the target graph.</summary>
    Added,

    /// <summary>The entity exists in both graphs; the source version will overwrite the target.</summary>
    Modified,
}
