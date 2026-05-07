# 0005 — Complex Studio entity: all scalars + all owned relation flavours

- **Status**: accepted
- **Date**: 2026-05-07
- **Author**: agent

## Context

Chapters 01–09 demonstrate scalar-only entities (`Book`, `DataRecord`, `Artist`) and
hand-written capability handlers. None of them show how the Forge entity model handles
**owned relations** — the `[Owning]` attribute that persists an IRI reference or IRI
collection in the owning entity's RDF graph.

`Forge.Entity` supports three structural relation flavours:

| Flavour | Type | Semantics |
|---------|------|-----------|
| N:1 | `EntityRef<T>?` | Many instances of this type reference one remote entity |
| 1:N | `EntityRefCollection<T>` | One instance of this type owns an ordered collection of remote entities |
| M:N | `EntityRefCollection<T>` | Many instances of this type may reference the same remote entity |

Without a concrete example in the sample:

- Contributors have no living reference for how `[Owning]` is declared alongside
  scalar `[Predicate]` properties.
- The generator behaviour for mixed-property entities (scalars + relations in the
  same partial class) is untested at the sample level.
- There is no demonstration that all three relation flavours can coexist on a single entity.

## Options

1. **Introduce a `Studio` entity** that carries:
   - All 11 supported scalar CLR types (non-nullable) + their 11 nullable variants,
     explicitly mirroring `DataRecord`'s breadth at the model level.
   - An N:1 `[Owning]` to the existing `Artist` entity (many studios, one managing artist).
   - A 1:N `[Owning]` to a new `Recording` entity (one studio → many recordings).
   - An M:N `[Owning]` to a new `Genre` entity (one studio → multiple genres; same genre
     shared across studios).
   All three entities carry `[OperationEndpoints]` so they integrate automatically with
   `AddOperationEndpointsHttpFromAssemblyContaining<Book>()` and `MapOperations()`.
   Pro: demonstrates everything in one cohesive domain story; no new capabilities needed.
   Con: the `[Owning]` properties are silently skipped by `OperationEntityBinder`
   (documented limitation of the HTTP scalar-binder); relations are exercised at the
   entity model and RDF backend level only, not via the REST surface.

2. **Extend an existing entity** (e.g., add relations to `Book`).
   Con: `Book` is already exercised in chapters 02 and 07 with specific aspect shapes;
   adding relations would require reshaping those SHACL/SPARQL fixtures; changes risk
   breaking existing integration tests.

3. **Add relations only in `Entity.Tests.Fixtures`** and not in the sample.
   Con: the sample would still lack a living runnable demonstration of relations;
   the ADR-0013 chapter story would have a gap in the relation-model surface.

## Decision

Option 1.

### Domain model

| Type | Identity | Role |
|------|----------|------|
| `Genre` | `PropertyBasedPlain` (slug) | Shared lookup — target of M:N from Studio |
| `Recording` | `Random` (UUID) | Child entity — target of 1:N from Studio |
| `Studio` | `Random` (UUID) | Complex aggregate — owns all three relation flavours + all scalars |

### Relation annotations on `Studio`

| Property | Attribute | Flavour | Semantics |
|----------|-----------|---------|-----------|
| `ManagedBy` | `[Owning("managedBy")]` | N:1 | Many studios can be managed by the same `Artist` |
| `Recordings` | `[Owning("hasRecording")]` | 1:N | A studio produces an ordered collection of `Recording` entities |
| `Genres` | `[Owning("hasGenre")]` | M:N | A studio specialises in multiple genres; each genre is shared across studios |

### HTTP binder note

`OperationEntityBinder` filters bindable properties via `[Predicate]`; `[Owning]`
properties are silently skipped (see Operations.Http source). This means Bruno chapters
for the three new entities exercise scalar CRUD only. This is intentional and documented:
the relation model is the demonstration, not the HTTP transport.

### Bruno chapter layout

| Chapter | Entity | What it demonstrates |
|---------|--------|----------------------|
| `10-genres/` | `Genre` | Scalar CRUD for the M:N lookup entity |
| `11-recordings/` | `Recording` | Scalar CRUD for the 1:N child entity |
| `12-studios/` | `Studio` | Full scalar CRUD for the complex aggregate (all 22 scalar fields) |

Each chapter maps to one `[SkippableFact]` in `BrunoIntegrationTests`.

### ADR-0013 numbering

Chapters are appended after the existing `09-artists/`. The next available chapter prefix
is `10`. Chapter 09 (Artists) gains its missing `[SkippableFact]` in `BrunoIntegrationTests`
as part of this change, satisfying ADR-0013's rule that every committed chapter has an
automated test.

## Consequences

- The sample demonstrates all three owned-relation flavours in one domain story.
- `Genre`, `Recording`, and `Studio` are automatically discovered by
  `AddOperationEndpointsHttpFromAssemblyContaining<Book>()` — no `Program.cs` change needed.
- `[Owning]` properties generate correctly alongside scalar `[Predicate]` properties
  in the same partial class (validated at build time by the source generator).
- Future contributors can point to `Studio.cs` as the canonical reference for writing
  entities with mixed scalar + relation properties.
- The REST surface for Studio is scalar-only; contributors who need to manipulate
  relations programmatically use `EntityRef<T>.ForIri(…)` and
  `EntityRefCollection<T>.Add(…)` directly on entity instances.
