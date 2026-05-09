# 0006 — Guard `ICollectionLoader.LoadCollectionIrisAsync` to close deferred-load bypass

- **Status**: accepted
- **Date**: 2026-05-09
- **Author**: agent

## Context

`GuardedTransactionalStore` implements `ICollectionLoader` so that deferred
`EntityRefCollection<T>` loading (ADR-0009) passes through the authorization layer.
The method was wired as a direct expression-body delegation:

```csharp
IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
    string ownerIri, string predicate, CancellationToken ct)
    => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, ct);
```

`IAspectGuard.AuthorizeAsync` was never called before the inner store was contacted.
Any caller that obtained an `IEntityStore` or `ITransactionalEntityStore` reference
and then iterated a lazy `EntityRefCollection<T>` would load collection members
**without authorization**, bypassing the guard entirely. This is the same class of
vulnerability that `GuardedTransactionalStore.LoadAsync<T>` and
`QueryByTypeAsync<T>` were already protecting against.

## Decision

Rewrite `ICollectionLoader.LoadCollectionIrisAsync<T>` as an
`async IAsyncEnumerable<string>` that:

1. Resolves `AuthorizationContext.CurrentAgentToken` (or `""` when no scope is active).
2. Calls `_guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)` and
   `await`s it — throwing `UnauthorizedAccessException` (or any guard-specific
   exception) if denied.
3. Only then iterates `((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(...)` and
   `yield return`s each IRI.

The guard call matches the `Aspect.NoOpIri` token already used by `LoadAsync<T>` and
`QueryByTypeAsync<T>`, keeping authorization granularity consistent for read paths.
`[EnumeratorCancellation]` is applied to the `CancellationToken` parameter as required
by the compiler for `async IAsyncEnumerable` iterator methods.

## Consequences

- Deferred collection loading is fully covered by the authorization guard; no read
  path in `GuardedTransactionalStore` bypasses `IAspectGuard`.
- Applications using `AllowAllAspectGuard` (the default) are unaffected — the
  guard call is a no-op.
- The existing test `ICollectionLoader_LoadCollectionIrisAsync_delegates_without_guard`
  was updated to assert `Received(1)` instead of `DidNotReceive`, reflecting the
  corrected behaviour.
