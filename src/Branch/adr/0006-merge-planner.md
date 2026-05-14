# 0006 — Merge planner and topological sort (`IMergePlanner`)

- **Status**: accepted
- **Date**: 2026-05-14
- **Author**: agent

## Context

The diff engine (ADR-0004) produces an `EntityGraphDelta`: a list of entity IRIs, their
`rdf:type` IRIs, and whether each entry is `Added` or `Modified`. Before this list can be
submitted as a single `EntityTransaction` (ADR-0015), two preparation steps are required:

1. **Hydration** — each entity IRI must be loaded from the source graph to obtain the
   entity instance required by `CreateOperation<T>` / `UpdateOperation<T>`.
2. **Topological ordering** — entities with `[Owning]` references to other entities in the
   same batch must be written *after* their owned targets exist in the store. A naively
   ordered list risks `EntityAlreadyExistsException` or referential-integrity violations
   depending on the backend.

Both steps require knowledge that is local to the Repository slice
(`IRdfMapperRegistry`, `TransactionOperation` hierarchy) but the orchestrating logic belongs
in `Forge.Branch` (ADR-0007). The planner is the seam between them.

`IMergePlanner` is exposed as a public injectable interface so implementations can be
replaced in tests with deterministic stubs without requiring a live store.

## Options

### Option A — Planner handles hydration and ordering (full plan)

`PlanAsync` accepts the `EntityGraphDelta` plus both stores (source for hydration, target
for create-vs-update detection), and returns a fully ordered `IReadOnlyList<TransactionOperation>`.
Pro: single responsibility surface; callers (merge service) are thin.
Con: planner signature is heavier; hydration is async and pulls entity instances from the
source graph.

### Option B — Sorter only; merge service handles hydration

`Plan` accepts a pre-hydrated list of entities and only sorts. Pro: pure, synchronous,
easily testable. Con: the merge service must know how to hydrate entities generically
(resolving type from `EntityDeltaEntry.TypeIri` via the registry) — this logic is
non-trivial and belongs closer to the registry, not in BranchMergeService.

### Option C — Separate `IEntityHydrator` and `IMergePlanner`

Two interfaces, two registrations. Pro: maximal decomposition. Con: two additional
abstractions for what is a single well-bounded workflow; over-engineering for current
scope.

## Decision

**Option A.**

`IMergePlanner` is responsible for hydration and ordering. The source store is used for
hydration; the target store is used only for existence checks (Create vs Update
discrimination).

### Interface

```csharp
public interface IMergePlanner
{
    /// <summary>
    /// Hydrates each entry from <paramref name="delta"/> using <paramref name="sourceStore"/>,
    /// determines create-vs-update against <paramref name="targetStore"/>, and returns
    /// a topologically ordered list of <see cref="TransactionOperation"/> instances safe
    /// to submit to <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/>.
    /// </summary>
    /// <exception cref="MergePlanCycleException">
    /// Thrown if a circular owning dependency is detected among the entities in the delta.
    /// This indicates a corrupted or inconsistent entity graph — circular owning is
    /// forbidden by the mapper (Repository ADR-0013).
    /// </exception>
    /// <exception cref="MergePlanUnresolvableTypeException">
    /// Thrown if <see cref="IRdfMapperRegistry.ForTypeIri"/> returns null for any entry's
    /// <see cref="EntityDeltaEntry.TypeIri"/>. The entity type must be registered at DI time.
    /// </exception>
    Task<IReadOnlyList<TransactionOperation>> PlanAsync(
        EntityGraphDelta delta,
        IEntityStore sourceStore,
        IEntityStore targetStore,
        CancellationToken cancellationToken = default);
}
```

### `MergePlanner` implementation (`internal sealed`)

Registered as `IMergePlanner` singleton by `AddForgeBranch()`.

#### Step 1 — Type resolution

For each `EntityDeltaEntry` in `delta.Entries`, call
`IRdfMapperRegistry.ForTypeIri(entry.TypeIri, options)` to get the `IRdfMapper`. Throw
`MergePlanUnresolvableTypeException` if the result is null.

#### Step 2 — Hydration from source

Call `sourceStore.LoadAsync<T>(entry.EntityIri)` for each entry, scoped to the source
graph via `BranchScope.Use(entry.SourceGraphIri)`. Hydration uses the already-loaded
`IRdfMapper<T>` (cast from `IRdfMapper`). A null hydration result (entity IRI not found
in source) indicates a race condition; throw `MergePlanHydrationException` with the IRI.

#### Step 3 — Create vs Update discrimination

For each hydrated entity, call `targetStore.LoadAsync<T>(iri)` scoped to the target graph.
- Null result → `CreateOperation<T>(entity)`
- Non-null result → `UpdateOperation<T>(entity)`

The `EntityDeltaKind` on the delta entry is used as a sanity hint but the live target
check is authoritative (the delta was computed before this call; the target may have
changed).

#### Step 4 — Dependency graph construction

Using the `IRdfMapper` for each entity, enumerate `[Owning]` properties via reflection on
the entity's CLR type. For each `[Owning]` `EntityRef<T>` or `EntityRefCollection<T>`
property that is already resolved (non-lazy), read the referenced IRI(s). If the
referenced IRI is also present in the batch, record a directed edge:
`referencedIri → owningEntityIri` (referenced must come first).

#### Step 5 — Kahn's algorithm

Perform a standard Kahn's topological sort over the dependency graph. The algorithm:
1. Build `in-degree` map for all nodes (entity IRIs in the batch).
2. Enqueue all zero-in-degree nodes.
3. Dequeue, emit the operation, decrement in-degree of successors, enqueue newly
   zero-in-degree nodes.
4. If any nodes remain after processing all reachable nodes, a cycle exists →
   throw `MergePlanCycleException` listing the cycle participants.

Because circular owning is already a compile-time error caught by the mapper, hitting this
exception in practice indicates a corrupted graph state brought in from outside the
platform.

### Exception types

```csharp
/// <summary>A cycle was detected in the owning-dependency graph of the merge batch.</summary>
public sealed class MergePlanCycleException(IReadOnlyList<string> cycleIris)
    : InvalidOperationException($"Circular owning dependency detected among: {string.Join(", ", cycleIris)}");

/// <summary>
/// An entity IRI in the delta could not be loaded from the source graph during hydration.
/// </summary>
public sealed class MergePlanHydrationException(string iri)
    : InvalidOperationException($"Entity IRI '{iri}' not found in source graph during merge plan hydration.");

/// <summary>
/// An rdf:type IRI in the delta has no registered mapper. Register the entity type at DI time.
/// </summary>
public sealed class MergePlanUnresolvableTypeException(string typeIri)
    : InvalidOperationException($"No mapper registered for rdf:type IRI '{typeIri}'. Ensure the entity type is registered via AddForgeEntityRepository().");
```

### Registration

```csharp
// Inside AddForgeBranch():
services.AddSingleton<IMergePlanner, MergePlanner>();
```

## Consequences

- Hydration and ordering are co-located; the merge service (ADR-0007) delegates entirely
  and remains a thin coordinator.
- `IMergePlanner` is a `public` interface: test projects can inject a stub that returns a
  fixed ordered list without standing up stores.
- The Kahn-based sorter handles any DAG topology; no artificial constraints on batch size.
- Entities whose `[Owning]` refs point outside the batch (pre-existing entities in the
  store) are not included in the dep graph — they are assumed to already exist; no
  pre-check is performed for them in v1.
