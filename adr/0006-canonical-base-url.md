# 0006 — Canonical base URL is https://forge-it.net

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

Throughout the codebase (source comments, default field values, ADR examples, and test
fixtures) two placeholder URLs have been used interchangeably:

- `https://forge.example` — used in examples and test fixtures as a realistic-looking IRI prefix.
- `https://forge.local` — used as the hard-coded default value for `EntityOptions.BaseIri`
  and `EntityOptionsInstance._baseIri`.

`forge.example` and `forge.local` are synthetic; `forge-it.net` is the real, registered domain
for the Forge project. Using the real domain avoids confusion for contributors about where the
project actually lives, makes documentation copy-paste safe, and ensures defaults in shipped
binaries point to a meaningful address.

## Options

1. **Replace all `forge.example` and `forge.local` occurrences with `https://forge-it.net`.**
   One search-and-replace pass covers source files, ADR prose examples, and test fixtures.
   The decision is recorded here so future contributors do not re-introduce synthetic placeholders.
2. Keep `forge.example` (RFC-2606 reserved) for examples and use `forge-it.net` only in
   production defaults.
   Pro: strict separation of example and real URLs. Con: two different base URLs make tests
   less representative of production; contributors must remember which to use where.
3. Use `localhost` or a loopback address as the default.
   Pro: no external dependency implied. Con: IRIs are global by definition; a `localhost`
   IRI is semantically wrong and misleading.

## Decision

Option 1. `https://forge-it.net` is the single canonical base URL for the Forge platform.

- All existing `https://forge.example` occurrences in source code, tests, and ADR prose are
  replaced with `https://forge-it.net`.
- All existing `https://forge.local` default field values are replaced with `https://forge-it.net`.
- No new `forge.example` or `forge.local` strings are introduced anywhere in the repository.

## Consequences

- Contributors and documentation readers always see the real project URL.
- Test fixtures exercise exactly the same IRI root that a real deployment would use.
- Searching the repository for `forge.example` or `forge.local` should yield zero results
  (useful as a CI lint check in the future).
