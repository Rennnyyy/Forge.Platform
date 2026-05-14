# 0007 — Branch merge service (`BranchMergeService`)

- **Status**: accepted
- **Date**: 2026-05-14
- **Author**: agent

## Context

ADR-0004 introduces `IBranchDiffEngine` (directional diff between two named graphs).
ADR-0006 introduces `IMergePlanner` (hydrate + topological sort → `TransactionOperation` list).
Neither component alone constitutes a merge: they must be orchestrated into a single,
atomic operation with consistent error handling, store scoping, and observability output.

Merge semantics fixed by design session:

- **Upsert**: source entities are created-or-replaced in the target; entities that exist
  only in the target are untouched.
- **No conflict detection**: source-side values always win. Any target-side mutations since
  the fork are silently overwritten.
- **In-place**: the target named graph is mutated directly; no auto-snapshot is created.
- **Atomic**: the entire mutation list is applied in a single `ExecuteTransactionAsync`
  call. Partial application is not observable.
- **Event fan-out**: because `EventEmittingEntityStore` (root ADR-0021) wraps every
  `ExecuteTransactionAsync` call in the decorator chain, one `EntityChangedEnvelope` per
  mutated entity is emitted automatically. No extra eventing is needed here.
- **Pairwise only**: one source graph merged into one target graph per call.

## Options

### Option A — Extend `BranchSeedingService`

Add `MergeAsync` as a new method on `BranchSeedingService`.
Pro: no new class.
Con: seeding (copying from a full entity-IRI list) and merging (diff-computed upsert) are
distinct concerns with different dependency sets. Combining them inflates the constructor
and makes both operations harder to test in isolation.

### Option B — Dedicated `BranchMergeService`

New `internal sealed class BranchMergeService` registered scoped by `AddForgeBranch()`.
Pro: single responsibility; no shared state with seeding; injectable for testing;
consistent with the `BranchSeedingService` pattern (Branch ADR-0003).
Con: one more class — acceptable at this scope.

## Decision

**Option B.**

### Constructor dependencies

```csharp
internal sealed class BranchMergeService(
    IBranchDiffEngine diffEngine,
    IMergePlanner planner,
    ITransactionalEntityStore targetStore,          // unkeyed, full decorator chain
    IEntityStore sourceStore,                       // unkeyed, full decorator chain
    IOptions<EntityRepositoryOptions> repoOptions)
```

The `targetStore` and `sourceStore` share the same backing store instance; graph scoping
is applied per-operation via `BranchScope.Use(...)` inside the diff engine and planner.
The unkeyed stores include the full Guard → EventEmitting → AspectEnforcing → Backend
decorator chain (root ADR-0014, root ADR-0021).

### `MergeAsync`

```csharp
Task<BranchMergeResult> MergeAsync(
    string sourceBranchIri,
    string targetBranchIri,
    CancellationToken cancellationToken = default)
```

#### Pipeline

```
1. Validate inputs
   ├─ sourceBranchIri != targetBranchIri (same-graph merge is a no-op error)
   └─ both IRIs are non-null/empty

2. Compute diff
   delta = await diffEngine.ComputeDiffAsync(sourceBranchIri, targetBranchIri, ct)

3. Short-circuit on empty delta
   if (delta.IsEmpty) return BranchMergeResult.Empty(sourceBranchIri, targetBranchIri)

4. Plan
   operations = await planner.PlanAsync(delta, sourceStore, targetStore, ct)

5. Execute as single transaction scoped to the target branch
   using BranchScope.Use(targetBranchIri):
     await using var tx = new EntityTransaction(targetStore)
     foreach op in operations: tx.Add(op)
     await tx.CommitAsync(ct)

6. Return result
   return new BranchMergeResult(
       SourceBranchIri: sourceBranchIri,
       TargetBranchIri: targetBranchIri,
       CreatedCount: operations.OfType<CreateOperation<IEntity>>().Count(),  // via base type
       UpdatedCount: operations.OfType<UpdateOperation<IEntity>>().Count())  // via base type
```

`BranchScope.Use(targetBranchIri)` ensures that the `EntityTransaction.CommitAsync` writes
all mutations into the target named graph, not the ambient branch at the call site.
Source graph scoping for hydration is handled inside `IMergePlanner.PlanAsync` per
ADR-0006.

### `BranchMergeResult`

```csharp
public sealed record BranchMergeResult(
    string SourceBranchIri,
    string TargetBranchIri,
    int CreatedCount,
    int UpdatedCount)
{
    public int TotalCount => CreatedCount + UpdatedCount;
    public bool IsEmpty => TotalCount == 0;

    public static BranchMergeResult Empty(string source, string target) =>
        new(source, target, 0, 0);
}
```

### Error propagation

| Exception | Thrown by | Handling |
|-----------|-----------|----------|
| `ArgumentException` | `MergeAsync` | Same-IRI or empty IRI input |
| `MergePlanUnresolvableTypeException` | `IMergePlanner` | Propagated to caller |
| `MergePlanHydrationException` | `IMergePlanner` | Propagated to caller |
| `MergePlanCycleException` | `IMergePlanner` | Propagated to caller; indicates graph corruption |
| `NotSupportedException` | `ITransactionalEntityStore` | Backend does not support transactions; propagated |

The merge service does not swallow exceptions. The HTTP layer (if any) maps them to
appropriate problem-detail responses.

### Registration

```csharp
// Inside AddForgeBranch():
services.AddScoped<BranchMergeService>();
```

`BranchMergeService` is `internal sealed`; it is not registered under any public interface.
Dependent slices (e.g. `Forge.Branch.Http`) access it via the concrete type, consistent
with `BranchSeedingService`.

### HTTP exposure (deferred to `Forge.Branch.Http`)

A `POST /branches/{targetName}/merge` endpoint (or `POST /branches/{targetName}/merge-from/{sourceName}`)
is the natural HTTP surface. Mapping `sourceBranchIri` and `targetBranchIri` from branch
name slugs (using `EntityOptions.BaseIri` + Branch path convention) is the HTTP layer's
responsibility and is not decided here.

## Consequences

- The full merge pipeline — diff → plan → transact → event fan-out — is three method
  calls in a coherent scoped service.
- `BranchMergeResult` gives callers observable counts without requiring them to inspect
  internal state.
- Event fan-out via `EventEmittingEntityStore` (ADR-0021) is zero-cost to this ADR: it
  fires automatically as a consequence of `ExecuteTransactionAsync`.
- Rollback on partial failure is handled by the backend transaction strategy
  (Entity ADR-0015): InMemory restores the copy-on-write snapshot; GraphDB rolls back
  the native REST transaction.
- Multi-source (N-way) merges are out of scope. Callers who need them can issue multiple
  `MergeAsync` calls in sequence; intermediate states are observable between calls.
