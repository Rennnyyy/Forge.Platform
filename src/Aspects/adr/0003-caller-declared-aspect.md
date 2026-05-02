# 0003 — Caller-declared aspect per operation; no-op default

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

The original trunk specification described an engine-driven discovery model: the engine
automatically resolves all aspects registered for a `(entityType, operationKind)` pair
and loops over them. The caller has no control over which aspects are applied to a given
operation — they are all applied or none are.

Problems with that model:

- **Opaque intent.** Reading a transaction body gives no indication of what validation
  policy is in effect for each operation. Intent is implicit, buried in registrations.
- **Fan-out complexity.** The engine must aggregate multiple aspects, define ordering,
  handle independent violations, and decide whether to short-circuit or accumulate.
- **Testing friction.** To test a single aspect, the test must ensure no other aspects
  are registered for the type, or mock the resolver.
- **No-validation paths are implicit.** An operation without any registered aspects
  silently bypasses validation. There is no way to tell at the call site whether
  "no validation" is intentional or a misconfiguration.

The desired semantics are simpler: each operation is aware of its own validation policy.
An operation that needs no validation says so explicitly.

## Options

1. **Caller-declared `IAspect` on each operation; `Aspect.NoOp` as the default.**
   `TransactionOperation` carries an `IAspect Aspect { get; init; }` property, defaulting
   to `Aspect.NoOp`. Callers write `tx.Create(x, myAspect)` or `tx.Create(x)` (no-op).
   The engine validates exactly the declared aspect, never more. Unregistered aspect →
   fail at commit time via `IAspectResolver`.
2. **Scoped aspect: set once on the transaction, applies to all operations.**
   `tx.WithAspect(a).Create(x).Update(y)`. Simpler for uniform transactions; awkward
   when operations within a transaction have different policies.
3. **Keep engine-driven discovery; add per-call opt-out.** `tx.Create(x).SkipAspects()`.
   Con: opts-out is the exceptional case syntactically; intent remains implicit for the
   common path; fan-out complexity unchanged.

## Decision

Option 1.

### API surface changes (in `Forge.Repository`)

```csharp
// TransactionOperation gains:
public IAspect Aspect { get; init; } = Aspect.NoOp;

// EntityTransaction gains parallel overloads:
public EntityTransaction Create<T>(T entity, IAspect aspect) where T : class, IEntity;
public EntityTransaction Update<T>(T entity, IAspect aspect) where T : class, IEntity;
public EntityTransaction Delete(string iri, IAspect aspect);
```

Parameterless overloads (`tx.Create(x)`) remain; they enqueue with `Aspect.NoOp`.

### No-op semantics

`Aspect.NoOp` is a named public singleton, not null. It is the default and the
explicit declaration that no validation policy applies. The engine fast-paths any
operation carrying `Aspect.NoOp` without calling the resolver or evaluating any shape.

There is no observable difference between omitting the aspect argument and passing
`Aspect.NoOp` explicitly. Both are intentional. `null` is never a valid aspect value;
constructors and setters that receive null throw `ArgumentNullException`.

### Unregistered aspect

If a caller passes an aspect that is not registered for the operation's `(entityType,
operationKind)` pair, the engine throws `AspectNotRegisteredException` when the
operation is processed during `CommitAsync`. This is fail-fast, not silent pass-through.
The exception carries the aspect name, the entity type, and the operation kind.

Validation of registration happens lazily at commit time (not at enqueue time) so that
`EntityTransaction` itself remains ignorant of the aspects registry.

## Consequences

- Each transaction call site is self-documenting: the aspect declared is the validation
  applied.
- The `IAspectResolver` contract changes from "return all active aspects" to "assert
  this aspect is valid for this type+kind."
- Trunk 5 (`WithAspect(…)` per-call shapes) will extend this model; the per-operation
  `IAspect` slot is the hook Trunk 5 will use.
- Engine fan-out loop is removed entirely. The pipeline (ADR-0001) operates on a single
  declared aspect per operation.
