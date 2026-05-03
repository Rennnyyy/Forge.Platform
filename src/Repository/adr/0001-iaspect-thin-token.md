# 0001 — `IAspect`: thin identity token for validation policies

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

`Forge.Repository` owns `TransactionOperation` and `EntityTransaction`. Both types need
to carry a *named validation policy* so that the `Forge.Aspects` engine can look up and
apply the correct SHACL/SPARQL shapes at transaction-execute time (Aspects ADR-0003).

The full shape data (`LocalShapeTtl`, `ContextWhere`) lives in `Forge.Aspects`, which
already references `Forge.Repository`. Defining the rich write-validation interface
(`IOperationAspect`) directly in `Forge.Repository` would require `Forge.Repository` to
reference `Forge.Aspects` — a circular dependency (Aspects ADR-0004).

The solution is a *thin token* interface in `Forge.Repository` that carries only the
policy name; `Forge.Aspects` extends it with shape data.

## Decision

Define `IAspect` in `Forge.Repository` as a minimal identity token:

```csharp
/// <summary>
/// Marker for a named validation policy attached to a <see cref="TransactionOperation"/>.
/// Use <see cref="Aspect.NoOp"/> to declare that no validation applies.
/// </summary>
public interface IAspect
{
    string Name { get; }
}
```

- `Aspect.NoOp` (type `IAspect`) is the no-operation sentinel. Engine fast-paths via
  reference equality (`ReferenceEquals`), never by name comparison.
- `TransactionOperation.Aspect` is typed `IAspect`; the engine casts to
  `IOperationAspect` (in `Forge.Aspects`) to obtain shape data for CUD operations.
- The name `IAspect` signals: "any named policy" — unscoped, maximally generic.
  Concrete scoped contracts (`IOperationAspect`, `IQueryAspect`, `IMessageAspect`) live
  in `Forge.Aspects` and extend `IAspect`.

## Consequences

- No circular dependency: `Forge.Repository` knows nothing about SHACL or SPARQL.
- Callers that only use `Aspect.NoOp` never need to reference `Forge.Aspects`.
- The optional-slice model is preserved: adding validation is an opt-in dependency on
  `Forge.Aspects`, not a mandatory one.

> *This ADR supersedes the naming context of Aspects ADR-0004 for the Repository slice.
> The original thin token was called `IAspect` (ADR-0004), renamed `IOperationAspect`
> (Aspects ADR-0006), then restored to `IAspect` (Aspects ADR-0009).*
