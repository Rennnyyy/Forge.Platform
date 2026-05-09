# Architecture Decision Records — Forge.Authorization

Slice-local decisions for the Authorization library. Read after the
[root ADRs](../../../adr/) and the [Repository slice ADRs](../../Repository/adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — `IOperationGuard`: allow-all-by-default authorization contract for transactions and queries](0001-operation-guard-allow-all-default.md)
- [0002 — `ValidationContext`: ambient agent-token binding via AsyncLocal](0002-validation-context-async-local.md)
- [0003 — `GuardedTransactionalStore`: decorator that enforces pre-commit validation of all operations](0003-guarded-transactional-store.md)
- [0004 — `IAspectGuard` unifies authorization; supersedes `IOperationGuard`](0004-iaspect-guard-unifies-operation-guard.md)
- [0005 — Register `IAspectGuard` in the DI container](0005-iaspect-guard-registered-in-di.md)
- [0006 — Guard `ICollectionLoader.LoadCollectionIrisAsync` to close deferred-load bypass](0006-guard-collection-loader.md)
