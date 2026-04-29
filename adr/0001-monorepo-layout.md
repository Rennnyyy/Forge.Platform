# 0001 — Single-solution monorepo with src/ and tests/

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Forge is a platform composed of multiple libraries (Entity, future slices). We need a layout that supports CPM, shared build settings, and per-slice ADRs without splitting into several repositories.

## Options

1. **Single solution, src/ + tests/**, slice-per-folder under `src/`. Test projects mirror slice names under `tests/`.
2. Multiple solutions, one per slice. Pro: isolation. Con: shared settings duplicated; coordinated changes painful.
3. No solution file, plain folders. Pro: minimal. Con: poor IDE story, no `dotnet build` over the whole repo.

## Decision

Option 1. The repository has exactly one `Forge.Platform.slnx` at the root. Source projects live in `src/<Slice>/`. Their tests live in `tests/<Slice>.Tests/`. Generator projects live next to the slice they belong to (e.g. `src/Entity.Generators/`).

## Consequences

- Shared `Directory.Build.props` and `Directory.Packages.props` apply to every project.
- Slice-local conventions go into `src/<Slice>/adr/`.
- Adding a slice = `dotnet new classlib`, `dotnet sln add`, write its first ADR.
