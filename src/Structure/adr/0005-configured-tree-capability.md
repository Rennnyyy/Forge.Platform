# 0005 — ConfiguredTreeCapability: depth-first DAG traversal with variant filtering

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

ADR-0001 through ADR-0004 establish the Variant slice's data model (IStructure, Usage,
ConditionSet) and its ambient-scope filtering layer (VariantScope, VariantFilteringStore).
The final missing piece is the "configured tree" read: given a structure-head IRI and a
VariantConfiguration, produce a tree of the structure nodes that are reachable under that
configuration.

Three design decisions are required:

1. Where does the handler live?
2. How does the handler interact with VariantFilteringStore?
3. How are cycles handled in a DAG?

## Options

### 1 — Handler in Forge.Variant

`Forge.Variant` adds a dependency on `Forge.Capability` and ships
`GetConfiguredTreeHandler` as a reusable service.

Pro: the handler is part of the slice contract; any application gets it for free.
Con: `Forge.Variant` gains a Capability dependency; the response type is opaque to
callers who need to control their own HTTP surface.

### 2 — Handler in the consuming application

The `Forge.Variant` slice exposes the building blocks (IEntityStore + VariantScope);
the application wires them into a capability handler of its own choosing.

Pro: no structural coupling between Variant and Capability; applications keep full
control over command shape and response projection.
Con: each application re-implements the DFS traversal.

## Decision

Option 2.

The combination of VariantScope + VariantFilteringStore is sufficient scaffolding for
any application to build a configured-tree handler. The sample application in
`samples/Application.Sample/` provides a reference implementation
(`GetConfiguredTreeHandler`) that every adopter can copy and adapt.

### Handler contract (Application.Sample reference implementation)

```csharp
public sealed record GetConfiguredTreeCommand(
    string StructureHeadIri,
    string BranchIri,
    IReadOnlyDictionary<string, bool>?   FlagOptions        = null,
    IReadOnlyDictionary<string, string>? EnumerationOptions = null,
    DateTimeOffset?                      ReferenceDate      = null);

public sealed record StructureNodeDto(string Iri, IReadOnlyList<StructureNodeDto> Children);

public sealed record GetConfiguredTreeResponse(
    StructureNodeDto       Root,
    IReadOnlyList<string>  AllNodeIris);
```

`FlagOptions` and `EnumerationOptions` are intentionally flat dictionaries rather than
the closed `VariantValue` hierarchy. This avoids polymorphic JSON on the HTTP surface;
the handler reconstructs `VariantConfiguration` internally.

### Traversal algorithm

1. Build `VariantConfiguration` from the flat command fields.
2. Activate `VariantScope.Use(config)`; the ambient scope causes
   `VariantFilteringStore` to filter `QueryByTypeAsync<Usage>()` automatically.
3. Load all filtered usages as a `ILookup<parentIri, childIri>`.
4. Depth-first traversal from `StructureHeadIri`.
5. Cycle guard: maintain a `HashSet<string>` representing the **current DFS path**.
   When a node is encountered that is already in the current path, a cycle exists;
   return the node without further recursion (childless sentinel). Backtrack (remove
   the node from the path set) after returning from recursion so the same IRI may
   be visited again via a different DAG path (DAG sharing is normal).
6. Collect all unique IRIs into `AllNodeIris` during traversal for O(1) Bruno
   assertion support.

### Companion: RegisterUsageCapability

Because `Usage.Conditions` carries no `[Predicate]` annotation and is therefore not
persisted by the default RDF mapper (see ADR-0001), creating condition-bearing usages
from HTTP clients requires a capability-based creation path.

`RegisterUsageCapability` accepts flat condition spec DTOs, constructs the full
`ConditionSet`, and calls `IEntityStore.SaveAsync(usage)`. With the InMemory backend,
the stored object reference retains its `Conditions` in memory, which is sufficient for
demonstration purposes.

RDF persistence of `ConditionSet` is deferred to a future ADR; this limit is
documented in ADR-0001.

### Integration into Application.Sample

- `ProductNode` entity: `[Entity(Path = "product-nodes")]`, `[OperationEndpoints]`,
  implements `IStructure` — provides the structural nodes for the demo tree.
- `AddForgeVariant()` called after `AddForgeAspects()` (and all other store decorators)
  so that the VariantFilteringStore is the outermost unkeyed `IEntityStore`.
- Bruno chapter 21 (`21-variant-tree/`) provides a 28-call integration narrative
  covering node creation, usage registration with all three condition types, tree
  queries under six different configurations, and ProductNode CRUD.

## Consequences

- `Forge.Variant` has no dependency on `Forge.Capability`; the slice boundary is clean.
- Applications that build a configured-tree capability must implement the DFS traversal
  themselves, but the reference implementation in `Application.Sample` is copy-paste ready.
- Once `ConditionSet` RDF persistence is implemented (future ADR), the
  `RegisterUsageCapability` pattern becomes unnecessary; standard CRUD will suffice.
