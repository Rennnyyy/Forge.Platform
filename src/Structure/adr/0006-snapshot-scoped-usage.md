# 0006 — Snapshot-scoped Usage: per-edge branch annotation for geometry resolution

- **Status**: accepted
- **Date**: 2026-05-16
- **Author**: agent

## Context

ADR-0001 established `Usage` as a condition-bearing directed edge between two
`IStructure` nodes. ADR-0002 noted that snapshot-lineage–based conditions
("satisfy only when the evaluation context is within snapshot v1.0.0") were
deferred to a future v2.

An independent but complementary need arose in the car-demo sample: geometry
nodes and their `GeometryUsage` placement edges live in the same RDF named graph
(branch) as the structural nodes. When the platform evolves and rebases geometry
— replacing one SVG or OBJ mesh with a newer version — the old version is no
longer reachable from the main branch. Callers who want to reference a specific,
frozen geometry version have no standard mechanism to express that intent without
duplicating structure nodes or using ad-hoc conventions.

The core requirement:

> When resolving the configured structure tree, a caller should be able to pin an
> individual structural subtree to a specific named graph (branch or snapshot) so
> that the client code fetches the associated context (geometry, metadata) from
> that named graph instead of the query's default branch.

## Options

### Option A — `SnapshotIri` predicate on `Usage`

Add an optional `[Predicate("snapshotIri")] string? SnapshotIri` property to `Usage`.
`GetConfiguredTreeHandler` propagates the value into the `StructureNodeDto` via a new
`string? SnapshotBranchIri` field. Callers read the annotated tree and switch their
`BranchScope` when fetching context for snapshot-annotated nodes.

Pro: zero new entity types; pure annotation on existing edge; backward compatible
(null = current behaviour); `SnapshotIri` is stored as an RDF predicate and round-trips
through both InMemory and GraphDB backends.
Con: the `Usage` entity gains semantic dual purpose (condition edge + branch pin).

### Option B — New `SnapshotedUsage : Usage` entity subtype

Inherit from `Usage` in the same way `Snapshot : Branch` does. The subtype carries
`SnapshotIri` as a typed property.
Pro: clean type separation.
Con: `GetConfiguredTreeHandler` must handle both `Usage` and `SnapshotedUsage` in
the adjacency lookup; `QueryByTypeAsync<Usage>()` returns both (polymorphic listing);
the filter store and condition evaluation already treat subtype instances correctly —
no real benefit over a nullable property on the base.

### Option C — Proxy entity (`GeometryPin`) at application level (ADR-0002 Option 3 from analysis)

A new application-level entity explicitly pins a geometry node + snapshot branch.
The structure tree node itself is unmodified.
Pro: no platform change.
Con: every consuming application must invent its own proxy pattern; the branch context
is not visible in the configured-tree response; requires another client-side join.

## Decision

**Option A.**

### `Usage.SnapshotIri`

```csharp
/// <summary>
/// Optional IRI of the named graph (branch or snapshot) that should be used when
/// resolving this edge's child node and its subtree. When set …
/// </summary>
[Predicate("snapshotIri")]
public string? SnapshotIri { get; set; }
```

Null means "inherit caller's branch" — existing behaviour is unchanged.

### `StructureNodeDto.SnapshotBranchIri`

```csharp
public sealed record StructureNodeDto(
    string                          Iri,
    IReadOnlyList<StructureNodeDto> Children,
    string?                         SnapshotBranchIri = null);
```

### Propagation rule (BuildNode)

The DFS traversal maintains an `inheritedSnapshotBranchIri` parameter:

1. The tree root is called with `null` (no snapshot pin).
2. For each outgoing edge `u` from node `N`:
   - The child's effective snapshot = `u.SnapshotIri ?? inheritedSnapshotBranchIri`.
   - If non-null, the child's `StructureNodeDto.SnapshotBranchIri` is set to this value.
   - The child's own children inherit the child's effective snapshot unless their edge
     overrides it.
3. A more specific snapshot on a descendant edge always wins (it overrides the inherited
   value, allowing partial re-pinning inside a snapshot subtree).

### Relationship to ADR-0002 v2 milestone path

ADR-0002 deferred "snapshot lineage window" conditions to v2. `SnapshotIri` on `Usage`
is **orthogonal** to that: it does not affect condition evaluation; it only annotates
which named graph the client should consult for associated context. The two features
compose: a single `Usage` may carry both a `ConditionSet` (governing whether the edge is
active) and a `SnapshotIri` (governing where the child's context is resolved from).

### Demo in `Application.Sample`

The car demo populates a v1 steel-frame geometry to a named graph
`https://forge-it.net/branches/geometry-v1`, then seeds the current (v2) geometry to
the main branch. The `Usage(Chassis → SteelFrame)` edge carries
`SnapshotIri = "https://forge-it.net/branches/geometry-v1"`. The Bruno chapter 23
(`23-geometry-snapshot/`) verifies:

- The configured-tree response carries `snapshotBranchIri` on the `SteelFrame` node
  and its descendants.
- A `GET /api/entities/geometry-nodes` with the snapshot branch scope returns exactly
  the v1 geometry node; the same request on the main branch returns the v2 node.

## Consequences

- `Usage` gains one optional predicate. All existing `Usage` instances (no `SnapshotIri`
  → null) are unaffected; the field is not required.
- `StructureNodeDto` gains one optional field. Clients that do not inspect it receive
  the same tree as before; the field is null for all nodes in snapshot-free trees.
- The configured-tree response is now richer: clients can use `snapshotBranchIri` to
  issue context queries (geometry, metadata) against the correct named graph without
  any client-side heuristics.
- The v2 milestone condition path (ADR-0002) remains open and is not pre-empted by this
  change.
