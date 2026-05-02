# 0005 — Context pass: aspects declare only the WHERE body

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

The Context pass (ADR-0001) runs a SPARQL SELECT against the transaction-local store to
detect cross-entity constraint violations. Any row returned by the query is treated as a
violation; the engine reads `?focusNode`, `?message`, and `?path` from each row.

Originally `IShapeAspect.ContextSparql` expected a **full SPARQL SELECT statement**:

```sparql
SELECT ?focusNode ?message WHERE {
  BIND (<urn:focus:test> AS ?focusNode)
  BIND ("Constraint violated." AS ?message)
}
```

This has two problems:

1. **Projection is an implementation detail of the engine**, not a concern of the aspect
   author. The bindings `?focusNode`, `?message`, and `?path` are fixed by the engine
   contract; an aspect cannot meaningfully project anything else.
2. **Boilerplate leaks into every aspect.** Every author must copy the same
   `SELECT ?focusNode ?message WHERE { ... }` wrapper, which adds noise and creates
   opportunity for subtle mistakes (e.g. wrong variable names, missing `WHERE`).

## Options

1. **Aspect declares only the WHERE body; engine owns the full SELECT.**
   `IShapeAspect.ContextWhere` is the content of the `WHERE { ... }` block.
   The engine assembles the full query: `SELECT ?focusNode ?message ?path WHERE { <body> }`.
2. **Keep the full SELECT.** Simpler engine, more flexible for aspects that want non-standard
   projections. Con: the flexibility is unused; the boilerplate cost is real.
3. **Introduce a typed constraint DSL** (method-based builder). More discoverable, fully
   type-safe. Con: significant scope increase; premature for a v1 slice.

## Decision

Option 1: aspects declare only the WHERE body.

- `IShapeAspect.ContextSparql` is **renamed** to `ContextWhere`.
- The property value is the raw content of the `WHERE { }` block — no `SELECT`, no `WHERE`
  keyword.
- `AspectEngine` wraps it before execution:
  ```csharp
  var fullQuery = $"SELECT ?focusNode ?message ?path WHERE {{ {aspect.ContextWhere} }}";
  ```
- `InlineTtlShapeAspect` renames its constructor parameter and property accordingly.

## Consequences

- Aspect authors write less boilerplate and cannot accidentally omit required bindings.
- The engine's output contract (`?focusNode`, `?message`, `?path`) is now explicit in the
  public interface documentation rather than implied by convention.
- Existing `ContextSparql` usages (tests, any downstream code) must be updated to supply
  only the WHERE body and use the new property name.
