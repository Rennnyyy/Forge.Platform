# 0017 — Platform entity HTTP layers must use shared `Forge.Operations.Http` contracts

- **Status**: accepted
- **Date**: 2026-05-12
- **Author**: agent

## Context

`Forge.Operations.Http` defines four canonical response records for entity CRUD operations:

| Type | Usage |
|------|-------|
| `OperationCreatedResponse(string Iri)` | Entity successfully created |
| `OperationUpdatedResponse(string Iri)` | Entity successfully updated |
| `OperationDeletedResponse()` | Entity successfully deleted |
| `OperationListResponse<T>(IReadOnlyList<T> Items)` | List of entities returned |

Entity types decorated with `[OperationEndpoints]` get free use of these contracts via the
generated HTTP layer. However, some platform-owned entity types — notably `Branch` and
`Snapshot` — cannot use `[OperationEndpoints]` because their HTTP surface has bespoke
semantics:

- `Branch` endpoints need management-graph isolation, default-branch protection, and
  cascade graph drop on delete.
- `Snapshot` endpoints need seeding orchestration, SemVer-conflict checks, and frozen-set
  invalidation on create/delete.

When `Forge.Branch.Http` was first written its `MapBranches` and `MapSnapshots` endpoints
defined their own parallel response records (`BranchCreatedResponse`, `BranchDeletedResponse`,
`BranchListResponse`, `BranchUpdatedResponse`, `SnapshotCreatedResponse`,
`SnapshotDeletedResponse`, `SnapshotListResponse`). These are structurally identical to the
shared types but are separate declarations, meaning:

1. A JSON consumer cannot infer from the type name alone that it is seeing the same
   contract as any other entity CRUD endpoint.
2. Code that switches on or wraps responses from multiple slices (e.g. a gateway, a test
   helper, a middleware layer) must know about both sets of types.
3. Every new platform-managed entity type risks creating a third set of duplicates.

## Options

### Option A — Keep slice-local response records

Pro: zero new project references in `Forge.Branch.Http`.  
Con: violates the single-vocabulary principle; the same JSON shape is given three different
.NET type identities; compounds as more managed-entity slices are added.

### Option B — Shared response records in `Forge.Execution` or `Forge.Repository`

Pro: avoids pulling in the full `Forge.Operations.Http` dependency.  
Con: `OperationCreatedResponse` etc. are HTTP-surface contracts and belong in an HTTP
package; moving them to a non-HTTP package blurs the layering.

### Option C — Use `Forge.Operations.Http` response records everywhere

Pro: single vocabulary; `Branch.Http` and every future managed-entity HTTP slice reuse the
same types; consumers inspect one set of records.  
Con: `Forge.Branch.Http` must add a project reference to `Forge.Operations.Http`. The
dependency is legitimate (both are HTTP-surface packages at the same layer).

## Decision

**Any HTTP layer for a platform entity class — whether code-generated or hand-written —
must use `OperationCreatedResponse`, `OperationUpdatedResponse`, `OperationDeletedResponse`,
and `OperationListResponse<T>` from `Forge.Operations.Http` as its response body types.**

Custom implementations are free to differ in every other dimension (routing shape,
validation logic, orchestration, middleware hooks), but the *response envelope records*
must be the shared contracts.

Additionally, entity lookup endpoints must be IRI-first. Convenience query parameters that
restate information already present in the list response (e.g. `?semver=…`) add a second
resolution path without adding expressiveness: the client can read the IRI from the list and
use `?iri=…`. Secondary query parameters that do not map to a stable IRI field are not
permitted on GET endpoints for managed entities.

### Immediate application: `Forge.Branch.Http`

- `Forge.Branch.Http.csproj` adds `<ProjectReference>` to `Forge.Operations.Http`.
- `BranchCreatedResponse`, `BranchUpdatedResponse`, `BranchDeletedResponse`,
  `BranchListResponse`, `SnapshotCreatedResponse`, `SnapshotDeletedResponse`,
  `SnapshotListResponse` are all deleted.
- Their usages are replaced with the shared types from `Forge.Operations.Http`.
- `GET api/snapshots?semver=…` is removed; `GET api/snapshots?iri=…` remains.

## Consequences

- `Forge.Branch.Http` gains one additional project reference.
- The IRI is the single canonical handle for any snapshot; callers obtain it from
  `GET api/snapshots` (list) and then use `GET api/snapshots?iri=…` for a direct read.
- Future platform-managed entity slices (e.g. `Authorization.Http`, if it ever ships
  entity endpoints) must follow the same rule without a separate ADR.
