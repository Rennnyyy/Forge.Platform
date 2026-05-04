# 0001 — Configuration-switchable repository backend

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

`samples/Application.Sample` originally hard-coded `UseInMemory()` in `Program.cs` with
a comment suggesting to swap it for `UseGraphDb()` manually. This prevents the sample from
serving as a realistic end-to-end demonstration against a real Ontotext GraphDB instance
without a code change.

ADR-0005 (root) specifies that backend selection is configuration-driven via
`Forge:EntityRepository:Backend`. The repository infrastructure is already wired to
read `EntityRepositoryOptions` from that section; the missing piece is the `Program.cs`
dispatch from configuration to the correct `Use*()` call.

A `UseFromConfiguration` helper cannot live in `Forge.Repository` because that slice
knows nothing about `InMemory` or `GraphDb` — both depend on it, not the other way round.
A dedicated "hosting integration" slice (`Forge.Hosting` or similar) is a possible future
concern when more samples share the same pattern; for now it would be over-engineering.

## Options

1. **Local dispatch in `Program.cs`** — read `Forge:EntityRepository:Backend` from
   `builder.Configuration` and call `UseInMemory()` or `UseGraphDb()` with a two-branch
   `if` statement. No new library slice required.
2. **`UseFromConfiguration` extension in a new `Forge.Hosting` slice** — a thin slice
   that depends on all backends and provides the dispatch helper. Reusable across all
   future samples.
   Con: premature generalization; creates a new slice for a two-line if statement; the
   "Hosting" surface is entirely undefined at this point.
3. **Keep the hard-coded `UseInMemory()` comment pattern** — status quo; developer swaps
   the call manually for GraphDB runs.
   Con: poor DX; prevents runtime switching via environment variables or appsettings.

## Decision

Option 1. `Program.cs` reads `Forge:EntityRepository:Backend` from configuration and
dispatches to `UseInMemory()` or `UseGraphDb()` with an explicit two-branch `if`.

- Default backend is `"InMemory"` (no infrastructure required).
- Switching to GraphDB requires either:
  - Setting `Forge__EntityRepository__Backend=GraphDb` as an environment variable, **or**
  - Overriding via `appsettings.{ASPNETCORE_ENVIRONMENT}.json`.
- GraphDB connection details are read from `Forge:GraphDb:*` (see `GraphDbOptions`).

If a `Forge.Hosting` slice is introduced in the future, the dispatch can migrate there;
the `Program.cs` call site would shrink to a single `UseFromConfiguration()` call.

## Consequences

- `Application.Sample` can be switched to GraphDB at runtime without recompiling.
- The `Forge.Repository.GraphDb` project reference is always present in the sample's
  `.csproj`; both backends are always compiled into the output.
- The root `appsettings.json` defaults to `InMemory` so `dotnet run` works without
  any infrastructure.
- Bruno integration tests (ADR-0012) continue to use `InMemory` (no changed environment).
