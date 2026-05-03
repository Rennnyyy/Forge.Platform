# 0004 — `IAspectGuard` unifies authorization; supersedes `IOperationGuard`

- **Status**: accepted; supersedes [0001](0001-operation-guard-allow-all-default.md) and [0003](0003-guarded-transactional-store.md)
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0001 introduced `IOperationGuard` with two methods:

```csharp
ValueTask AuthorizeTransactionAsync(
    string agentToken,
    IReadOnlyList<TransactionOperation> operations,
    CancellationToken ct);

ValueTask AuthorizeQueryAsync(
    string agentToken,
    string aspectToken,
    CancellationToken ct);
```

`AuthorizeQueryAsync` is in fact the general primitive — "can agent X act under aspect
policy Y?" — and that same question is asked by the Capability dispatcher for every
message slot (command, response, event). Having two separate interfaces (`IOperationGuard`
in Validation, `ICapabilityAspectGuard` / `IAspectGuard` in Capability) for what is
structurally identical creates unnecessary duplication.

`AuthorizeTransactionAsync` is *not* a distinct authorization primitive. It is a
usability loop: "validate the whole batch before touching the store." That loop is an
invariant of the `GuardedTransactionalStore` decorator — it belongs there, not in the
auth interface.

## Decision

1. **`IAspectGuard`** — the single authorization primitive, lives in `Forge.Validation`:

   ```csharp
   public interface IAspectGuard
   {
       ValueTask AuthorizeAsync(
           string agentToken,
           string aspectToken,
           CancellationToken cancellationToken = default);
   }
   ```

2. **`IOperationGuard` is deleted.** No replacement needed.

3. **`GuardedTransactionalStore`** holds `IAspectGuard`. For `ExecuteTransactionAsync` it
   iterates over `operations` and calls `AuthorizeAsync(agentToken, op.Aspect.Name, ct)`
   for each operation before delegating to the inner store. The "validate ALL before
   executing ANY" invariant is preserved — iteration happens entirely before the inner
   store's `ExecuteTransactionAsync` is called. For read operations the call site is
   `AuthorizeAsync(agentToken, Aspect.NoOp.Name, ct)` — unchanged.

4. **`AllowAllAspectGuard`** (renamed from `AllowAllOperationGuard`) is the singleton
   no-op implementation of `IAspectGuard`.

5. **`Forge.Capability`** already depends on `Forge.Validation` and uses `IAspectGuard`
   in its dispatcher. Its local `IAspectGuard.cs` and `AllowAllAspectGuard.cs` are
   deleted; the types now come from `Forge.Validation`.

## Consequences

- One interface, one place. Any guard implementation satisfies both the store decorator
  and the capability dispatcher.
- The per-operation loop in `GuardedTransactionalStore` is more explicit than a single
  batch method; a guard that rejects one operation in a batch now rejects as soon as
  that operation is evaluated, before the inner store is ever called.
- `AllowAllAspectGuard.Instance` can be used anywhere a guard is needed.
