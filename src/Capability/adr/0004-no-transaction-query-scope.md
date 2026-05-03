# 0004 — No transaction / query scope in v1

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

During the Capability architecture design session, two additional enforcement ideas were
proposed:

1. **Transaction allowlist** — annotate a handler with the set of entity types /
   operation kinds (`Save`, `Delete`) it is permitted to execute. The dispatcher
   generates or selects an `IOperationGuard` that enforces this allowlist.
2. **Query allowlist** — annotate a handler with the entity types it is permitted to
   read. The dispatcher enforces this via a custom `IQueryAspect` or guard.

Both ideas are sound but deferred because they add significant scope that is not yet
grounded in concrete requirements:

- Any useful allowlist requires naming the entity types permitted — creating a tight
  compile-time coupling between the Capability and Entity slices that deserves its own ADR.
- The enforcement mechanism (generated `IOperationGuard` vs. ambient scope check vs.
  `GuardedTransactionalStore` wrapper) has multiple defensible implementations.
- Building it before we have a real handler to exercise it risks designing the wrong
  level of granularity.

## Decision

Transaction and query scope enforcement are **out of scope for v1** of `Forge.Capability`.

Capability handlers receive a standard `ITransactionalEntityStore` (or
`GuardedTransactionalStore`) and may perform any operation the existing guard allows.
No `[AllowsTransaction]`, `[AllowsQuery]`, or equivalent handlers are introduced.

## Consequences

- v1 scope is bounded: message aspect validation (commands, responses, events) is the
  only new enforcement concern.
- A future ADR in this folder (`0005+`) must be written before transaction/query scope
  is implemented. It must address: attribute syntax, enforcement point
  (dispatcher vs. store wrapper), and how dynamic allowlists compose with the existing
  `IOperationGuard`.
- The `GuardedTransactionalStore` from `Forge.Validation` remains the primary write-access
  control mechanism for capability handlers, as for all other callers.
