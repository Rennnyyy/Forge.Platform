# 0001 — Branch entity and management graph

- **Status**: accepted
- **Date**: 2026-05-10
- **Author**: agent

## Context

Repository ADR-0002 establishes that each branch is an RDF named graph whose IRI is the
branch's identity IRI. Repository ADR-0003 establishes that deleting a branch cascades to
a `DropGraphOperation` inside the same transaction. Both ADRs defer to this document for:

1. The structure of the `Branch` entity type itself.
2. The management graph — the dedicated named graph that holds all `Branch` entity metadata.
3. How `Forge.Branch.Default` exposes the platform's configured default branch IRI.
4. The startup upsert of the default branch entity.
5. DI registration: how the management graph store is wired alongside the ordinary
   branch-scoped store.

### Key constraints brought forward from the brainstorm

- The branch entity's `IEntity.Iri` **is** the named graph IRI. No translation layer.
- Management graph IRI is **configurable** (not a fixed constant).
- Deleting a branch entity **atomically cascades** to dropping its named graph.
- Merge and fork semantics are **explicitly out of scope** for this ADR.

## Options

### Where does the `Branch` entity type live?

**Option A** — Define `Branch` inside `Forge.Repository`.
Pro: no new assembly. Con: `Forge.Repository` is already a foundational, framework-agnostic
layer. Adding a domain entity type to it conflates the persistence contract with a
domain concern; it would also mean `Forge.Repository` transitively exposes the
source-generated partial class (and its Roslyn generator dependency) to all consumers.

**Option B** — Define `Branch` in a new `Forge.Branch` slice.
Pro: clean separation; the domain entity, DI wiring, and `Forge.Branch.Default` constant
all have a single coherent home. Consumers that do not use branches do not pay for the
dependency. Con: one more project.

### How is the management graph store registered?

**Option 1** — Register a second `IEntityStore` keyed to a well-known key
(`"forge.branch.management"`), permanently wired to `BranchOptions.ManagementGraphIri`.
This store bypasses `BranchScope` entirely; `NamedGraph` returns the configured
management graph IRI unconditionally.

**Option 2** — Reuse the default `IEntityStore` with an explicit `BranchScope.Use(...)` 
call wrapping every branch-management operation.
Con: callers must remember to wrap; forgetting silently operates on whatever the ambient
branch happens to be, corrupting user data.

### How does the default branch IRI reach `BranchScope.Current == null` consumers?

**Option I** — Consumers read `BranchOptions.DefaultBranchIri` directly from DI.
Con: every store implementation must inject `BranchOptions`; leaks a domain concern into
the persistence layer.

**Option II** — Expose `Forge.Branch.Default` as a `static class` with a `BranchIri`
property populated at DI registration time. Stores read `BranchScope.Current ??
BranchOptions.DefaultBranchIri` (Repository ADR-0002); `Forge.Branch.Default.BranchIri`
is a convenience for callers above the repository layer.

## Decision

**Option B + Option 1 + Option II.**

### `BranchOptions`

Configuration class in `Forge.Branch`, bound from the `Forge:Branch` configuration section:

```csharp
public sealed class BranchOptions
{
    /// <summary>
    /// IRI of the named graph that holds all <see cref="Branch"/> entity metadata.
    /// All branch-management reads and writes target this graph exclusively.
    /// Default: <c>https://forge-it.net/management</c>.
    /// </summary>
    public string ManagementGraphIri { get; set; } = "https://forge-it.net/management";

    /// <summary>
    /// IRI of the default branch named graph. Used by <see cref="BranchScope"/>-unaware
    /// callers and by store implementations when no ambient scope is active.
    /// Default: <c>https://forge-it.net/branches/main</c>.
    /// </summary>
    public string DefaultBranchIri { get; set; } = "https://forge-it.net/branches/main";
}
```

Both defaults are rooted at the canonical base URL `https://forge-it.net` (root ADR-0006).

### `Forge.Branch.Default`

Static convenience class — populated once at DI registration time:

```csharp
/// <summary>
/// Exposes the platform's configured default branch IRI. Populated by
/// <c>AddForgeBranch()</c> at DI registration time.
/// </summary>
public static class BranchDefault
{
    /// <summary>
    /// The IRI of the default branch (i.e. <c>BranchOptions.DefaultBranchIri</c>).
    /// Guaranteed non-null after <c>AddForgeBranch()</c> has run.
    /// </summary>
    public static string BranchIri { get; internal set; } = string.Empty;
}
```

The class lives in `namespace Forge.Branch` per root ADR-0004. Consumers reference it as
`BranchDefault.BranchIri`. The identifier `BranchDefault` is used instead of
`Forge.Branch.Default` because `Default` is a reserved keyword in C# context expressions.

### `Branch` entity

```csharp
[Entity(Path = "branches", PredicatePath = "branch")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
public partial class Branch
{
    /// <summary>
    /// Human-readable name, e.g. "main" or "feature-X".
    /// Also the sole identity part — the IRI becomes
    /// <c>{BaseIri}/branches/{Name}</c>, which equals the named graph IRI.
    /// </summary>
    [IdentityPart(0)]
    [Predicate("name")]
    public partial string Name { get; init; }

    /// <summary>Optional human-readable description.</summary>
    [Predicate("description")]
    public string? Description { get; set; }

    /// <summary>Timestamp of branch creation. Set at creation time; never mutated.</summary>
    [Predicate("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
```

Design notes:
- `Name` is the sole `[IdentityPart]`. The generated IRI
  `{EntityOptions.BaseIri}/branches/{Name}` equals the named graph IRI (per brainstorm
  decision: entity IRI = named graph IRI with no translation).
- No `ParentBranch` ref: fork tracking is out of scope (brainstorm decision).
- `CreatedAt` is set by the caller at the moment of `EntityTransaction.Create<Branch>`.
  No platform-level automatic timestamping in this ADR.

### Management graph store: keyed DI registration

`AddForgeBranch()` registers a second `IEntityStore` (and `ITransactionalEntityStore`)
keyed as `"forge.branch.management"`. This store is constructed with
`NamedGraph = BranchOptions.ManagementGraphIri` hard-wired; it never consults
`BranchScope.Current`. It is resolved via `[FromKeyedServices("forge.branch.management")]`
inside `Forge.Branch` types only — it is never exposed as the default unkeyed `IEntityStore`.

The default unkeyed `IEntityStore` remains the branch-scoped store that reads
`BranchScope.Current ?? BranchOptions.DefaultBranchIri` (Repository ADR-0002).

### Default branch upsert at startup

`AddForgeBranch()` registers a hosted service (or `IHostedLifecycleService` for .NET 8+
API compatibility; `IHostedService` for .NET 10 simplicity) that runs on application start:

1. Resolves the management graph store.
2. Calls `IEntityRepository<Branch>.FindAsync(BranchDefault.BranchIri)`.
3. If null, issues `EntityTransaction.Create<Branch>(defaultBranch)` against the
   management graph store.

This is an upsert-on-startup pattern, not an error-on-missing pattern. Missing default
branch is a misconfiguration, not a runtime fault; the startup service surfaces it as a
fatal startup error if `Create` itself fails.

### Atomic cascade delete

To delete a branch, callers use the management graph `EntityTransaction`:

```csharp
// branchToDelete.Iri == the named graph IRI of the branch data
await using var tx = branchEntityOperations.BeginTransaction();
tx.Delete<Branch>(branchToDelete.Iri);         // remove the Branch entity
tx.DropGraph(branchToDelete.Iri);              // drop the branch data graph
await tx.CommitAsync();
```

`tx.DropGraph` enqueues a `DropGraphOperation` (Repository ADR-0003). For GraphDB, this
issues `DROP GRAPH <branchIri>` within the open transaction URL; for InMemory it clears
the graph partition. The atomicity guarantee is provided by the transaction, not by
`Forge.Branch`.

Callers must not delete the default branch or the management graph; `Forge.Branch` does
not enforce this with a guard in v1 — it is a documentation constraint.

## Consequences

- `Forge.Branch` depends on `Forge.Repository` (for `BranchScope`, `IEntityRepository<T>`,
  `ITransactionalEntityStore`) and `Forge.Entity` (for `EntityBase`, attributes).
- No existing slice gains a dependency on `Forge.Branch`. The dependency graph is strictly
  additive.
- `BranchDefault.BranchIri` is safe to read from any context after DI startup completes;
  it is an empty string before that point (a known, detectable misconfiguration state).
- `EntityRepositoryOptions.DefaultBranchIri` (Repository ADR-0002) and
  `BranchOptions.DefaultBranchIri` carry the same value. `AddForgeBranch()` is
  responsible for keeping them in sync at registration time — it copies
  `BranchOptions.DefaultBranchIri` into `EntityRepositoryOptions.DefaultBranchIri` so
  that store implementations (which depend only on `Forge.Repository`) can read the
  configured default without a `Forge.Branch` dependency.
- `BranchOptions.ManagementGraphIri` and `EntityRepositoryOptions.NamedGraph` (the
  existing fixed-graph property) are independent. The management graph store sets its
  own `NamedGraph` directly; `BranchOptions.ManagementGraphIri` is the source of truth.
- Merge and fork are out of scope. A follow-up ADR must be written before any such
  feature is implemented.
