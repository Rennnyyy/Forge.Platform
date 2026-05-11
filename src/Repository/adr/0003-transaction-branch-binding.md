# 0003 — Transaction branch binding and DropGraphOperation

- **Status**: accepted
- **Date**: 2026-05-10
- **Author**: agent

## Context

Repository ADR-0002 establishes `BranchScope` as the ambient propagation mechanism for
the active branch IRI. It states that transactions must be single-branch and that
`EntityTransaction` will snapshot the branch at construction time. This ADR specifies
how that snapshot flows through to `ITransactionalEntityStore.ExecuteTransactionAsync`
and introduces `DropGraphOperation` to support the atomic branch-delete semantics
decided in the brainstorm (deleting a branch entity cascades to dropping its named graph
within the same transaction).

Two pressures shape the design:

1. **Snapshot at construction, not at execute time.** A transaction enqueues operations
   over an arbitrary time window; the ambient `BranchScope` might change (e.g. a nested
   scope opens) between the first `Create` call and `CommitAsync`. Capturing the branch
   IRI once at construction prevents silent divergence.

2. **Explicit `branchIri` parameter, not ambient read inside the store.** The store
   must not re-read `BranchScope.Current` at execute time. Doing so would re-introduce
   the drift risk and make the single-branch invariant unenforceably by testing.

## Options

### Branch IRI on `ExecuteTransactionAsync`

**Option A** — Add `string branchIri` as an explicit parameter:
```csharp
ValueTask ExecuteTransactionAsync(
    IReadOnlyList<TransactionOperation> operations,
    string branchIri,
    CancellationToken cancellationToken = default);
```
Pros: unambiguous per-call; the store implementation is stateless relative to the branch.
Cons: breaking interface change — all callers of `ExecuteTransactionAsync` must be updated.

**Option B** — Store the branch IRI on each `TransactionOperation`.
Pros: no interface signature change. Cons: redundant (all operations in one transaction
share one branch); the invariant "all ops in one branch" becomes unenforceable by the
interface; stores must validate consistency across all ops on every execute.

**Option C** — Retain ambient read inside the store at execute time.
Cons: violates the snapshot-at-construction principle; untestable in isolation.

### `DropGraphOperation`

**Option 1** — New `DropGraphOperation : TransactionOperation` subtype.
Pros: dispatches through the existing `TransactionOperation` switch in every backend with
minimal change; no new interface member required; validates through the existing
`ExecuteTransactionAsync` pipeline unmodified.

**Option 2** — A new `DropGraphAsync(string graphIri)` method on `ITransactionalEntityStore`.
Pros: explicit for callers. Cons: splits the atomicity guarantee — a separate method call
outside the operation list cannot be part of the same transaction without extra coordination;
forces a new interface member on every store implementation.

## Decision

### A + 1: explicit `branchIri` on `ExecuteTransactionAsync` and `DropGraphOperation` subtype.

### `ITransactionalEntityStore.ExecuteTransactionAsync` — new signature

```csharp
/// <summary>
/// Executes <paramref name="operations"/> atomically against the named graph identified
/// by <paramref name="branchIri"/>. All operations target that single graph; the
/// single-branch invariant is enforced by the caller (<see cref="EntityTransaction"/>).
/// </summary>
ValueTask ExecuteTransactionAsync(
    IReadOnlyList<TransactionOperation> operations,
    string branchIri,
    CancellationToken cancellationToken = default);
```

The previous single-parameter overload is removed. No default is provided; callers must
supply the branch IRI explicitly.

### `EntityTransaction` — branch snapshot at construction

`EntityTransaction` is updated to snapshot `BranchScope.Current` from
`EntityRepositoryOptions.DefaultBranchIri` at construction:

```csharp
public sealed class EntityTransaction : IAsyncDisposable
{
    private readonly ITransactionalEntityStore _store;
    private readonly string _branchIri;
    // ...

    public EntityTransaction(ITransactionalEntityStore store, EntityRepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _branchIri = BranchScope.Current
            ?? (!string.IsNullOrWhiteSpace(options.DefaultBranchIri)
                ? options.DefaultBranchIri
                : throw new InvalidOperationException(
                    "No BranchScope is active and EntityRepositoryOptions.DefaultBranchIri is not configured."));
    }
}
```

`CommitAsync` passes `_branchIri` to `ExecuteTransactionAsync`:

```csharp
public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
{
    // ...
    await _store.ExecuteTransactionAsync(
        _operations.AsReadOnly(), _branchIri, cancellationToken).ConfigureAwait(false);
}
```

The `_branchIri` field is immutable after construction. No method on `EntityTransaction`
can change it. The branch a transaction was opened against is the branch it will commit to.

### `DropGraphOperation`

A new concrete `TransactionOperation` subtype for dropping a named graph in its entirety:

```csharp
/// <summary>
/// Drops the named graph identified by <see cref="GraphIri"/> from the RDF store.
/// Used by <c>Forge.Branch</c> to cascade-delete a branch's data graph when the
/// branch entity itself is deleted. See Repository ADR-0003.
/// </summary>
public sealed class DropGraphOperation : TransactionOperation
{
    public DropGraphOperation(string graphIri) => GraphIri = graphIri;

    /// <summary>The IRI of the named graph to drop.</summary>
    public string GraphIri { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// For a <see cref="DropGraphOperation"/> the "entity IRI" is the graph IRI itself —
    /// the graph as a whole is the target, not a specific subject within it.
    /// </remarks>
    public override string EntityIri => GraphIri;
}
```

Design notes:
- `AspectIri` inherited from `TransactionOperation` defaults to `Aspect.NoOpIri`. No
  SHACL validation applies to a graph-drop; the Aspects engine skips `DropGraphOperation`
  the same way it skips `DeleteOperation` when `AspectIri` is `NoOpIri`.
- `GraphIri` is independent of `branchIri` on the transaction: a `DropGraphOperation`
  for branch `B` issued inside a transaction whose `branchIri` is the management graph is
  the intended pattern for branch deletion (the management graph transaction atomically
  removes the branch entity and drops the branch data graph).
- Store implementations handle `DropGraphOperation` in their dispatch switch:
  - **GraphDB**: `DROP GRAPH <graphIri>` issuedinside the open transaction URL as a
    SPARQL Update (`PUT {txUrl}?action=UPDATE`).
  - **InMemory**: clear and remove the graph partition keyed by `graphIri`.

### Management graph transactions

The branch-delete pattern is:

```
// Management graph store (branchIri pinned to management graph, not from BranchScope)
await using var tx = managementGraphEntityOperations.BeginTransaction();
tx.Delete<Branch>(branchEntityIri);              // remove the Branch entity
tx.DropGraph(branchDataGraphIri);                // drop the branch data graph
await tx.CommitAsync();
```

`EntityTransaction.DropGraph(string graphIri)` is a new builder method (parallel to
`Create`, `Update`, `Delete`) that enqueues a `DropGraphOperation`.

## Consequences

- `ITransactionalEntityStore.ExecuteTransactionAsync` has a new required `branchIri`
  parameter. All implementations (`GraphDbEntityStore`, `InMemoryEntityStore`) and all
  callers (`EntityTransaction.CommitAsync`, `AspectEnforcingTransactionalStore`) must be
  updated. This is a planned breaking change; no external callers exist at this stage.
- `EntityTransaction` now requires `EntityRepositoryOptions` at construction. DI-obtained
  transactions (via `EntityOperations.BeginTransaction()`) will inject options via the
  ambient service locator already present in `EntityOperations`. Directly constructed
  `new EntityTransaction(store)` is a compile error after this change; callers must supply
  options.
- `DropGraphOperation` is not subject to Aspects validation. The Aspects engine
  (`AspectEnforcingTransactionalStore`) must explicitly skip or no-op on
  `DropGraphOperation` instances.
- Repository ADR-0002 (`IEntityStore.NamedGraph` computed from ambient) is unaffected:
  read operations continue to use the ambient fallback pattern. The explicit `branchIri`
  parameter applies only to `ExecuteTransactionAsync`.
