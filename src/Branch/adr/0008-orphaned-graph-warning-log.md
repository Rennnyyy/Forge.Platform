# 0008 — Orphaned-graph warning log in BranchSeedingService

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

`BranchSeedingService.CreateSeededBranchAsync` and `CreateSnapshotAsync` commit two
separate stores in sequence: the data-graph seed (`SeedGraphOperation`) first, then
the management-graph entity write (`CreateOperation`). This two-phase commit has no
rollback mechanism: if the management write fails after a successful seed, the named
graph written to the data store remains as an orphan — a named graph with no
corresponding management entity.

Branch ADR-0003 documents this limitation explicitly:

> *"If the management write fails after a successful seed, the seeded graph becomes an
> orphan until a subsequent attempt or manual cleanup."*

Until now there was no signal to an operator when this happened. A partial failure was
silent: the management write exception propagated to the caller (correct), but no
structured log event identified the orphaned IRI for later remediation.

## Decision

Inject `ILogger<BranchSeedingService>` into `BranchSeedingService` alongside the
existing constructor parameters. Wrap the management-write step in both
`CreateSeededBranchAsync` and `CreateSnapshotAsync` in a `try/catch`. On any exception:

1. Log a **Warning** event that includes:
   - A human-readable message identifying the failure as "management write failed after
     successful data-graph seed".
   - A structured `{BranchIri}` / `{SnapshotIri}` property carrying the IRI of the
     orphaned named graph.
   - The captured exception as the log event's `Exception` parameter.
2. Re-throw the exception unchanged so the caller receives the original failure.

The warning is emitted at `Warning` level (not `Error`) because the data is not lost:
the seeded content is present in the data store and the orphan can be recovered by
retrying the operation or by issuing a `DropGraph` transaction manually.

## Consequences

- Operators running with structured logging (e.g. Seq, OpenTelemetry) receive a
  queryable `{BranchIri}` / `{SnapshotIri}` field whenever a partial failure occurs.
  Standard monitoring rules can alert on `EventId` or on the presence of this field.
- No transactional rollback is added. The two-store commit design and its known
  limitation remain unchanged; this ADR only adds observability.
- `Forge.Branch.csproj` gains a `PackageReference` to
  `Microsoft.Extensions.Logging.Abstractions` (already in `Directory.Packages.props`).
- `BranchSeedingService` constructor gains a required `ILogger<BranchSeedingService>`
  parameter. DI hosts that register `BranchSeedingService` via `AddForgeBranch()` or
  `AddForgeBranchHttp()` receive loggers automatically from the ASP.NET Core logging
  infrastructure; test hosts must supply a `NullLogger` instance.
