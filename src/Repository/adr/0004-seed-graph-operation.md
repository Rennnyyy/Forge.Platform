# 0004 — SeedGraphOperation: filtered triple copy within a transaction

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

Branch ADR-0002 and ADR-0003 require a mechanism to populate a newly created named graph
with a specific set of entity triples copied from a source graph, atomically within a
single transaction. Repository ADR-0003 introduced `DropGraphOperation` for the inverse
case (drop a graph). The same `TransactionOperation` hierarchy is the natural home for
the copy variant.

Two constraints carry forward from the design session:

1. **Atomicity**: entity creation and graph seeding must be one `CommitAsync()` call. A
   branch that exists in the management graph but has an unpopulated named graph is a
   corrupt intermediate state.
2. **Strict failure contract**: if any entity IRI in the requested set does not exist in
   the source graph, the entire transaction is aborted. Silent partial copies are never
   acceptable.

## Options

**Option A** — `SeedGraphOperation : TransactionOperation` with a source graph IRI,
target graph IRI, and an explicit entity IRI list. The backend executes a SPARQL UPDATE
that CONSTRUCTs the relevant triples from the source graph and INSERTs them into the
target graph within the active transaction.
Pro: fits the existing operation-dispatch model with no change to `ITransactionalEntityStore`;
atomic by construction; follows exact same pattern as `DropGraphOperation`.

**Option B** — A separate `ISeedingStore` interface with a dedicated `SeedAsync` method.
Con: a separate method call cannot participate in the same SPARQL UPDATE as `Create` and
`Delete` operations without additional coordination; forces a new interface member on
every backend; breaks atomicity unless callers carefully sequence calls.

## Decision

**Option A.**

### `SeedGraphOperation`

New `sealed` subtype of `TransactionOperation` in `Forge.Repository.Transaction`:

```csharp
/// <summary>
/// Copies the triples for a specific set of entity IRIs from <see cref="SourceGraphIri"/>
/// into <see cref="TargetGraphIri"/> as part of an atomic transaction.
/// The copy is point-in-time: source graph state is snapshotted at
/// <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/> time.
/// If any IRI in <see cref="EntityIris"/> does not exist in the source graph,
/// the entire transaction is aborted and a
/// <see cref="SeedOperationMissingEntityException"/> is thrown.
/// </summary>
public sealed class SeedGraphOperation : TransactionOperation
{
    public SeedGraphOperation(
        string sourceGraphIri,
        string targetGraphIri,
        IReadOnlyList<string> entityIris) { ... }

    public string SourceGraphIri { get; }
    public string TargetGraphIri { get; }
    public IReadOnlyList<string> EntityIris { get; }

    /// <summary>
    /// Returns <see cref="TargetGraphIri"/>; graph-level operations have no
    /// single entity IRI.
    /// </summary>
    public override string EntityIri => TargetGraphIri;
}
```

### `SeedOperationMissingEntityException`

New exception in `Forge.Repository.Transaction`. Carries the missing IRI list so callers
can surface a precise diagnostic. Always causes the transaction to abort.

### `EntityTransaction.SeedFrom`

New builder method on `EntityTransaction`, following the same fluent pattern as
`Create`, `Update`, `Delete`, and `DropGraph`:

```csharp
/// <summary>
/// Enqueues a <see cref="SeedGraphOperation"/> that copies triples for
/// <paramref name="entityIris"/> from <paramref name="sourceGraphIri"/> into
/// <paramref name="targetGraphIri"/> as part of this transaction.
/// See Repository ADR-0004.
/// </summary>
public EntityTransaction SeedFrom(
    string sourceGraphIri,
    string targetGraphIri,
    IReadOnlyList<string> entityIris)
```

### Backend responsibilities

- **`Forge.Repository.GraphDb`** — must implement `SeedGraphOperation` dispatch. Execute
  a SPARQL UPDATE using a `CONSTRUCT WHERE { GRAPH <source> { ?s ?p ?o . FILTER(?s IN (...)) } }`
  then `INSERT DATA INTO <target>`. Before committing, verify that every IRI in
  `EntityIris` produced at least one triple; if any IRI is absent throw
  `SeedOperationMissingEntityException` and roll back.
- **`Forge.Repository.InMemory`** — throws `NotSupportedException`. The in-memory backend
  is used for unit tests of slices that do not require graph seeding; tests requiring
  seeding use a GraphDb instance or a test double.

### Explicit IRI list only (V1 scope)

Query-driven seeding (seed by SPARQL pattern rather than an explicit IRI list) is
explicitly out of scope for this ADR. Draft a follow-up ADR before implementing.

## Consequences

- `ITransactionalEntityStore.ExecuteTransactionAsync` signature is unchanged. Dispatch
  of `SeedGraphOperation` happens through the existing operation hierarchy; backends that
  do not support it throw `NotSupportedException`.
- `SeedOperationMissingEntityException` is a new public type in `Forge.Repository.Transaction`.
- Backends are expected to execute the seed inside the same transaction scope as adjacent
  `Create`/`Update`/`Delete` operations, guaranteeing atomicity.
- `Forge.Repository.InMemory` remains usable for all other unit tests; only seeding-
  specific tests require a different backend or test double.
- Update `src/Repository/adr/SLICING.md` Transaction section to list
  `SeedGraphOperation.cs` and `SeedOperationMissingEntityException.cs`.
