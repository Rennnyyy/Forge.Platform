# 0007 — Shared test fixtures project for sample entities

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`tests/Repository.Tests/` owns a rich set of sample entities (Artist, Album, Label,
Track) together with `EntityOptionsFixture` that sets `EntityOptions.BaseIri`. A second
backend test project (`Repository.GraphDb.Tests`) needs the same domain model to
validate the same scenarios against a live GraphDB instance. Until now it consumed those
files via `<Compile Include>` MSBuild file-links — both projects compiled the source files
into their own assemblies, each carrying the namespace `Forge.Repository.Tests.Sample`.

This approach has three problems:

1. **Roslyn source generators** run per project. File-linking causes the generator to emit
   partial implementations independently in each project. Any change to an entity definition
   must be reflected correctly in two generated outputs rather than one.
2. **Namespace coupling**: the types are named after the owning test project, spreading a
   test-internal coordinate across consumers.
3. **Scalability**: a future `Samples` project or additional backend test slices cannot
   add a `ProjectReference` to a **test** project.

## Options

1. **Dedicated `tests/Entity.Tests.Fixtures/` project** — a non-test, non-packable class
   library that contains the sample entities and `EntityOptionsFixture`.  All consumers add
   a `ProjectReference`; the generator runs once; the namespace is stable and consumer-neutral.
2. Keep `<Compile Include>` links, only rename the namespace.
   Pro: no new project. Con: generator-duplication and type-identity issues remain.
3. Move the sample entities into `src/Entity/` as example types.
   Con: ships them to production consumers; violates the src/tests boundary.

## Decision

Option 1.

- New project: `tests/Entity.Tests.Fixtures/Forge.Entity.Tests.Fixtures.csproj`
- Root namespace: `Forge.Entity.Tests.Fixtures` (follows ADR-0004; `Tests.Fixtures` is the
  library segment for shared test support).
- Sample entities live under the `Forge.Entity.Tests.Fixtures.Sample` sub-namespace.
- `EntityOptionsFixture` lives directly under `Forge.Entity.Tests.Fixtures`; it carries no
  xUnit dependency — each consumer declares its own `[CollectionDefinition]` that wires it.
- The project has `<IsPackable>false</IsPackable>` and is never published to NuGet.
- The project is added to the `/tests/` solution folder.
- `Repository.Tests` and `Repository.GraphDb.Tests` replace their current
  entity consumption mechanism with a plain `<ProjectReference>` to the fixtures project.

## Consequences

- The Roslyn source generator runs exactly once for the shared entities.
- The sample entity namespace is stable: `Forge.Entity.Tests.Fixtures.Sample`.
- Any future test slice (e.g. `Sparql.Tests` or a `Samples` project) gains a single
  `<ProjectReference>` to get the full domain model and fixture helpers.
- `Repository.Tests` no longer owns the sample entity source files.
- The `<Compile Include>` hack in `Repository.GraphDb.Tests` is eliminated.
