# SLICING.md — `src/Entity.Generators/`

## Sub-folder overview

| Folder | Sub-concern | Files |
|--------|-------------|-------|
| `Polyfills/` | Roslyn / netstandard2.0 shim types | `IsExternalInit.cs` |

## Placement rule

All source-generator files (`EntityGenerator.cs`, `EntityParser.cs`, `EntityEmitter.cs`,
`EntityModel.cs`, `EntityDiagnostics.cs`) are kept flat in the slice root because there
are fewer than 10 flat files (ADR-0010 threshold) and no clear grouping boundary has
emerged.

New files belong in the slice root unless they are:
- **Polyfills**: types that exist in later BCL versions but are absent from `netstandard2.0`.
  These go in `Polyfills/`. The sub-folder is excluded from the flat-file threshold count
  because it is framework-driven, not design-driven.

## Excluded sub-folders

- `Polyfills/` — excluded from the ADR-0010 flat-file threshold per the "framework-driven
  sub-folders" exemption in that ADR.
- `adr/` — excluded per ADR-0010.
- `bin/`, `obj/` — excluded per ADR-0010.
