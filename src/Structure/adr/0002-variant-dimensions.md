# 0002 — Variant dimensions: Milestone and Option conditions

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

A `Usage` edge between two Structure nodes is not always unconditionally active. Two
orthogonal kinds of applicability conditions must be supported:

1. **Milestone** — a time-window: the Usage is active only when the evaluation date
   falls within a `[ValidFrom, ValidTo]` interval.
2. **Option** — a user-specified selection: the Usage is active when the caller's
   configuration carries a matching value for a named dimension. Options are either
   boolean flags (`yes` / `no`) or enumeration values (one of a fixed set of IRIs).

Both condition kinds must be evaluable in C# against a `VariantConfiguration` record
without requiring SPARQL or graph queries. Multiple conditions on a single `Usage` are
evaluated with logical AND.

## Options

### For Milestone
1. **`DateTimeOffset` window (v1).** `MilestoneCondition` stores `ValidFrom: DateTimeOffset?`
   and `ValidTo: DateTimeOffset?`. `VariantConfiguration.ReferenceDate: DateTimeOffset?`
   provides the evaluation instant (null = `DateTimeOffset.UtcNow`).
   Pro: self-contained; no dependency on `Forge.Branch`. Con: does not model
   milestone in terms of snapshot lineage progression.
2. **Snapshot lineage window (v2 target).** Conditions reference `ValidFromSnapshotIri`
   and `ValidToSnapshotIri`; satisfaction is determined by walking `Branch.DerivedFrom`
   lineage. Pro: tightly coupled to versioned history. Con: requires `Forge.Branch`
   dependency and a lineage-resolver service; higher complexity for v1.

### For Options
1. **Polymorphic `VariantValue` hierarchy.** Define an abstract `VariantValue` with two
   sealed subtypes: `FlagVariantValue(bool)` and `EnumerationVariantValue(string valueIri)`.
   Store options in `VariantConfiguration` as `IReadOnlyDictionary<string, VariantValue>`.
   Condition types (`FlagOptionCondition`, `EnumerationOptionCondition`) pattern-match on
   the concrete value type.
   Pro: type-safe, extensible, no magic strings for value kinds.
2. **Untyped string dict.** Store all option values as strings; callers use a convention
   for boolean values vs. IRI values.
   Con: no compile-time type check; invites bugs at call sites.

## Decision

**Milestone v1**: `DateTimeOffset` window (Option 1). The v2 snapshot-lineage path is
noted in Consequences but is not implemented here. A future ADR in this folder will
supersede this section when the lineage path is pursued.

**Options**: Polymorphic `VariantValue` hierarchy (Option 1).

### Condition interface

```csharp
public interface IVariantCondition
{
    bool IsSatisfiedBy(VariantConfiguration config);
}
```

### `MilestoneCondition`

- `ValidFrom: DateTimeOffset?` — null = open start (always valid from the beginning).
- `ValidTo: DateTimeOffset?` — null = open end (always valid until the end).
- Evaluation: uses `config.ReferenceDate ?? DateTimeOffset.UtcNow` as the reference instant.

### `FlagOptionCondition`

- `DimensionIri: string` — IRI identifying the boolean option axis.
- `ExpectedValue: bool` — the value the flag must be set to.
- `IsRequired: bool` — when `true`, the condition fails if the dimension is absent from the
  configuration; when `false` (default), an absent dimension means "don't care" (satisfied).

### `EnumerationOptionCondition`

- `DimensionIri: string` — IRI identifying the enumeration option axis.
- `EnumerationValueIri: string` — the specific enumeration value (IRI) that must be selected.
- `IsRequired: bool` — same semantics as `FlagOptionCondition.IsRequired`.

### `ConditionSet`

A value object wrapping `IReadOnlyList<IVariantCondition>`. Evaluation is always AND
across all entries: `All(c => c.IsSatisfiedBy(config))`. An empty `ConditionSet` is
always satisfied (vacuous truth). OR semantics across Usages are expressed by creating
multiple parallel `Usage` entities between the same parent and child (see ADR-0001).

### `VariantValue` hierarchy

```
VariantValue (abstract, sealed hierarchy)
├── FlagVariantValue(bool Value)
└── EnumerationVariantValue(string ValueIri)
```

## Consequences

- `MilestoneCondition` has no dependency on `Forge.Branch`; the snapshot-lineage path
  (v2) will require a new ADR and a `Forge.Branch` project reference.
- The open-world default (`IsRequired = false`) means callers who do not specify a
  dimension value do not accidentally exclude Usages that carry conditions on that
  dimension. Dimension owners who need a mandatory selection must set `IsRequired = true`.
- `ConditionSet.Empty` is always satisfied; Usages without conditions are unconditional
  (always included in any configured tree).
- The `VariantValue` type hierarchy is `private protected`-sealed; new value kinds
  require a new ADR.
