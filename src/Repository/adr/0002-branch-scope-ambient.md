# 0002 — BranchScope: per-operation branch IRI via ambient context

- **Status**: accepted
- **Date**: 2026-05-10
- **Author**: agent

## Context

All reads and writes in `IEntityStore` target a single RDF named graph. Today that graph
is fixed at store construction time via `EntityRepositoryOptions.NamedGraph`. Supporting
multiple concurrent branches requires each operation — load, query, save, delete — to
target a different named graph depending on which branch the caller is working in.

Two constraints shape the solution:

1. `IEntityStore` methods carry no branch parameter. Adding one to every method is a
   massive breaking change with no benefit at the contract level; the branch is a
   cross-cutting concern, not a per-call argument.
2. Transactions must be single-branch (decided in the brainstorm). The branch a
   transaction was opened against must be immutable after construction; it cannot follow
   any ambient drift that might occur mid-transaction.

The same cross-cutting, ambient-propagation pattern is already used in this codebase:
`AuthorizationContext` (`Forge.Authorization`) propagates an agent token;
`ExecutionScope` (`Forge.Execution`) propagates correlation IDs. The branch IRI follows
the same model.

## Options

1. **`AsyncLocal<string?>` ambient in `Forge.Repository` (`BranchScope`).** `IEntityStore.NamedGraph`
   becomes a computed property: reads `BranchScope.Current` and falls back to a configured
   default branch IRI when the ambient is null. No changes to `IEntityStore` method signatures.

2. **Pass `branchIri` as a parameter on every `IEntityStore` method.**
   Pro: explicit. Con: breaks every caller; forces every intermediate layer (Capability,
   Operations) to thread a value they have no business knowing about, violating the
   principle already established for aspect IRIs and agent tokens.

3. **Keyed DI registrations: one `IEntityStore` instance per branch.**
   Pro: no ambient. Con: branches are dynamic (created and deleted at runtime); keyed DI
   registrations are static (wired at startup). Does not compose with runtime branch creation.

## Decision

Option 1.

### `BranchScope`

A new `static class BranchScope` is added to `Forge.Repository` (root namespace):

```csharp
public static class BranchScope
{
    private static readonly AsyncLocal<string?> _branchIri = new();

    /// <summary>
    /// The branch IRI bound to the current async control flow, or <see langword="null"/>
    /// when no scope has been opened. Consumers fall back to
    /// <see cref="EntityRepositoryOptions.DefaultBranchIri"/> when this is null.
    /// </summary>
    public static string? Current => _branchIri.Value;

    /// <summary>
    /// Opens an ambient scope that routes all store operations in the current async
    /// control flow to the named graph identified by <paramref name="branchIri"/>.
    /// Dispose the returned handle to restore the previous branch.
    /// </summary>
    public static IDisposable Use(string branchIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchIri);
        var previous = _branchIri.Value;
        _branchIri.Value = branchIri;
        return new BranchIriScope(previous);
    }

    private sealed class BranchIriScope(string? previous) : IDisposable
    {
        public void Dispose() => _branchIri.Value = previous;
    }
}
```

Design notes:
- Dispose restores the **previous** value (not null), so nested scopes compose correctly.
  This matches `AuthorizationContext` and differs from `ExecutionScope` (which sets null).
- `Use` validates non-null, non-whitespace. Full IRI validation is the caller's
  responsibility (e.g. HTTP middleware returns `400` on a malformed header value).
- A null ambient is not an error condition. Consumers fall back to the configured default.

### `EntityRepositoryOptions.DefaultBranchIri`

`EntityRepositoryOptions` gains one new property:

```csharp
/// <summary>
/// The named-graph IRI used when no <see cref="BranchScope"/> is active.
/// Consumers of the platform configure this to the IRI of their "main" branch.
/// Required; must be a non-empty IRI string.
/// </summary>
public string DefaultBranchIri { get; set; } = string.Empty;
```

The existing `NamedGraph` property on `EntityRepositoryOptions` is **superseded** by
`DefaultBranchIri` for branch-aware stores. `NamedGraph` is retained without removal
for stores that are intentionally fixed to one graph (e.g. the management graph store
used by `Forge.Branch`); its semantics are unchanged for that use case.

### `IEntityStore.NamedGraph` — computed contract

`IEntityStore.NamedGraph` (currently a constructor-assigned string) becomes a computed
property on all branch-aware store implementations:

```csharp
// Implementation pattern (not part of the interface signature):
public string? NamedGraph => BranchScope.Current ?? _options.DefaultBranchIri;
```

The property stays on the `IEntityStore` interface as a `string?` get-only property.
Its documented contract changes from "the graph this store was constructed with" to
"the effective named graph for the next operation in the current async flow".

### Fallback rule

| `BranchScope.Current` | `DefaultBranchIri` | Effective graph |
|----------------------|--------------------|-----------------|
| non-null             | any                | `BranchScope.Current` |
| null                 | non-empty          | `DefaultBranchIri` |
| null                 | empty / not set    | implementation detail — individual stores may use the RDF default graph or throw; this is a misconfiguration |

Callers must not depend on the "null + empty" row in production. It is a misconfiguration
that will be caught by startup validation (future ADR).

## Consequences

- `IEntityStore` method signatures are unchanged; no caller is broken.
- All store implementations (`GraphDbEntityStore`, `InMemoryEntityStore`) must switch
  `NamedGraph` from a constructor field to a computed ambient read.
- `AuthorizationContext`, `ExecutionScope`, and `BranchScope` form a consistent family
  of ambient propagation types in the platform.
- `EntityTransaction` will snapshot `BranchScope.Current ?? DefaultBranchIri` at
  construction time to enforce the single-branch invariant. The transaction's branch is
  immutable after construction. See Repository ADR-0003.
- HTTP middleware (`Forge.Execution.Http`) will set the scope from the
  `X-Forge-BranchIri` request header before the request handler runs. See
  Execution.Http ADR for the branch carrier.
- `Forge.Branch` will configure a dedicated management-graph store by registering a
  keyed `IEntityStore` with `NamedGraph` pinned to `BranchOptions.ManagementGraphIri`
  (bypassing `BranchScope` entirely). That store remains branch-scope-unaware by design.
