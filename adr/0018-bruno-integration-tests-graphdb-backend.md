# 0018 — Bruno integration tests also run against GraphDB backend

- **Status**: accepted
- **Date**: 2026-05-12
- **Author**: agent

## Context

ADR-0012 established that `tests/Application.Sample.Tests/` drives the committed Bruno
collection against a `dotnet exec`-hosted sample app. That test class
(`BrunoIntegrationTests`) always starts the app with the InMemory backend
(`Forge:EntityRepository:Backend = InMemory`), meaning the GraphDB code-path
(`Repository.GraphDb`) is never exercised by the Bruno suite.

`Repository.GraphDb.Tests` already contains a `GraphDbFixture` that spins up a GraphDB
container via the bundled `docker-compose.graphdb.yml` and skips gracefully when no
container runtime is present. The same lifecycle pattern can be reused to back the full
Bruno suite with a live GraphDB instance.

## Options

1. **Parallel GraphDB test class** — add `BrunoGraphDbFixture` (mirrors `GraphDbFixture`)
   and `BrunoGraphDbIntegrationTests` (mirrors `BrunoIntegrationTests`) in the same
   `Application.Sample.Tests` project. The fixture manages the container; the test class
   starts the sample app with `Forge__EntityRepository__Backend=GraphDb` and the fixture's
   coordinates. Skips gracefully when no container runtime is present (`fixture.Available`).
2. **Parameterise the existing test class over backends** — make `BrunoIntegrationTests`
   backend-aware through a `[MemberData]` or xUnit theory approach.
   Con: harder to read; skip semantics become complex (InMemory should never skip, only
   GraphDB can skip).
3. **CI-only second test run** — same test class, started a second time with different
   env vars via a CI pipeline step. Con: no integration in the test codebase; not runnable
   locally in the same `dotnet test` run.

## Decision

Option 1.

### Mechanics

- `BrunoGraphDbFixture` is an `IAsyncLifetime` xUnit collection fixture that:
  - Probes `http://localhost:7200/rest/repositories`.
  - If unreachable, runs `{cli} compose up -d` using a bundled
    `docker-compose.graphdb.yml` (copy of the one in `Repository.GraphDb.Tests`).
  - Waits up to 90 s for GraphDB to become healthy.
  - Ensures repository `forge-sample-tests` exists (separate from `forge-tests` used by
    `Repository.GraphDb.Tests` to avoid cross-suite interference).
  - Exposes `Available`, `BaseUrl`, `RepositoryId`, and `ClearAsync()`.
  - On dispose, tears down the compose stack if the fixture started it.
- `BrunoGraphDbIntegrationTests` is `[Collection("BrunoGraphDb")]` + `IAsyncLifetime`:
  - Constructor injects `BrunoGraphDbFixture`.
  - `InitializeAsync`: calls `fixture.ClearAsync()` (wipes any leftover triples from a
    prior test run), picks a free port, starts the sample app with
    `ASPNETCORE_ENVIRONMENT=Development`,
    `Forge__EntityRepository__Backend=GraphDb`,
    `Forge__GraphDb__BaseUrl=<fixture.BaseUrl>`,
    `Forge__GraphDb__RepositoryId=<fixture.RepositoryId>`,
    then waits for the app probe endpoint.
  - `DisposeAsync`: kills the app process.
  - All 15 `[SkippableFact]` tests skip when `!IsNpxAvailable() || !_graphDb.Available`.

### Repository separation

`forge-sample-tests` is distinct from the `forge-tests` repository used by
`Repository.GraphDb.Tests`. The two test projects can therefore run in parallel on the
same GraphDB instance without interfering with each other.

## Consequences

- Every Bruno chapter is now validated against both the InMemory and GraphDB backends.
- Machines without a container runtime (or GraphDB) continue to get full InMemory
  coverage; the GraphDB variant skips cleanly.
- `docker-compose.graphdb.yml` is duplicated into `tests/Application.Sample.Tests/`
  to keep the test project self-contained (no cross-project file references at runtime).
- Any new Bruno chapter added under `samples/Application.Sample/bruno/` must add a
  matching `[SkippableFact]` in **both** `BrunoIntegrationTests` and
  `BrunoGraphDbIntegrationTests`.
