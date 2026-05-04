# 0011 — Samples folder for runnable demonstration applications

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

The repository currently has two top-level code folders:

| Folder | Contents |
|--------|----------|
| `src/` | Production library slices |
| `tests/` | xUnit test projects |

There is no defined home for **runnable demonstration applications** that wire multiple
slices together to show end-to-end usage. Such applications are:

- Not libraries (no public NuGet surface).
- Not test projects (they do not run assertions; `dotnet test` ignores them).
- Valuable as living, buildable API-usage documentation.

ADR-0007 anticipated a future `Samples` project; this ADR establishes where and how it lives.

## Options

1. **New top-level `samples/` folder**, mirroring the `src/` / `tests/` precedent.
   Each sample is a `Microsoft.NET.Sdk.Web` or `Microsoft.NET.Sdk` project named
   `Forge.<Slice>.Sample` (or `Forge.<Slice>.Http.Sample`) under
   `samples/<Slice>.Sample/`. All samples are added to the solution under a
   `/samples/` solution folder.
2. Put samples under `tests/`. Con: conflates demo apps with automated verification;
   `dotnet test` may attempt to discover tests in them; the name misleads contributors.
3. Put samples under `src/`. Con: shipped artifacts (NuGet source) would include demo
   apps; `IsPackable=false` is needed but the folder semantics still mislead.

## Decision

Option 1.

### Naming and layout rules

| Rule | Detail |
|------|--------|
| Folder | `samples/<Slice>.Sample/` or `samples/<Slice>.<Transport>.Sample/` |
| Project name | Matches the folder; e.g. `Forge.Capability.Http.Sample.csproj` |
| Root namespace | `Forge.Capability.Http.Sample` (ADR-0004 pattern; `Sample` is the library segment) |
| SDK | `Microsoft.NET.Sdk.Web` for web apps; `Microsoft.NET.Sdk` for console apps |
| `IsPackable` | Always `false` |
| Solution folder | `/samples/` |
| `TreatWarningsAsErrors` | Inherits `true` from `Directory.Build.props`; samples must compile cleanly |

### Scope

Sample projects may reference any combination of `src/` slices. They must not be
referenced by any `src/` or `tests/` project — the dependency arrow points
`samples/ → src/` only.

### ADR-0002 note

Samples target `net10.0` and use `Microsoft.AspNetCore.App` via the Web SDK, consistent
with the platform-wide target framework established in ADR-0002.

## Consequences

- A `samples/` tree is now a recognized part of the repository layout.
- `dotnet build` and `dotnet test` from the solution root include samples in the build
  graph (build artefacts are validated) but `dotnet test` runs no tests from them.
- Sample projects serve as canonical usage examples for onboarding contributors.
