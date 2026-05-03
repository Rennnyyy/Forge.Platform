# Architecture Decision Records — Forge.Aspects

Slice-local decisions for the Aspects validation library. Read after the [root ADRs](../../../adr/) and the [Entity slice ADRs](../../Entity/adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — Split-shape validation: Local pass + Context pass](0001-split-shape-validation.md)
- [0002 — No native GraphDB SHACL](0002-no-native-graphdb-shacl.md)
- [0003 — Caller-declared aspect per operation; no-op default](0003-caller-declared-aspect.md)
- [0004 — `IAspect` as a thin token in `Forge.Repository`](0004-iaspect-in-repository.md)
- [0005 — Context pass: aspects declare only the WHERE body](0005-context-where-body-only.md)
- [0006 — Rename `IAspect` → `IOperationAspect`](0006-rename-iaspect-to-ioperationaspect.md)
- [0007 — `IQueryAspect`: filter injection + result-graph SHACL for reads and queries](0007-iquery-aspect-model.md)
- [0008 — Rename `IShapeAspect` → `IWriteAspect`](0008-rename-ishapeaspect-to-ioperationaspect.md)
