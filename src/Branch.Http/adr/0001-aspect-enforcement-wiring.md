# 0001 — Aspect enforcement wiring for Branch and Snapshot HTTP endpoints

- **Status**: accepted
- **Date**: 2026-05-12
- **Author**: agent

## Context

Root ADR-0019 establishes that any hand-written HTTP layer for a platform-managed entity
must satisfy four obligations: shared response contracts (ADR-0017), aspect enforcement
on the backing store, aspect IRI threading through endpoint handlers, and managed-entity
store key registration. This ADR records how `Forge.Branch.Http` satisfies all four for
the `Branch` and `Snapshot` entity types.

## Decision

### `AddForgeBranchHttp(services, configuration)`

A new `BranchHttpServiceCollectionExtensions.AddForgeBranchHttp` extension method in
`Forge.Branch.Http.DependencyInjection` satisfies obligations 2 and 3:

```csharp
public static IServiceCollection AddForgeBranchHttp(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Wires AddForgeBranch (entity layer) and inserts AspectEnforcingTransactionalStore
    // on the management store key. The raw backend (before Branch/Snapshot guards)
    // is resolved under "forge.branch.management.raw" for SPARQL context queries.
    AddForgeBranch(services, configuration);
    services.AddForgeAspectsForKeyedStore(
        ManagementStoreKey,
        sp => (ISparqlQueryStore)sp.GetRequiredKeyedService<ITransactionalEntityStore>(
            ManagementStoreKey + ".raw"));
    return services;
}
```

Callers who do not use `Forge.Aspects` call `AddForgeBranch()` directly. The `AddForgeBranchHttp()`
overload is the right entry point for applications that also call `AddForgeAspects()`.

### Aspect IRI in endpoint handlers

All CUD handlers in `MapBranches()` and `MapSnapshots()` read the aspect IRI from the
`X-Forge-Operation-AspectIri` header via a shared `HeaderExecutionAspectIriProvider`:

```csharp
// Constructed once at MapBranches/MapSnapshots call time (not per-request):
IExecutionAspectIriProvider aspectIriProvider =
    new HeaderExecutionAspectIriProvider(
        OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader);

// Inside each CUD handler:
var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;
tx.Create(entity, aspectIri);   // or tx.Update / tx.Delete<T>
```

`BranchSeedingService.CreateSnapshotAsync` and `DeleteSnapshotAsync` receive `aspectIri`
as an explicit parameter and pass it to their management `EntityTransaction` operations.

### Why `AddForgeBranchHttp` and not a flag on `AddForgeBranch`

`AddForgeBranch` lives in `Forge.Branch`, which must not reference `Forge.Aspects`.
`AddForgeBranchHttp` lives in `Forge.Branch.Http`, which already references
`Forge.Execution.Http` (for `IExecutionAspectIriProvider`) and can acceptably reference
`Forge.Aspects.DependencyInjection` for `AddForgeAspectsForKeyedStore`. The HTTP layer
is the natural boundary where the decision to use aspects is made.

## Consequences

- `Forge.Branch.Http.csproj` adds a `<ProjectReference>` to `Forge.Aspects`.
- Applications that use managed branches should call `AddForgeBranchHttp()` instead of
  `AddForgeBranch()` when `AddForgeAspects()` is present.
- `BranchSeedingService` gains explicit `aspectIri` parameters — no ambient scope or
  thread-local state is used, making the data flow explicit and testable.
- The `ManagedEntityAspectValidationService` (Aspects ADR via root ADR-0019) will catch
  any misconfiguration at startup if `AddForgeBranch()` is called without
  `AddForgeAspectsForKeyedStore()` in an application that also calls `AddForgeAspects()`.
