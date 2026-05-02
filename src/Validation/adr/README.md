# Architecture Decision Records — Forge.Validation

Slice-local decisions for the Validation authorization library. Read after the
[root ADRs](../../../adr/) and the [Repository slice ADRs](../../Repository/adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — `IOperationGuard`: allow-all-by-default authorization contract for transactions and queries](0001-operation-guard-allow-all-default.md)
- [0002 — `ValidationContext`: ambient agent-token binding via AsyncLocal](0002-validation-context-async-local.md)
- [0003 — `GuardedTransactionalStore`: decorator that enforces pre-commit validation of all operations](0003-guarded-transactional-store.md)
