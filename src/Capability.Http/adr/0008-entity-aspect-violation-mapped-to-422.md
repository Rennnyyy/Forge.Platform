# 0008 — Map `AspectViolationException` to 422 in `RegisterEndpoint`

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

ADR-0007 maps `MessageAspectViolationException` (thrown by the message-aspect engine
during capability dispatch) to HTTP 422 with error code `"SHACL_VIOLATION"`.

Capability ADR-0015 makes generated CUD handlers route all writes through
`EntityTransaction`. When the caller supplies a `CapabilityAspect` with
`OperationAspectIri` set, `AspectEnforcingTransactionalStore.ExecuteTransactionAsync`
may throw `AspectViolationException` (from `Forge.Aspects`) if the entity's RDF graph
fails the Local SHACL pass or the Context SPARQL pass.

`AspectViolationException` is structurally separate from `MessageAspectViolationException`
(different base class, different namespace, different data payload). Without explicit
handling it propagates past `DispatchAsync` and reaches the minimal-API lambda as an
unhandled exception, resulting in HTTP 500.

## Options

1. **Catch `AspectViolationException` in `RegisterEndpoint<TCommand,TResponse>` and
   return `422 Unprocessable Entity` with a `CapabilityError` payload,
   `code = "ENTITY_SHACL_VIOLATION"`.** The distinct code distinguishes entity-graph
   constraint failures (`IOperationAspect`) from message-schema failures (`IMessageAspect`).
   This is the exact same pattern as ADR-0007.

2. **Use code `"SHACL_VIOLATION"` for both.** Simpler — one code for all SHACL failures.
   Con: callers lose the ability to distinguish the validation layer (message vs entity).

3. **Let it surface as HTTP 500.** Con: indistinguishable from a server bug; breaks the
   contract that validation rejections are 422.

## Decision

Option 1.

### Change

In `RegisterEndpoint<TCommand,TResponse>`, extend the try/catch block:

```csharp
catch (AspectViolationException ex)
{
    return Results.UnprocessableEntity(
        new CapabilityError("ENTITY_SHACL_VIOLATION", ex.Message));
}
```

Added after (or before) the existing `MessageAspectViolationException` catch. Both
blocks return HTTP 422 with a `CapabilityError` body; the `code` field distinguishes them.

### Error code semantics

| Code | Source | Meaning |
|------|--------|---------|
| `SHACL_VIOLATION` | `MessageAspectViolationException` | Command/response/event JSON graph fails shape constraint |
| `ENTITY_SHACL_VIOLATION` | `AspectViolationException` | Entity RDF graph fails Local SHACL or Context SPARQL constraint |

## Consequences

- Entity-graph constraint violations are now observable as structured 422 responses,
  consistent with message-layer violations.
- Callers can branch on `res.body.code` to tell apart the two validation layers.
- `Forge.Aspects` namespace must be added to the `using` directives of
  `EndpointRouteBuilderExtensions.cs`.
