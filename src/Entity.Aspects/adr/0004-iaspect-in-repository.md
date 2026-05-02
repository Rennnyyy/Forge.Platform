# 0004 — `IAspect` as a thin token in `Forge.Entity.Repository`

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

ADR-0003 places `IAspect Aspect { get; init; }` on `TransactionOperation` and adds
`IAspect`-bearing overloads to `EntityTransaction`. Both types live in
`Forge.Entity.Repository`.

`Forge.Entity.Aspects` references `Forge.Entity.Repository` (it needs `TransactionOperation`,
`ITransactionalEntityStore`, `IRdfMapper`, etc.). If `IAspect` with its full SHACL/SPARQL
data surface (`LocalShapeTtl`, `ContextSparql`, …) were defined in `Forge.Entity.Aspects`,
making `TransactionOperation` carry it would require `Forge.Entity.Repository` to reference
`Forge.Entity.Aspects` — a circular dependency.

## Options

1. **Thin `IAspect` identity token in `Forge.Entity.Repository`; `IShapeAspect : IAspect`
   with shape data in `Forge.Entity.Aspects`.** Repository stays clean (no SHACL
   knowledge); Aspects adds the concrete contract. `Aspect.NoOp` — the sentinel — also
   lives in Repository. Engine casts `IAspect` to `IShapeAspect` to get shape data;
   the NoOp fast-path is a type check, not a name comparison.
2. **New `Forge.Entity.Aspects.Abstractions` project** referenced by both Repository and
   Aspects. Con: an extra project for a two-method interface is over-engineering;
   introduces a third dependency axis for consumers.
3. **Opaque `string AspectName` on `TransactionOperation`; Aspects resolves by name
   internally.** Con: loses static typing; callers pass magic strings; no `Aspect.NoOp`
   compile-time constant.
4. **Move all of `EntityTransaction` / `TransactionOperation` into Aspects.**
   Con: Aspects becomes a mandatory dependency of every application that uses transactions,
   even those that never use validation. Breaks the optional-slice model.

## Decision

Option 1.

### What lives in `Forge.Entity.Repository`

```csharp
/// <summary>
/// Marker for a named validation policy attached to a <see cref="TransactionOperation"/>.
/// Use <see cref="Aspect.NoOp"/> to declare that no validation applies.
/// </summary>
public interface IAspect
{
    string Name { get; }
}

/// <summary>Well-known aspect singletons.</summary>
public static class Aspect
{
    /// <summary>
    /// The no-operation aspect. When an operation carries this aspect the engine skips
    /// all validation and applies the operation directly. This is the default.
    /// </summary>
    public static readonly IAspect NoOp = NoOpAspect.Instance;

    private sealed class NoOpAspect : IAspect
    {
        public static readonly NoOpAspect Instance = new();
        public string Name => "noop";
        private NoOpAspect() { }
    }
}
```

### What lives in `Forge.Entity.Aspects`

```csharp
/// <summary>
/// An aspect that carries SHACL shape data. The engine casts <see cref="IAspect"/> to
/// this interface to obtain shape material; the cast succeeds for every non-NoOp aspect
/// in Trunk 2.
/// </summary>
public interface IShapeAspect : IAspect
{
    /// <summary>Turtle-serialized SHACL Local shape, or null if this aspect has no local pass.</summary>
    string? LocalShapeTtl { get; }

    /// <summary>SPARQL SELECT body for the Context pass, or null if this aspect has no context pass.</summary>
    string? ContextSparql { get; }
}
```

### Engine fast-path

```
if (operation.Aspect is NoOpAspect)   // internal type check — not string comparison
    goto apply;
```

This is safe because `NoOpAspect` is `sealed internal` and the only instance is
`Aspect.NoOp`. No lock, no dictionary lookup.

## Consequences

- `Forge.Entity.Repository` gains two small public types (`IAspect`, `Aspect`) with zero
  SHACL or dotNetRDF dependencies. Its NuGet closure is unchanged.
- `Forge.Entity.Aspects` remains the only assembly that knows about SHACL/SPARQL shapes.
- Future aspect types (e.g. Trunk 5 inline shapes) extend `IShapeAspect` or introduce
  their own `IAspect` sub-interface without touching Repository.
- The no-op fast-path is a sealed-type comparison — branch-predictor friendly, zero
  allocation.
