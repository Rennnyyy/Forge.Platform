# 0001 — `IOperationGuard`: allow-all-by-default authorization contract for transactions and queries

- **Status**: superseded by [0004](0004-iaspect-guard-unifies-operation-guard.md)
- **Date**: 2026-05-03
- **Author**: agent

## Context

Every transaction and read/query operation in the platform needs a consistent authorization
hook. The hook must be:

1. **Opt-in for enforcement.** The default behavior is to allow everything. Additional
   authorization is an explicit configuration choice, not the fallback.
2. **Aware of two identifiers** — the *aspect token* (which validation policy applies to
   the operation) and the *agent token* (who is performing the operation).
3. **Transaction-scoped.** For transactions, all operations must be evaluated *before* any
   are applied to the store. Partial validation with partial execution is not acceptable.
4. **Slice-independent.** The Validation slice must not be a mandatory dependency of
   `Forge.Repository`, `Forge.Operations`, or any other slice. Consumers opt in by
   wiring the guarded decorator.

### Relationship to `IAspect`

`IAspect` (Repository slice) carries the *validation policy name* as a thin token
on each `TransactionOperation`.

> *Renamed from `IOperationAspect` to `IAspect` due to Aspects ADR-0009.* The `aspectToken` in `IOperationGuard.AuthorizeTransactionAsync`
is derived from `TransactionOperation.Aspect.Name` — the guard does not receive a separate
parameter; it inspects each operation directly. For queries, the `aspectToken` is passed
explicitly because queries currently carry no ambient aspect in the base store interfaces.

## Options

1. **Single `IOperationGuard` interface with two methods — `AuthorizeTransactionAsync` and
   `AuthorizeQueryAsync`; `AllowAllOperationGuard` as the default stub.**
   Transactions receive the entire `IReadOnlyList<TransactionOperation>` so the guard sees
   all aspect tokens at once. Queries receive an explicit `aspectToken` and `agentToken`.
   The default stub returns `ValueTask.CompletedTask` for both — no allocation, no throw.
2. **Per-operation `AuthorizeAsync(aspectToken, agentToken, operation)` method.**
   Simpler per-call surface; the framework loops over operations. Con: loses the
   "validate ALL before applying ANY" semantic at the interface level — callers could
   interleave validation and execution.
3. **Middleware / policy pipeline outside the repository layer.**
   Con: no integration with `ITransactionalEntityStore`; no cross-backend parity;
   loses the batch-validation contract.

## Decision

Option 1.

```csharp
public interface IOperationGuard
{
    ValueTask AuthorizeTransactionAsync(
        string agentToken,
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default);

    ValueTask AuthorizeQueryAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default);
}
```

`AllowAllOperationGuard` is a sealed singleton (`AllowAllOperationGuard.Instance`) that
returns `default` (completed `ValueTask`) for both methods without examining either token.

- `null` is never a valid `agentToken` or `aspectToken`; the decorator and context helpers
  enforce this at the call boundary.
- The guard does **not** throw a specific exception type; implementations choose their own
  denial exception. Callers should expect any exception as a denial signal.

## Consequences

- The "validate ALL before executing ANY" invariant is encoded in the interface signature
  (the whole `IReadOnlyList<TransactionOperation>` is passed at once).
- Swapping from allow-all to a real guard is a single DI registration change.
- Per-operation aspect tokens are accessible via `operation.Aspect.Name` inside the
  `AuthorizeTransactionAsync` implementation.
- Query authorization is a first-class concern, not an afterthought.

> *`IOperationGuard` renamed to `IAspectGuard` and the two-method batch contract replaced by a single `AuthorizeAsync(agentToken, aspectToken)` per-operation method due to Authorization ADR-0004.*

> *`AllowAllOperationGuard` renamed to `AllowAllAspectGuard` due to Authorization ADR-0004.*

> *`Forge.Validation` namespace/slice renamed to `Forge.Authorization` due to Authorization ADR-0004.*
