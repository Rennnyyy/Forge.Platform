# 0006 — Genre as [Enumeration] and relation demonstration via capability handler

- **Status**: superseded by [0009](0009-remove-studio-relations-capability.md)
- **Date**: 2026-05-07
- **Author**: agent

## Context

ADR-0005 introduced `Genre`, `Recording`, and `Studio` as three entities that together
demonstrate all three owned-relation flavours (N:1, 1:N, M:N). That ADR made `Genre`
a regular `PropertyBasedPlain` entity with `[OperationEndpoints]`, exposing full CRUD.

Two problems arose from review:

1. **[Enumeration] not demonstrated** — Forge.Entity ships `[Enumeration]`, a class-level
   attribute that compiles a fixed named-individual vocabulary into the assembly (SKOS-like).
   Genre is the canonical use case: music genres are a bounded, stable vocabulary that does
   not grow via user POST requests at runtime. Leaving Genre as mutable CRUD contradicts this
   design intent.

2. **Relations not visible in Bruno** — The `OperationEntityBinder` silently skips `[Owning]`
   properties during POST / PUT binding (Operations.Http ADR-0001). ADR-0005 documented this
   but did not provide a Bruno chapter that actually wires entities together and shows the
   relation IRIs in the response. Contributors reading ADR-0005 can see the entity *model*
   but cannot observe the *behaviour* end-to-end via Bruno.

## Options

### Genre

1. **Convert Genre to `[Enumeration]`** — Make it `sealed` + `[Enumeration]`; declare five
   named individuals (`Jazz`, `Classical`, `Electronic`, `Ambient`, `Pop`) as
   `public static readonly` fields. The generator emits a static constructor that calls
   `MaterializeIdentity()` on each field, sealing their IRIs at class-load time. Remove
   `[OperationEndpoints]` and register read-only `GET api/entities/genres` endpoints
   manually in `Program.cs`, backed by reflection over the static fields via a
   `public static IReadOnlyList<Genre> All { get; }` property.
   Pro: demonstrates `[Enumeration]`; genre vocabulary is encoded in the type system, not
   the database; no Create / Update / Delete exposure.

2. **Keep Genre as a regular entity** but mark CRUD routes noisy/optional.
   Con: `[Enumeration]` remains undemonstrated; nothing prevents a caller from
   deleting a genre that Studios reference.

### Relation demonstration

1. **Add a `CreateLinkedStudioHandler` capability** (`demo.studio.create-linked`) that
   accepts a command with scalar Studio fields plus explicit relation IRIs
   (`managedByArtistIri`, `recordingIris`, `genreIris`). The handler:
   - Sets `studio.ManagedBy = EntityRef<Artist>.ForIri(...)` (N:1).
   - Loads each Recording from the store and calls `studio.Recordings.AddAsync(...)` (1:N).
   - Looks up each genre IRI in `Genre.All` by IRI and calls `studio.Genres.AddAsync(...)` (M:N).
   - Persists the studio via `EntityTransaction`.
   - Returns a `CreateLinkedStudioResponse` with the Studio IRI and all linked IRIs,
     so Bruno can assert each relation flavour from the response body.
   Pro: clean DTO response avoids entity serialization concerns (EntityRef<T>.ValueOrThrow
   would throw during STJ serialization on unloaded refs); handler demonstrates store access
   and all three relation annotations in a single call.
   Con: Requires a new capability file and a new Bruno chapter (13).

2. **Mutate the Studio entity via a synthetic PUT body that includes IRIs** — extend the
   entity binder to support `[Owning]` binding.
   Con: changes the `Operations.Http` slice's binder, which has its own ADR; scope exceeds
   this task.

## Decision

Option 1 for both concerns.

### Genre [Enumeration] rules

| Rule | Detail |
|------|--------|
| Class modifiers | `sealed partial` |
| Attributes | `[Entity]`, `[Identity(PropertyBasedPlain)]`, `[Enumeration]` |
| No `[OperationEndpoints]` | Genre is excluded from `MapOperations()` auto-discovery |
| Named individuals | `Jazz`, `Classical`, `Electronic`, `Ambient`, `Pop` as `static readonly` |
| `All` property | `public static IReadOnlyList<Genre>` for iteration without reflection at call sites |
| IRI structure | `https://forge-it.net/genres/{slug}` (same as PropertyBasedPlain before) |
| HTTP surface | `GET api/entities/genres` (list) and `GET api/entities/genres?iri=…` (single) |
| 404 behaviour | Unknown IRI returns 404 JSON `{code, message}` |

### Capability handler

| Detail | Value |
|--------|-------|
| Capability ID | `demo.studio.create-linked` |
| Route | `POST api/capabilities/demo/studio/create-linked` |
| Command | `CreateLinkedStudioCommand(Name, FoundedYear, ManagedByArtistIri?, RecordingIris?, GenreIris?)` |
| Response | `CreateLinkedStudioResponse(StudioIri, ManagedByArtistIri?, RecordingIris, GenreIris)` |
| N:1 wiring | `EntityRef<Artist>.ForIri(iri)` — artist existence validated via `EntityOperations.ReadAsync` |
| 1:N wiring | `EntityOperations.ReadAsync<Recording>(iri)` + `studio.Recordings.AddAsync(recording)` |
| M:N wiring | `Genre.All.FirstOrDefault(g => g.Iri == iri)` + `studio.Genres.AddAsync(genre)` |

### Bruno chapter 13 — Studio relations

| File | What it demonstrates |
|------|---------------------|
| `01-create-artist.bru` | Creates the Artist that will be the N:1 target |
| `02-create-recording.bru` | Creates the Recording that will be the 1:N child |
| `03-create-linked-studio.bru` | Calls `demo.studio.create-linked`; asserts all three relation IRIs in the response |

### Bruno chapter 10 — Genres (read-only)

All five `.bru` files in `10-genres/` are repurposed as read-only tests that demonstrate
the bounded genre vocabulary:

| File | Request | Assertion |
|------|---------|-----------|
| `01-create.bru` | GET /genres (list) | `items` array is defined |
| `02-list.bru` | GET /genres?iri=jazz | `name == Jazz` |
| `03-read.bru` | GET /genres?iri=electronic | `name == Electronic` |
| `04-update.bru` | GET /genres?iri=ambient | `name == Ambient` |
| `05-delete.bru` | GET /genres?iri=nonexistent | `status == 404` |

(File names are legacy from the CRUD layout; content is now read-only.)

## Consequences

- `[Enumeration]` is fully demonstrated in the sample with a bounded genre vocabulary.
- The RDF named-individual pattern (IRI sealed at class-load time) is observable via
  the `GET api/entities/genres` endpoint without any database writes.
- All three owned-relation flavours (N:1, 1:N, M:N) are wired and observable in Bruno
  via the `demo.studio.create-linked` handler's response body.
- Future contributors can point to `StudioRelationsCapability.cs` + Bruno chapter 13 as
  the canonical reference for writing capability handlers that create entities with relations.
- `EntityRef<T>.ValueOrThrow`'s STJ serialization quirk is avoided by returning the
  linked IRIs in the capability response DTO rather than the raw entity.
