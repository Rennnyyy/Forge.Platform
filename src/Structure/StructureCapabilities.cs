using Forge.Capability;
using Forge.Execution;
using Forge.Repository;

namespace Forge.Structure;

// ═══════════════════════════════════════════════════════════════════════════
// GetConfiguredTreeCapability
// POST /api/capabilities/structure/configured-tree/get
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Command that specifies the tree root and the variant selection.
/// <para>
/// Flag options and enumeration options are expressed as flat dictionaries to avoid
/// polymorphic JSON deserialization on the HTTP surface. The handler reconstructs the
/// full <see cref="StructureConfiguration"/> internally.
/// </para>
/// </summary>
public sealed record GetConfiguredTreeCommand(
    string StructureHeadIri,
    string BranchIri,
    IReadOnlyDictionary<string, bool>? FlagOptions = null,
    IReadOnlyDictionary<string, string>? EnumerationOptions = null,
    DateTimeOffset? ReferenceDate = null);

/// <summary>
/// A single node in the configured tree returned by <see cref="GetConfiguredTreeHandler"/>.
/// <para>
/// <see cref="SnapshotBranchIri"/> is non-null when the <see cref="Usage"/> edge that
/// led to this node carried a <c>SnapshotIri</c> value (or when an ancestor edge did
/// and no descendant edge overrode it). Callers should fetch associated context
/// (geometry, metadata) for this node from that named graph instead of the query's
/// default branch. See Structure ADR-0006.
/// </para>
/// </summary>
public sealed record StructureNodeDto(
    string Iri,
    IReadOnlyList<StructureNodeDto> Children,
    string? SnapshotBranchIri = null);

/// <summary>
/// Response returned by <see cref="GetConfiguredTreeHandler"/>.
/// <para>
/// <see cref="Root"/> is the full recursive tree.
/// <see cref="AllNodeIris"/> is a de-duplicated flat list of every IRI that appears
/// in the tree — provided as a convenience for assertion in Bruno and unit tests.
/// </para>
/// </summary>
public sealed record GetConfiguredTreeResponse(
    StructureNodeDto Root,
    IReadOnlyList<string> AllNodeIris);

/// <summary>
/// Builds the configured structure tree for the given <see cref="GetConfiguredTreeCommand"/>.
/// <para>
/// Algorithm:
/// <list type="number">
///   <item>Reconstruct <see cref="StructureConfiguration"/> from the flat command fields.</item>
///   <item>
///     Activate <see cref="StructureScope.Use"/> so that
///     <see cref="IEntityStore.QueryByTypeAsync{T}"/> for <see cref="Usage"/> entities
///     transparently returns only the edges whose <see cref="Usage.Conditions"/> are
///     satisfied by the current configuration.
///   </item>
///   <item>Group the filtered edges into a parent → children adjacency lookup.</item>
///   <item>
///     Depth-first traversal from <see cref="GetConfiguredTreeCommand.StructureHeadIri"/>
///     with backtracking cycle detection: a node already on the current DFS path is
///     returned as a childless sentinel; once recursion returns, the node is removed from
///     the path set so it may appear in other DAG branches (diamond sharing).
///   </item>
///   <item>Collect all unique node IRIs into <see cref="GetConfiguredTreeResponse.AllNodeIris"/>.</item>
/// </list>
/// </para>
/// <remarks>
/// <para>
/// Condition evaluation is pure in-memory C#. <see cref="StructureFilteringStore"/>
/// calls <see cref="ConditionSet.IsSatisfiedBy"/> on each <see cref="Usage"/> edge after
/// fetching them from the backing store. There is no SPARQL FILTER involved.
/// </para>
/// <para>
/// Because <see cref="Usage.Conditions"/> is not annotated with <c>[Predicate]</c>, conditions
/// are not persisted to the RDF triple store. They are preserved in the InMemory backend across
/// the lifetime of the process. See Structure ADR-0005, Structure ADR-0002.
/// </para>
/// </remarks>
/// See Structure ADR-0005.
/// </summary>
[Capability("structure.configured-tree.get")]
public sealed class GetConfiguredTreeHandler
    : ICapabilityHandler<GetConfiguredTreeCommand, GetConfiguredTreeResponse>
{
    private readonly IEntityStore _store;

    public GetConfiguredTreeHandler(IEntityStore store) => _store = store;

    public async ValueTask<ExecutionResult<GetConfiguredTreeResponse>> HandleAsync(
        GetConfiguredTreeCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Build StructureConfiguration ────────────────────────────────
        var options = new Dictionary<string, OptionValue>(StringComparer.Ordinal);

        foreach (var (iri, val) in command.FlagOptions ?? new Dictionary<string, bool>())
            options[iri] = new FlagOptionValue(val);

        foreach (var (iri, valueIri) in command.EnumerationOptions ?? new Dictionary<string, string>())
            options[iri] = new EnumerationOptionValue(valueIri);

        var config = new StructureConfiguration(command.BranchIri, options, command.ReferenceDate);

        // ── 2. Load all usages filtered by the active StructureScope ───────
        List<Usage> filteredUsages;
        using (StructureScope.Use(config))
        {
            filteredUsages = await _store
                .QueryByTypeAsync<Usage>(cancellationToken)
                .ToListAsync(cancellationToken);
        }

        // ── 3. Build a parent → (childIri, snapshotIri) adjacency lookup ─────
        var childrenByParent = filteredUsages.ToLookup(
            u => u.ParentStructureIri,
            u => (ChildIri: u.ChildStructureIri, SnapshotIri: u.SnapshotIri),
            StringComparer.Ordinal);

        // ── 4. DFS from the head IRI ──────────────────────────────────────
        var allNodeIris = new HashSet<string>(StringComparer.Ordinal);
        var inCurrentPath = new HashSet<string>(StringComparer.Ordinal);
        var root = BuildNode(command.StructureHeadIri, childrenByParent, inCurrentPath, allNodeIris, null);

        return new ExecutionResult<GetConfiguredTreeResponse>.Ok(
            new GetConfiguredTreeResponse(root, [.. allNodeIris]));
    }

    /// <summary>
    /// Recursively builds a <see cref="StructureNodeDto"/> for <paramref name="iri"/>.
    /// Uses backtracking cycle detection: a node already on the current DFS path is returned
    /// without children. Backtracking allows the same IRI to appear in multiple subtrees.
    /// <para>
    /// <paramref name="inheritedSnapshotBranchIri"/> carries the snapshot branch annotation
    /// from the incoming edge (or from an ancestor edge when no closer edge overrides it).
    /// Each child edge's <c>SnapshotIri</c> overrides the inherited value for that subtree.
    /// See Structure ADR-0006.
    /// </para>
    /// </summary>
    private static StructureNodeDto BuildNode(
        string iri,
        ILookup<string, (string ChildIri, string? SnapshotIri)> childrenByParent,
        HashSet<string> inCurrentPath,
        HashSet<string> collected,
        string? inheritedSnapshotBranchIri)
    {
        collected.Add(iri);

        if (!inCurrentPath.Add(iri))
            return new StructureNodeDto(iri, [], inheritedSnapshotBranchIri);   // cycle sentinel

        var children = childrenByParent[iri]
            .Select(edge => BuildNode(
                edge.ChildIri,
                childrenByParent,
                inCurrentPath,
                collected,
                edge.SnapshotIri ?? inheritedSnapshotBranchIri))
            .ToList();

        inCurrentPath.Remove(iri);   // backtrack

        return new StructureNodeDto(iri, children, inheritedSnapshotBranchIri);
    }
}
