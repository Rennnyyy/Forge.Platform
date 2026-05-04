# 0007 — Map `MessageAspectViolationException` to 422 in `RegisterEndpoint`

- **Status**: accepted
- **Date**: 2026-05-05
- **Author**: agent

## Context

When a caller supplies a registered capability-aspect IRI and the command fails the SHACL
shape, `IMessageAspectEngine.ValidateAsync` (called inside `CapabilityDispatcher.DispatchAsync`)
throws `MessageAspectViolationException`. Before this ADR, that exception was unhandled at
the HTTP layer and surfaced to the caller as HTTP 500 — indistinguishable from a server
bug.

Sample ADR-0003 noted that "adding structured error mapping for SHACL violations is a
separate Capability.Http concern" — this ADR implements exactly that.

## Options

1. **Catch `MessageAspectViolationException` in `RegisterEndpoint<TCommand,TResponse>`**
   and return `422 Unprocessable Entity` with a `CapabilityError` payload:
   `code = "SHACL_VIOLATION"`, `message = exception.Message` (which already includes the
   aspect IRI and the first violation message). This is symmetric with the existing 422
   path for `CapabilityResult.Fail`.
2. **Global exception filter / middleware.** Con: exception middleware that catches
   strongly-typed exceptions from a specific layer is harder to scope correctly; it could
   swallow unrelated exceptions. The endpoint lambda is the exact scope to handle it.
3. **Return `CapabilityResult.Fail` from the dispatcher instead of throwing.** Con:
   changes the dispatcher contract (ADR-0006); a validation failure is structurally
   different from a handler-declared failure — the handler was never called.

## Decision

Option 1.

### Change

In `RegisterEndpoint<TCommand,TResponse>` (the Minimal API lambda), wrap
`dispatcher.DispatchAsync` in a try/catch:

```csharp
catch (MessageAspectViolationException ex)
{
    return Results.UnprocessableEntity(
        new CapabilityError("SHACL_VIOLATION", ex.Message));
}
```

### Error code

`"SHACL_VIOLATION"` is a platform-stable error code. Callers can inspect
`res.body.code` to distinguish an aspect constraint rejection from a handler-declared
business failure (`CapabilityResult.Fail`, any caller-defined code) and from an
unexpected server error (HTTP 500).

## Consequences

- SHACL violations are now indistinguishable from handler-declared failures at the HTTP
  level (both → 422 + `CapabilityError`), which is the correct semantic: both are
  "the request was well-formed but cannot be processed as submitted".
- `"SHACL_VIOLATION"` joins the platform's error-code vocabulary.
- The Bruno chapter 06 request `03-with-aspect-invalid.bru` can now assert
  `res.status == 422` and `res.body.code == "SHACL_VIOLATION"` instead of `500`.
- `Forge.Capability.Http` gains a compile-time dependency on
  `Forge.Aspects` (already transitive via `Forge.Capability`; no new project reference
  required) for the `MessageAspectViolationException` catch type.
