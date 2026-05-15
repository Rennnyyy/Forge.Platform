# 0012 — Variant tree demonstration chapter (Bruno 21-variant-tree)

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

Variant ADR-0005 documents the `ConfiguredTreeCapability` pattern and states that the
reference implementation lives in the sample application. This ADR records all design
choices made while wiring that reference into `Application.Sample`.

## Decisions

### 1. Domain choice: vehicle configurator

A vehicle product tree is an immediately recognisable configurator scenario:
one vehicle root, two mutually exclusive powertrains (EV / ICE selectable by a required
flag dimension), two optional interiors (selectable by an optional enumeration dimension),
sub-components that are unconditionally attached to each powertrain, and a time-bounded
Race Edition package used to demonstrate MilestoneCondition.

This domain requires all three condition types and all three open-/closed-world semantics.

### 2. ProductNode entity

A new `ProductNode` entity (in the sample assembly, not in `Forge.Variant`) carries:
- `[Entity(Path = "product-nodes")]` + `[Identity(IdentityGenerator.Random)]`
- `[OperationEndpoints]` — standard REST CRUD via `MapOperations()`
- `IStructure` — marks it as a variant tree node
- Scalar properties: `Name` (required), `Description` (optional)

`Forge.Variant.IStructure` is a marker only; no interface members are required.

### 3. Two capability handlers in the sample

`RegisterUsageHandler` (`variant.usages.register`, POST):
- Accepts flat condition spec DTOs (`FlagConditionSpec`, `EnumerationConditionSpec`,
  `MilestoneConditionSpec`) to avoid polymorphic JSON on the HTTP surface.
- Constructs `ConditionSet` and saves the `Usage` entity directly.
- Works with the InMemory backend because the in-memory store retains object state
  (including the non-`[Predicate]`-annotated `Conditions` property).

`GetConfiguredTreeHandler` (`variant.configured-tree.get`, GET-over-POST):
- Accepts `StructureHeadIri`, `BranchIri`, flat option dictionaries, and optional
  `ReferenceDate`.
- Builds `VariantConfiguration`, activates `VariantScope.Use(config)`, loads all
  filtered `Usage` entities (VariantFilteringStore does the filtering), builds the
  depth-first DAG tree, returns `StructureNodeDto` tree + `AllNodeIris` flat list.

### 4. AddForgeVariant placement in Program.cs

Called immediately after the last store-wrapping registration
(`AddForgeEntityEvents` / `AddForgeAspects`), before the capability and operations
registrations. This ensures `VariantFilteringStore` is the outermost unkeyed
`IEntityStore`, giving the correct decorator chain:
`VariantFiltering → EventEmitting → AspectEnforcing → Backend`.

### 5. Bruno chapter 21 narrative (28 requests)

| Range | What it demonstrates |
|-------|----------------------|
| 01–10 | Create 10 `ProductNode` entities (vehicle configurator nodes) |
| 11–19 | Register 9 `Usage` edges with all three condition types |
| 20–24 | Query configured tree under 5 different `VariantConfiguration` snapshots |
| 25–27 | Standard `ProductNode` CRUD: list, read, update |
| 28    | Delete a leaf node (Race Edition cleanup) |

## Consequences

- `GetConfiguredTreeHandler` and `RegisterUsageHandler` are tied to the InMemory backend;
  they will not correctly persist conditions on the GraphDb backend until a future ADR
  addresses `ConditionSet` RDF serialization.
- The `ProductNode` entity is a sample-only concern and is not part of the Variant slice's
  public surface.
