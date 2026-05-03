# 0003 — `GuardedTransactionalStore`: decorator that enforces pre-commit validation of all operations

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0001 defines `IOperationGuard` and the "validate ALL before executing ANY" invariant for
transactions. The invariant needs an enforcement point in the execution path. It cannot live
in `EntityTransaction` (which is part of `Forge.Repository`) because Repository must not
depend on Validation. It cannot be a cross-cutting concern injected into the backend
implementations (InMemory, GraphDb) because those slices are also independent.

The existing precedent in the platform is the decorator pattern: both
`AspectEnforcingTransactionalStore` and `AspectEnforcingEntityStore` in `Forge.Aspects` wrap
a raw `IEntityStore` / `ITransactionalEntityStore` and add cross-cutting behavior without
modifying the inner store.

## Options

1. **`GuardedTransactionalStore : ITransactionalEntityStore` decorator in `Forge.Validation`.**
   Before delegating to the inner store's `ExecuteTransactionAsync`, calls
   `IOperationGuard.AuthorizeTransactionAsync` with the full operations list. If the guard
   throws, the inner store is never contacted — atomicity maintained trivially. All other
   `IEntityStore` / `ICollectionLoader` methods delegate transparently to the inner store.
   Read operations (`LoadAsync<T>`, `QueryByTypeAsync<T>`) call `AuthorizeQueryAsync` with
   `aspectToken = Aspect.NoOp.Name` ("noop") before delegating.
2. **Intercept at `EntityTransaction.CommitAsync`.** Requires Repository to depend on
   Validation — circular dep. Rejected.
3. **Intercept inside each backend (InMemory, GraphDb).** Spreads validation logic across
   backends; every new backend must re-implement the guard call. Rejected.

## Decision

Option 1.

### Call sequence for `ExecuteTransactionAsync`

1. Resolve `agentToken` = `ValidationContext.CurrentAgentToken ?? string.Empty`.
2. Call `_guard.AuthorizeTransactionAsync(agentToken, operations, ct)`.
   — If the guard throws, stop. The inner store is never called.
3. Delegate to `_inner.ExecuteTransactionAsync(operations, ct)`.

### Call sequence for read operations

For `LoadAsync<T>` and `QueryByTypeAsync<T>`:
1. Resolve `agentToken` = `ValidationContext.CurrentAgentToken ?? string.Empty`.
2. Call `_guard.AuthorizeQueryAsync(agentToken, Aspect.NoOp.Name, ct)`.
3. Delegate to `_inner`.

Individual-write methods (`SaveAsync`, `DeleteAsync`) delegate directly without a guard call.
They are low-level backend methods; the primary write API is `EntityTransaction` /
`ExecuteTransactionAsync`.

### DI wiring

`ServiceCollectionExtensions.AddForgeValidation(services, guard?)` decorates the registered
`ITransactionalEntityStore` (exactly as `AddForgeAspects` decorates `IEntityStore`).
When `guard` is omitted, `AllowAllOperationGuard.Instance` is used — making the registration
safe to include unconditionally.

## Consequences

- The "ALL ops validated before ANY executed" invariant is enforced at the decorator boundary.
- Swapping from allow-all to a real guard is a single DI call: `AddForgeValidation(myGuard)`.
- `GuardedTransactionalStore` does not depend on any SHACL / SPARQL infrastructure;
  the guard is a plain interface.
- ISparqlQueryStore is forwarded only if the inner store implements it (checked at construction).
