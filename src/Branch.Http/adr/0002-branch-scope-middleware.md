# 0002 — BranchScopeMiddleware and carrier types in Forge.Branch.Http

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

Execution.Http ADR-0002 documents the decision to move the branch HTTP carrier types —
`BranchScopeMiddleware`, `IBranchIriProvider`, `HeaderBranchIriProvider`,
`InvalidBranchIriException`, `AddBranchHttp()`, and `UseBranchScope()` — out of
`Forge.Execution.Http` and into `Forge.Branch.Http`.

This ADR records the Branch.Http side of that decision: what was received, why it belongs
here, and the resulting conventions that govern the branch HTTP carrier going forward.

## Decision

`Forge.Branch.Http` owns all branch HTTP carrier infrastructure:
- `IBranchIriProvider` — strategy for extracting a branch IRI from an HTTP request.
- `HeaderBranchIriProvider` — default implementation reading `X-Forge-BranchIri`.
- `InvalidBranchIriException` — thrown for non-absolute-URI values; maps to 400.
- `BranchScopeMiddleware` (`internal sealed`) — establishes `BranchScope` per request;
  validates non-default IRIs against the management graph (404 on unknown IRI);
  echoes the effective IRI as `X-Forge-Effective-BranchIri` response header.
- `BranchHttpServiceCollectionExtensions.AddBranchHttp(IServiceCollection, IConfiguration)`
  — binds `BranchOptions` and registers `HeaderBranchIriProvider` as the singleton
  `IBranchIriProvider`.
- `BranchHttpApplicationBuilderExtensions.UseBranchScope(IApplicationBuilder)` —
  registers `BranchScopeMiddleware` in the pipeline.

All behavioural contracts from Execution.Http ADR-0001 apply without change.

## Consequences

- `Forge.Branch.Http` is the single package a host application needs for branch scope
  HTTP support. No separate Execution.Http package reference is required for branch wiring.
- The `X-Forge-BranchIri` (request) and `X-Forge-Effective-BranchIri` (response) header
  names are fixed by root ADR-0006 and must not change without a new root ADR.
- Callers using `using Forge.Execution.Http.DependencyInjection;` for branch registration
  must migrate to `using Forge.Branch.Http.DependencyInjection;`.
