namespace Forge.Branch.Merge;

/// <summary>
/// Computes the directional entity-level diff between two named graphs.
/// See Branch ADR-0004.
/// </summary>
public interface IBranchDiffEngine
{
    /// <summary>
    /// Returns an <see cref="EntityGraphDelta"/> describing all entities that exist in
    /// <paramref name="sourceGraphIri"/> and are either absent from or present in
    /// <paramref name="targetGraphIri"/>. Entities present only in the target are not
    /// included (upsert policy — target-only entities are preserved by the merge).
    /// </summary>
    /// <param name="sourceGraphIri">The IRI of the named graph acting as the source of truth.</param>
    /// <param name="targetGraphIri">The IRI of the named graph to compare against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EntityGraphDelta> ComputeDiffAsync(
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken cancellationToken = default);
}
