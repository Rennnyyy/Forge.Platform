# 0005 — Branch lineage and the `DerivedFrom` predicate

- **Status**: accepted
- **Date**: 2026-05-14
- **Author**: agent

## Context

The merge pipeline (ADR-0006, ADR-0007) needs to know from which graph a branch was
originally forked. Without lineage metadata:

- There is no durable record of provenance for audit or tooling.
- A fast-forward check — "is the target graph unchanged since the fork?" — is impossible
  without knowing the common ancestor.
- `BranchSeedingService.CreateSeededBranchAsync` already receives a `sourceGraphIri`
  argument and discards it after construction; recording it on the entity costs nothing at
  creation time.

Constraints:

- `DerivedFrom` must accept both a `Snapshot` IRI and a `Branch` IRI (forks do not always
  originate from a snapshot; a developer may fork from another live branch).
- A circular dependency between `Branch` and `Snapshot` (where `Snapshot : Branch`) rules
  out a typed `EntityRef<Snapshot>` — using a typed ref would force the property type to
  `EntityRef<Branch>`, which is correct for branches but loses the snapshot discriminator.
  A plain `string?` IRI is sufficient and avoids introducing a lazy-loading cycle within
  the same partial-class hierarchy.
- Lineage is **optional**. Branches created directly (not via seeding) have no meaningful
  ancestor and `DerivedFrom` is left null.

## Options

### Option A — `string? DerivedFrom` on `Branch`

Store the IRI of the source graph (which is also the source branch or snapshot entity IRI,
per Branch ADR-0001) as a plain predicate.
Pro: no ref-loading complexity; no possibility of a self-referential load cycle; resolves
correctly whether the ancestor is a `Branch` or a `Snapshot`.
Con: no compile-time type safety; callers who want to navigate to the ancestor must resolve
the IRI themselves.

### Option B — `EntityRef<Branch>? DerivedFrom` on `Branch`

Store as a lazy `EntityRef<Branch>`. Snapshot IRIs are within the `branches/{Name}` path
so a `Branch` ref loads correctly for both subtypes.
Pro: uniform lazy-loading pattern.
Con: introduces an owning self-ref on `Branch` (a branch "owns" its ancestor reference)
which is semantically wrong — the ancestor is not owned by the descendant. Using
`[Inverse]` is equally wrong. A plain `[Predicate]`-only ref that does not participate
in the owning/inverse model does not exist today; adding it would require a new attribute
concept. Out of scope.

### Option C — `EntityRef<Snapshot>? DerivedFrom` on `Snapshot`, not on `Branch`

Only snapshots record their source. Branches forked from live branches cannot record
lineage at all.
Con: excludes the common case of forking from a live branch; incomplete provenance.

## Decision

**Option A.** `Branch` gains one new predicate property:

```csharp
/// <summary>
/// IRI of the <see cref="Branch"/> or <see cref="Snapshot"/> from which this branch was
/// forked. Null for primary branches created without a source graph.
/// Set automatically by <c>BranchSeedingService.CreateSeededBranchAsync</c>.
/// See Branch ADR-0005.
/// </summary>
[Predicate("derivedFrom")]
public string? DerivedFrom { get; init; }
```

### How `DerivedFrom` is set

`BranchSeedingService.CreateSeededBranchAsync` accepts a `sourceGraphIri` parameter.
This value is assigned to `branch.DerivedFrom` before the management `CreateOperation<Branch>`
is added to the transaction. No existing overload signatures change; the assignment is an
internal implementation detail of the service.

`BranchSeedingService.CreateSnapshotAsync` does the same for `Snapshot` (which inherits
`DerivedFrom` from `Branch`).

### Fast-forward detection

A merge is a **fast-forward** when the target branch's current named graph content is
byte-for-byte equal to the common ancestor's content — i.e. no mutations have been applied
to the target since the fork. The merge planner (ADR-0006) can detect this by checking
whether `IBranchDiffEngine.ComputeDiffAsync(targetGraphIri, derivedFromIri)` returns an
empty delta. In the fast-forward case the same plan is still correct; the check is purely
an optimisation hint for callers who want to skip metrics or logging when there is nothing
to apply. The merge service (ADR-0007) does not special-case fast-forward in v1.

### `DerivedFrom` in HTTP responses

`Forge.Branch.Http` exposes `DerivedFrom` as a plain `string?` field in any branch or
snapshot response DTO — no additional resource resolution is required. Callers who wish to
navigate to the ancestor may issue a separate GET for the returned IRI.

## Consequences

- Every seeded branch and snapshot carries a durable `DerivedFrom` pointer at no
  algorithmic cost.
- `Branch` gains one nullable predicate; existing branch entities without the predicate
  materialise with `DerivedFrom = null`, which is the correct default.
- Lineage traversal (walking the ancestor chain) is out of scope for v1; `DerivedFrom`
  is a single-hop pointer only.
- The mapper reflects the new property automatically (reflection-based mapper reads all
  `[Predicate]`-annotated properties); no generator changes needed.
