# 0016 — Bruno collection expanded to 13 chapters

- **Status**: accepted; amends [0013](0013-bruno-collection-story-structure.md)
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0013 defined the story-chapter structure for the Bruno integration collection and
documented four chapters (`01-greeting` through `04-catalog`). Since that ADR was accepted,
nine additional chapters were added to `samples/Application.Sample/bruno/` and matching
`[SkippableFact]` tests were added to `BrunoIntegrationTests.cs`. The ADR was never
updated, leaving the documented chapter list nine chapters out of date.

## Decision

The story-chapter conventions from ADR-0013 (numeric prefix, `NN-kebab-case/` folders,
`NN-kebab-case.bru` requests, `seq: NN` inside each file) remain in force without change.

The complete chapter inventory, including the nine chapters added after ADR-0013, is:

| Chapter | Folder | What it demonstrates |
|---------|--------|----------------------|
| 01 | `01-greeting/` | Capability handler; aspect-IRI header passthrough |
| 02 | `02-books/` | Generated CRUD for `Book` (minimal scalar surface) |
| 03 | `03-data-records/` | Generated CRUD for `DataRecord` (all scalar CLR types) |
| 04 | `04-catalog/` | Hand-written POST/PUT/PATCH capability handlers |
| 05 | `05-error-demo/` | Error handling: 4xx/5xx status codes and `ExecutionError` responses |
| 06 | `06-capability-aspect-demo/` | Capability-level SHACL guards enforced via aspect IRI header |
| 07 | `07-entity-aspect-demo/` | Entity-level SHACL guards on write operations |
| 08 | `08-update-aspect-combined/` | Combined capability + entity aspects in a single dispatch |
| 09 | `09-artists/` | Generated CRUD for `Artist` (music domain entity) |
| 10 | `10-genres/` | Generated CRUD for `Genre` (music domain entity) |
| 11 | `11-recordings/` | Generated CRUD for `Recording` (music domain entity) |
| 12 | `12-studios/` | Generated CRUD for `Studio` (music domain entity) |
| 13 | `13-featured-artists/` | Generated CRUD for `FeaturedArtist` (many-to-many relationship entity) |

### Integration test alignment

Each chapter maps to one `[SkippableFact]` in `BrunoIntegrationTests.cs`:

| Test name | Chapter folder |
|-----------|----------------|
| `Bruno_01_greeting_requests_all_pass` | `01-greeting/` |
| `Bruno_02_books_requests_all_pass` | `02-books/` |
| `Bruno_03_data_records_requests_all_pass` | `03-data-records/` |
| `Bruno_04_catalog_requests_all_pass` | `04-catalog/` |
| `Bruno_05_error_demo_requests_all_pass` | `05-error-demo/` |
| `Bruno_06_aspect_demo_requests_all_pass` | `06-capability-aspect-demo/` |
| `Bruno_07_entity_aspect_demo_requests_all_pass` | `07-entity-aspect-demo/` |
| `Bruno_08_update_aspect_combined_requests_all_pass` | `08-update-aspect-combined/` |
| `Bruno_09_artists_requests_all_pass` | `09-artists/` |
| `Bruno_10_genres_requests_all_pass` | `10-genres/` |
| `Bruno_11_recordings_requests_all_pass` | `11-recordings/` |
| `Bruno_12_studios_requests_all_pass` | `12-studios/` |
| `Bruno_13_featured_artists_requests_all_pass` | `13-featured-artists/` |

## Amendment to ADR-0013

The chapter table in ADR-0013 § "Chapter layout" is superseded by the table above.
All other conventions (naming, `seq` fields, `[SkippableFact]` approach, skip condition
when `npx` is absent) remain unchanged.

## Consequences

- ADR-0013 is the stable design record; this ADR is the living inventory of chapters.
- Any new chapter added to the collection must update the table in this ADR.
- Chapter numbers already assigned are immutable; gaps are not permitted.
