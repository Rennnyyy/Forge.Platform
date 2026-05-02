# Architecture Decision Records — Forge.Entity.Operations

Slice-local decisions for the Entity.Operations library. Read after the [root ADRs](../../../adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — Active-record CRUD operations on generated entities via ambient IEntityStore](0001-entity-operations-active-record.md)
- [0002 — Second Roslyn generator for entity operations rather than extending Entity.Generators](0002-operations-second-generator.md)
- [0003 — `EntityOperations.Query<T>()` exposes an EF-Core-shaped IQueryable surface](0003-iqueryable-entry-point.md)
- [0004 — `EntityOperations.BeginTransaction()` as the ambient transaction entry-point](0004-begin-transaction-ambient.md)
- [0005 — `[NoOperations]` opt-out from Operations.Generators](0005-no-operations-opt-out.md)
