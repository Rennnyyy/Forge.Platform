# 0001 — Structure marker interface and Usage entity

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

Product and configuration management systems require a directed graph where one entity
references another with applicability conditions attached to the edge (a
"Usage"). The existing `[Owning]` / `[Inverse]` attributes in `Forge.Entity` model
simple unconditional references; they have no facility for a condition payload on the
edge itself and do not support the concept of a DAG node that can be shared across
multiple parent contexts.

The platform needs a first-class representation of:

1. **Structure** — any entity that acts as a node in a variant tree.
2. **Usage** — a first-class RDF entity representing a directed, condition-bearing edge
   from one Structure node to another.

## Options

1. **`IStructure` marker interface + `Usage` entity.**
   `IStructure` is a pure C# marker with no members. Any entity class can implement it
   to signal that it participates as a structure node. `Usage` is a `[Entity]`-annotated
   partial class in the new `Forge.Variant` slice. It stores `ParentStructureIri` and
   `ChildStructureIri` as plain string predicates. Conditions are attached as a
   `ConditionSet` property.

2. **`[Structure]` attribute (generator-driven).**
   Introduce a new Roslyn source-generator attribute analogous to `[Entity]` for
   structure nodes. Con: requires changes to `Entity.Generators`, significantly widens
   scope; offers no behavioral benefit over a marker interface for v1.

3. **Extend `[Owning]` with a `Conditions` property.**
   Con: conflates unconditional references with conditional ones; adds conditions to
   every entity in the platform; not backward compatible.

## Decision

Option 1.

### `IStructure`

```csharp
public interface IStructure { }
```

A pure C# marker interface with no members. It signals intent to tools, documentation,
and future validation that a type participates as a structure node. It does **not**
constrain what types `Usage.ParentStructureIri` / `Usage.ChildStructureIri` point to —
those are plain string IRI fields to allow cross-type structures without generics.

### `Usage`

- `[Entity(Path = "usages", PredicatePath = "usage")]`
- `[Identity(IdentityGenerator.Random)]` — random GUIDv4 IRI.
- Scalar predicates: `ParentStructureIri` (`string`), `ChildStructureIri` (`string`).
- `Conditions` property of type `ConditionSet` — **not** annotated with `[Predicate]`.
  The default reflection mapper ignores it; RDF persistence of conditions requires a
  custom mapper (deferred to a follow-up task in `Forge.Variant.Repository`).

### Identity choice

`IdentityGenerator.Random` is chosen instead of `PropertyBasedEncoded` because multiple
`Usage` entities between the same parent and child are valid and intentional: they express
OR semantics — a child Structure is included in the configured tree if **any** of its
Usage edges to the parent is satisfied. Deterministic identity from `(parent, child)`
alone would cause IRI collisions when two parallel Usages coexist.

### DAG support

A child `Structure` may appear under more than one parent, forming a directed acyclic
graph. Cycle detection is the responsibility of the tree traversal layer (see
`Forge.Variant` configured-tree capability, future ADR).

## Consequences

- Any entity can act as a structure node by implementing `IStructure`; no generator
  changes are required.
- `Usage` entity participates in all existing repository, event, and aspect machinery
  without special-casing.
- RDF persistence of `ConditionSet` requires a custom `IRdfMapper<Usage>` to be
  provided before structure trees can be stored and queried in a repository.
- The `ParentStructureIri` / `ChildStructureIri` predicates are plain strings; the
  caller is responsible for passing valid IRIs of existing structure nodes.
