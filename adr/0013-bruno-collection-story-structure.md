# 0013 — Bruno collection organised as story chapters

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0012 established that Bruno collections under `samples/` are living integration tests
driven by the Bruno CLI. The `Application.Sample` collection covers two generated-CRUD
entities (`Book`, `DataRecord`) and two hand-written capabilities (`Greet`, `Catalog`).

The initial collection layout grouped requests by entity or feature name as flat
sibling folders at the collection root:

```
bruno/
  books/          ← Book entity CRUD
  data-records/   ← DataRecord entity CRUD
  demo/           ← mixed: Greet capability + Catalog capability
```

This layout had two problems:

1. **No narrative arc** — a reader opening the collection sees three unrelated folders
   with no signal about which to run first or how they relate to each other.
2. **Heterogeneous `demo/` folder** — the `demo/` bucket mixed two unrelated capabilities
   (greeting and catalog management) because there was no better home for them.

The companion integration test (`BrunoIntegrationTests.cs`) ran `books/` and `demo/` as
separate facts. `data-records/` had no automated coverage at all.

## Options

1. **Story chapters** — numbered top-level folders that guide a reader through the app
   in a natural sequence: greet → books → data-records → catalog.
   ```
   bruno/
     01-greeting/        ← Greet capability: say hello, pass an aspect header
     02-books/           ← Book entity CRUD (generated handlers)
     03-data-records/    ← DataRecord entity CRUD with every scalar CLR type
     04-catalog/         ← Catalog capability: create / update / patch
   ```
   Pro: self-documenting order; each chapter is thematically coherent; mirrors the
   onboarding journey a new contributor would naturally take.
   Con: The `01-`, `02-` prefix must stay stable; renaming chapters is a breaking change
   for anyone who has pinned a chapter path in a CI override.

2. **Mirror Application.Sample C# slices** — `Entities/` and `Capabilities/` folders,
   then one sub-folder per type (e.g. `Entities/Books/`).
   Pro: maps 1:1 to the code structure.
   Con: loses the onboarding sequencing; does not tell a story.

3. **Keep the current layout, fix only the `demo/` split** — add `greet/` and `catalog/`
   siblings at the root. Pro: minimal change. Con: still no narrative arc.

## Decision

Option 1 — story chapters with numeric prefixes.

### Chapter layout

| Folder | Requests | What it demonstrates |
|---|---|---|
| `01-greeting/` | `01-greet.bru`, `02-greet-with-aspect-iri.bru` | Capability handler; aspect-IRI header passthrough |
| `02-books/` | `01-create.bru` … `05-delete.bru` | Generated CRUD for `Book` (minimal scalar surface) |
| `03-data-records/` | `01-create.bru` … `05-delete.bru` | Generated CRUD for `DataRecord` (all scalar CLR types) |
| `04-catalog/` | `01-create-item.bru`, `02-update-item.bru`, `03-patch-item.bru` | Hand-written POST/PUT/PATCH capability handlers |

### Naming convention

- Top-level chapter folders are named `NN-kebab-case/` where `NN` is zero-padded.
- Request files within a chapter are named `NN-kebab-case.bru` with matching `seq: NN`.
- Chapter numbers start from `01`; request numbers start from `01`.
- The `seq` field inside each `.bru` file is scoped to its chapter folder.

### Integration test alignment

Each chapter maps to one `[SkippableFact]` in `BrunoIntegrationTests.cs`:

| Test name | Chapter folder |
|---|---|
| `Bruno_01_greeting_requests_all_pass` | `01-greeting/` |
| `Bruno_02_books_requests_all_pass` | `02-books/` |
| `Bruno_03_data_records_requests_all_pass` | `03-data-records/` |
| `Bruno_04_catalog_requests_all_pass` | `04-catalog/` |

This closes the gap where `data-records/` previously had no automated test coverage.

## Consequences

- Any new capability or entity added to `Application.Sample` gets a new numbered
  chapter; existing chapter numbers are never changed.
- The `demo/` folder and the top-level `books/` and `data-records/` folders are removed.
- CI scripts that pinned `demo/` or `books/` must update to use the new chapter paths.
- Adding a request inside an existing chapter automatically extends the coverage of
  its corresponding `[SkippableFact]`.
