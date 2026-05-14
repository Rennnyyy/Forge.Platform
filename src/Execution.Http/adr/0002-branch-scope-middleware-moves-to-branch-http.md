# 0002 — BranchScopeMiddleware and carrier types move to Forge.Branch.Http

- **Status**: accepted; supersedes [0001](0001-branch-http-carrier.md)
- **Date**: 2026-05-11
- **Author**: agent

## Context

Execution.Http ADR-0001 placed `BranchScopeMiddleware`, `IBranchIriProvider`,
`HeaderBranchIriProvider`, `InvalidBranchIriException`, `AddBranchHttp()`, and
`UseBranchScope()` inside `Forge.Execution.Http`. The rationale at the time was
"no new package required". This introduced a layering violation: the HTTP execution
carrier (`Forge.Execution.Http`) was importing a domain slice (`Forge.Branch`) solely
to read `BranchOptions`. The dependency graph became:

```
Forge.Execution.Http → Forge.Branch → Forge.Repository
```

`Forge.Execution.Http` is a general HTTP infrastructure layer; pulling branch-domain
knowledge into it means every consumer of `Forge.Execution.Http` implicitly depends on
the branch subsystem, even if they never use branches.

`Forge.Branch.Http` already exists and already carries all other branch-specific HTTP
concerns (CRUD endpoints, merge capability, aspect wiring). The branch scope middleware
belongs there: it is a branch-domain concern expressed at the HTTP layer.

## Decision

Move `BranchScopeMiddleware`, `IBranchIriProvider`, `HeaderBranchIriProvider`, and
`InvalidBranchIriException` from `Forge.Execution.Http` to `Forge.Branch.Http`.
Move `AddBranchHttp()` from `ExecutionHttpServiceCollectionExtensions` to
`BranchHttpServiceCollectionExtensions`. Introduce a new
`BranchHttpApplicationBuilderExtensions` in `Forge.Branch.Http.DependencyInjection`
with `UseBranchScope()`. Remove the `Forge.Branch` `ProjectReference` from
`Forge.Execution.Http.csproj`. The namespace of all moved types changes from
`Forge.Execution.Http` to `Forge.Branch.Http`.

## Consequences

- `Forge.Execution.Http` no longer depends on `Forge.Branch`. Its dependency graph is now:
  `Forge.Execution.Http → Forge.Aspects.Abstractions, Forge.Execution, Forge.Repository`.
- Callers that used `using Forge.Execution.Http.DependencyInjection;` for `AddBranchHttp` /
  `UseBranchScope` must switch to `using Forge.Branch.Http.DependencyInjection;`.
- `BranchScopeMiddlewareTests` and `HeaderBranchIriProviderTests` move from
  `Forge.Execution.Http.Tests` to `Forge.Branch.Http.Tests`.
- The header names (`X-Forge-BranchIri`, `X-Forge-Effective-BranchIri`) and all
  behavioural contracts from ADR-0001 remain unchanged.
