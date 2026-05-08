# 0007 — Wrong-IRI error demonstration for EntityRef and EntityRefCollection

- **Status**: superseded by [0009](0009-remove-studio-relations-capability.md)
- **Date**: 2026-05-08
- **Author**: agent

## Context

ADR-0005 introduced the `Studio` entity with three owned-relation flavours and noted that
`OperationEntityBinder` silently skips `[Owning]` properties (no existence validation at
bind time). ADR-0006 introduced `CreateLinkedStudioHandler` (`demo.studio.create-linked`)
to exercise those three relation flavours end-to-end via a capability handler that
explicitly wires `EntityRef<T>` and `EntityRefCollection<T>` values.

ADR-0006 specified that the handler validates each supplied IRI against the store (or
`Genre.All` for enumeration types) before wiring it. It did not, however, specify what
the handler should return when a supplied IRI is invalid, nor did it include Bruno
requests that intentionally pass wrong IRIs to demonstrate the HTTP error path.

Without concrete error-path requests and a documented error code, contributors
have no living reference for how to communicate a "referenced entity does not exist"
failure from an HTTP endpoint.

## Options

1. **`RELATION_NOT_FOUND` error code, 422 response, per-invalid-IRI fail-fast.**  
   The handler checks each relation IRI in order: ManagedBy first, then each
   Recording IRI, then each Genre IRI. The first invalid IRI causes an immediate
   `CapabilityResult.Fail(new CapabilityError("RELATION_NOT_FOUND", "…"))` return.
   The HTTP transport maps `Fail` → `422 Unprocessable Entity`. Three dedicated Bruno
   requests demonstrate all three relation flavours independently.  
   Pro: simple; the error code is self-explanatory; fail-fast aligns with capability
   handler conventions throughout the sample.  
   Con: when multiple invalid IRIs are supplied, only the first is reported.

2. **Collect all invalid IRIs and return a single aggregated error.**  
   Pro: more informative for callers who send multiple bad IRIs in one request.  
   Con: significantly more complex; the aggregate structure departs from the existing
   `CapabilityError` contract (which carries a single `code + message`); no other
   handler in the sample does this; out of scope for a demo.

3. **Return `400 Bad Request` via `ExecutionError` instead of `422 via CapabilityError`.**  
   Con: `Operations.Http` and `Capability.Http` have distinct error contracts; mixing
   them in the same handler violates the capability boundary. Chapter 13 is reached
   via `POST /api/capabilities/…` which goes through the capability transport.

## Decision

Option 1. The handler uses fail-fast validation and returns
`CapabilityResult.Fail(new CapabilityError("RELATION_NOT_FOUND", "…"))` on the first
invalid IRI encountered. The HTTP response is `422 Unprocessable Entity`.

### Validation order and logic

| Pass | Relation | Mechanism |
|------|----------|-----------|
| 1 | `ManagedByArtistIri` | `EntityOperations.ReadAsync<Artist>(iri)` — null → fail |
| 2 | Each IRI in `RecordingIris` | `EntityOperations.ReadAsync<Recording>(iri)` — null → fail |
| 3 | Each IRI in `GenreIris` | `Genre.All.FirstOrDefault(g => g.Iri == iri)` — null → fail |

`Genre` is an `[Enumeration]`; its individuals live as static named fields in the
assembly, not in the store. The handler therefore validates genre IRIs against
`Genre.All` to avoid a spurious store hit on a type that is never persisted.

### Error message format

```
"Artist 'https://…/artists/…' does not exist."
"Recording 'https://…/recordings/…' does not exist."
"Genre 'https://…/genres/bad-slug' is not a known genre IRI."
```

### Bruno chapter 13 layout (`13-studio-relations/`)

| File | seq | What it demonstrates |
|------|-----|----------------------|
| `01-create-artist.bru` | 1 | Setup — creates the Artist for the happy-path requests |
| `02-create-recording.bru` | 2 | Setup — creates the Recording for the happy-path requests |
| `03-create-linked-studio.bru` | 3 | Happy path — all three relations wired; response carries all linked IRIs |
| `04-bad-entity-ref-iri.bru` | 4 | Wrong N:1 IRI (`managedBy`) → 422 `RELATION_NOT_FOUND` |
| `05-bad-entity-ref-collection-iri.bru` | 5 | Wrong 1:N IRI in `recordingIris` → 422 `RELATION_NOT_FOUND` |
| `06-bad-enumeration-iri.bru` | 6 | Wrong M:N genre IRI in `genreIris` → 422 `RELATION_NOT_FOUND` |

### Integration test

Chapter 13 maps to one `[SkippableFact]` in `BrunoIntegrationTests`:
`Bruno_13_studio_relations_requests_all_pass`.

## Consequences

- Contributors have a concrete, runnable reference for what happens when an
  `EntityRef<T>` single-ref or `EntityRefCollection<T>` collection member IRI does not
  exist in the store — the endpoint returns `422 RELATION_NOT_FOUND`.
- The three error-path Bruno requests (files 04–06) cover all three relation flavours
  independently, making the connection between C# type and HTTP behaviour explicit.
- ADR-0013 chapter numbering is extended: chapters 01–12 unchanged; 13 appended.
- ADR-0006 chapter-13 layout is superseded by this ADR (which adds files 04–06 to
  the three files 01–03 already planned in ADR-0006).
