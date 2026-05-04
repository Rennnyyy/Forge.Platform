# 0014 — CRUD Create handler returns `ALREADY_EXISTS` capability error on duplicate IRI

- **Status**: accepted
- **Date**: 2026-05-05
- **Author**: agent

## Context

ADR-0012 introduced the CRUD capability generator. The generated handlers for `Read`,
`Update`, and `Delete` already return `CapabilityResult.Fail(new CapabilityError("NOT_FOUND", …))`
when the entity is absent — consistent with ADR-0005's discriminated-union result type.

The generated `Create` handler has no analogous failure path. Both the in-memory store
(`InMemoryEntityStore`) and the GraphDB store (`GraphDbEntityStore`, `GraphDbTransactionalStore`)
throw `InvalidOperationException` when `WriteMode.Create` is attempted on an IRI that
already exists. Without a catch, this exception propagates out of the handler unhandled,
breaking the ADR-0005 invariant that **structured errors are returned, not thrown**.

## Options

1. **`try/catch (InvalidOperationException)` in the generated Create handler body.**
   The `caugh` exception message is forwarded to `CapabilityError.Message`;
   error code is `"ALREADY_EXISTS"`. No new exception type is needed.
   This mirrors the `NOT_FOUND` guard pattern for the other three operations.
2. **Introduce a dedicated `EntityAlreadyExistsException` in `Forge.Repository`.**
   Allows a targeted catch that cannot mis-fire for other `InvalidOperationException`
   sources. Con: requires a new public exception type, a new Repository ADR, updates to
   both backends, and cross-slice changes — out of proportion for this task.
3. **Leave Create unguarded; callers catch the exception at the dispatcher.**
   Con: violates ADR-0005's intent; forces all callers to handle both the discriminated
   union and unexpected exception paths.

## Decision

Option 1.

The generated Create handler body wraps `entity.CreateAsync(cancellationToken)` in a
`try/catch (global::System.InvalidOperationException)`. On catch, it returns:

```csharp
new global::Forge.Capability.CapabilityResult<Create{T}Response>.Fail(
    new global::Forge.Capability.CapabilityError("ALREADY_EXISTS", ex.Message));
```

The `InvalidOperationException` catch is consistent with `WriteMode.Create`'s documented
contract ("Throws if a triple set already exists for the entity's IRI") and both
backend implementations.

## Changes

| Action | File |
|--------|------|
| Modify | `src/Capability.Generators/CrudCapabilityEmitter.cs` — add try/catch in `EmitCreateTypes` |
| Modify | `tests/Capability.Generators.Tests/CrudCapabilityGeneratorTests.cs` — add test for `ALREADY_EXISTS` error |
| Add    | `src/Capability/adr/0014-crud-create-returns-already-exists-error.md` (this file) |

## Consequences

- The Create handler is now consistent with Read/Update/Delete: all four operations
  return a structured `CapabilityError` for their respective domain failure modes.
- Callers (HTTP, tests) can exhaustively pattern-match on `Ok`/`Fail` without also
  guarding against `InvalidOperationException` for the common duplicate-IRI case.
- Unexpected exceptions (network, serialisation, authorisation) still propagate — only
  the `InvalidOperationException` that matches the documented duplicate-IRI contract
  is converted to a capability error.
