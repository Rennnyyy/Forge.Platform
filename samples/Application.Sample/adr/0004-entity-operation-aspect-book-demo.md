# 0004 — Entity operation aspect demonstration for generated CUD handlers

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

ADRs 0001–0003 demonstrated the configuration-switchable backend, the error-path, and
the message-aspect (capability-aspect) pipeline. None of them exercise `IOperationAspect`
— the entity-graph two-pass validation (Local SHACL + Context SPARQL) that runs during
`EntityTransaction.CommitAsync`.

Capability ADR-0015 makes generated CUD handlers route through `EntityTransaction`.
Aspects ADR-0010 adds `CapabilityAspect.OperationAspectIri`. Without a working sample
and Bruno chapter, contributors have no reference for:

- Registering an `IOperationAspect` (inline TTL or SPARQL) in `IAspectStore`.
- Wiring a `CapabilityAspect` with `OperationAspectIri` to a generated CUD handler.
- Observing that invalid entity data is rejected with `422 ENTITY_SHACL_VIOLATION`.
- Observing that a "no aspect header" request bypasses entity-graph validation entirely.

## Decision

Use the existing `Book` entity (already exercised in chapter 02) as the subject.
Register two `IOperationAspect` instances and three `CapabilityAspect` bundles in
`Program.cs` post-build via `IAspectStore` direct registration (the same pattern used
by chapter 06 for message aspects — see sample ADR-0003).

### Operation aspects

| IRI | Kind | Validation rule |
|-----|------|-----------------|
| `urn:forge:aspects:operation:book-write-v1` | Local SHACL pass | `publishedYear` must be ≥ 1800 |
| `urn:forge:aspects:operation:book-delete-v1` | Context SPARQL pass | `available = false` → violation (cannot delete a checked-out book) |

### Capability aspects

| IRI | OperationAspectIri |
|-----|--------------------|
| `urn:forge:aspects:capability:book-create-v1` | `urn:forge:aspects:operation:book-write-v1` |
| `urn:forge:aspects:capability:book-update-v1` | `urn:forge:aspects:operation:book-write-v1` |
| `urn:forge:aspects:capability:book-delete-v1` | `urn:forge:aspects:operation:book-delete-v1` |

### SHACL shape (`book-write-v1`)

```turtle
@prefix sh:    <http://www.w3.org/ns/shacl#> .
@prefix xsd:   <http://www.w3.org/2001/XMLSchema#> .
@prefix books: <https://forge-it.net/predicates/books/> .

<urn:forge:aspects:shape:book-write-shape>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/books> ;
    sh:property [
        sh:path books:publishedYear ;
        sh:minInclusive "1800"^^xsd:integer ;
        sh:message "Published year must be 1800 or later." ;
    ] .
```

### Context WHERE body (`book-delete-v1`)

```sparql
?entityIri <https://forge-it.net/predicates/books/available> false .
BIND(?entityIri AS ?focusNode)
BIND("Cannot delete a checked-out book (available = false)." AS ?message)
```

Returns a row (violation) when the book's `available` predicate is `false`.
Passes (no row) when `available = true`.

### Bruno chapter layout (`07-entity-aspect-demo/`)

| File | Request | Assertion |
|------|---------|-----------|
| `01-create-valid.bru` | POST books/create, year=2020, aspect create IRI | 200; stores `entityAspectBookIri` |
| `02-create-invalid.bru` | POST books/create, year=1600, aspect create IRI | 422 `ENTITY_SHACL_VIOLATION` |
| `03-create-checkedout.bru` | POST books/create, year=2022, available=false, aspect create IRI | 200; stores `checkedOutBookIri` |
| `04-delete-valid.bru` | POST books/delete, `{{entityAspectBookIri}}`, aspect delete IRI | 200 |
| `05-delete-invalid.bru` | POST books/delete, `{{checkedOutBookIri}}`, aspect delete IRI | 422 `ENTITY_SHACL_VIOLATION` |
| `06-create-permissive.bru` | POST books/create, year=1400, **no aspect header** | 200 (entity-graph bypassed) |

Chapter 07 maps to one `[SkippableFact]` in `BrunoIntegrationTests`:
`Bruno_07_entity_aspect_demo_requests_all_pass`.

## Consequences

- Contributors can observe both the Local-pass and Context-pass paths of operation
  aspect validation through a running HTTP application.
- The permissive request (step 06) demonstrates that absence of the
  `X-Forge-Capability-AspectIri` header bypasses entity-graph validation, which is
  the intended no-op default from Aspects ADR-0003.
