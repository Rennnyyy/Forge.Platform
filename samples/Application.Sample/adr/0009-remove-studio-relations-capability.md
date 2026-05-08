# 0009 — Remove studio-relations capability; move bad-IRI checks to entity endpoint

- **Status**: accepted
- **Date**: 2026-05-08
- **Author**: agent

## Context

ADR-0006 added `CreateLinkedStudioHandler` (`demo.studio.create-linked`) and Bruno
collection `13-studio-relations` because `OperationEntityBinder` silently skipped
`[Owning]` properties during POST / PUT binding. This made the entity endpoint
(`POST api/entities/studios`) unable to wire `EntityRef<T>` and
`EntityRefCollection<T>` values, so a capability handler was the only place to
demonstrate relation wiring and validate referenced IRIs.

ADR-0007 built on top of ADR-0006 by adding three bad-IRI requests (files 04–06 in
collection 13) that drove `RELATION_NOT_FOUND` 422 responses through the same
capability endpoint.

`OperationEntityBinder` has since been extended to bind `[Owning]` properties from
the request body and to validate referenced IRIs against the store (and against
`[Enumeration]` named-individual collections). Bruno collection `12-studios` already
demonstrates this: `01-create.bru` supplies `managedBy`, `recordings`, and `genres`
directly in the entity POST body and the happy path works end-to-end.

The capability handler and its collection are therefore redundant.

## Decision

1. **Delete** `Capabilities/StudioRelationsCapability.cs` (handler + command/response
   records for `demo.studio.create-linked`).
2. **Delete** Bruno collection `13-studio-relations/` (all six `.bru` files).
3. **Renumber** `14-featured-artists/` → `13-featured-artists/`.
4. **Add** three bad-IRI error-path tests to collection `12-studios/`:

   | File | Relation flavour | Bad field |
   |------|-----------------|-----------|
   | `06-bad-entity-ref-iri.bru` | N:1 `EntityRef<T>` | `managedBy` → non-existent Artist IRI |
   | `07-bad-entity-ref-collection-iri.bru` | 1:N `EntityRefCollection<T>` | `recordings` → non-existent Recording IRI |
   | `08-bad-enumeration-iri.bru` | M:N `EntityRefCollection<T>` (Enumeration) | `genres` → unknown genre IRI |

   All three assert `res.status: eq 422` and `res.body.code: eq RELATION_NOT_FOUND`.

## Consequences

- ADR-0006 and ADR-0007 are superseded by this decision.
- `CreateLinkedStudioHandler` is no longer registered (assembly scanning finds no
  `[Capability("demo.studio.create-linked")]` handler); the route
  `POST api/capabilities/demo/studio/create-linked` no longer exists.
- The canonical reference for both relation wiring (happy path) and bad-IRI error
  handling is now collection `12-studios/` against `POST api/entities/studios`.
- The sample application has one fewer capability handler; `[Enumeration]` (Genre)
  and all three owned-relation flavours remain fully demonstrated.
