# 0010 — Sub-folder structure inside slice directories

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

As slices grow, dropping all `.cs` files into a single flat directory makes it harder
to orient at a glance. A contributor opening `src/Aspects/` currently sees 30 files with
no grouping signal. A reader opening `src/Repository/` sees 18. Both exceed the point
where a quick look is enough to understand what is where.

## Options

1. **Graduated threshold with named sub-folders.** Apply a increasing file-count
   threshold: flat is preferred at ≤ 5 files, acceptable at 6–10, and sub-folders are
   recommended above 10. Above 20 files without sub-folders is a blocking review
   concern. Each sub-folder must be documented in a `SLICING.md` companion file.
2. Enforce a fixed limit of 8 files per folder, splitting aggressively.
   Pro: consistent. Con: creates trivially small folders; unnecessary churn for stable slices.
3. No structural rule; rely on PR review. Con: inconsistent outcomes; reviewer fatigue.

## Decision

Option 1. The graduated threshold is a **recommendation** that reviewers must enforce
at PR boundaries, not a CI hard-stop. Exceptions require a comment in the PR explaining
why the threshold is not meaningful for that slice.

### Threshold table

| File count (flat `.cs` files) | Recommendation |
|-------------------------------|----------------|
| ≤ 5 | Keep flat. Sub-folders add noise. |
| 6–10 | Flat is acceptable. Consider grouping if a clear boundary exists. |
| 11–20 | Sub-folders **recommended**. At least two named groups must emerge. |
| > 20 | Sub-folders **strongly recommended**; no sub-folders is a blocking review concern. |

### Exclusions

The following directories are **excluded** from the file count:

- **`DependencyInjection/`** — framework/architecture-driven; always its own sub-folder.
- **`adr/`** — ADR folders are excluded entirely; the threshold does not apply to ADRs.
- **`bin/`**, **`obj/`** — build artefacts.

Other framework-driven sub-folders (e.g. `Polyfills/`, `Generated/`) may be excluded
by documenting the exclusion in the slice's `SLICING.md`.

### Sub-folder path equals namespace

The folder path inside a slice **must** map 1-to-1 to a C# sub-namespace.
A file in `src/Aspects/Message/` must carry `namespace Forge.Aspects.Message;`.
This follows ADR-0004's rule that assembly name = root namespace and extends it
downward into sub-folders.

### SLICING.md companion

When a slice introduces sub-folders it must create `src/<Slice>/adr/SLICING.md`.
The file documents:
- Each sub-folder name, the sub-concern it represents, and which files belong there.
- The rule that determines where a new file goes.
- Any framework-driven sub-folders excluded from the threshold.

`SLICING.md` is a **living document** (not an append-only ADR); it is updated in the
same PR that adds or moves a file.

## Consequences

- Slices that already exceed 10 flat files are refactored as part of accepting this ADR.
  `src/Aspects/` (30 files) and `src/Repository/` (18 files, excluding its existing `Rdf/`
  sub-folder) are the first two.
- New slices start flat and introduce sub-folders only when the threshold is reached.
- Sub-namespace introductions are not breaking changes for internal platform code, but
  they require updating all `using` directives across the repository.
- `SLICING.md` is the single authoritative guide for "where does this file go?" within
  any sub-divided slice.
