# 0003 — Capability aspect IRI from X-Forge-Capability-AspectIri header

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

`ICapabilityDispatcher.DispatchAsync` accepts a `string? capabilityAspectIri` parameter
(Capability ADR-0007 + existing implementation). When dispatching from an HTTP endpoint,
the caller must supply the IRI that selects the validation policy for the request. Without
a defined mechanism to convey this IRI over HTTP, all HTTP-dispatched capabilities execute
permissively (no SHACL validation).

The spec trunk-04 originally proposed a richer per-slot aspect model
(`ICapabilityAspectsProvider` returning `CapabilityAspects`). After review the simpler
model was preferred: a single `string? capabilityAspectIri` matches the existing dispatcher
signature directly and defers per-slot aspect decomposition to the aspect store inside
the dispatcher.

## Options

1. **`X-Forge-Capability-AspectIri` header → `CapabilityAspectIriProvider`** — the
   endpoint handler calls `ICapabilityAspectIriProvider.GetCapabilityAspectIriAsync(HttpContext)`
   which reads the header value. Absent or whitespace-only header → `null` (permissive).
2. URL path parameter (e.g. `?aspectIri=...`). Con: IRIs contain characters that require
   URL encoding; query-string pollution; changes the endpoint surface per call.
3. Hardcode the IRI on `[Capability]` attribute. Con: bakes a global policy into the
   attribute; defeats per-request policy selection.

## Decision

Option 1.

### Header

```
X-Forge-Capability-AspectIri: urn:forge:aspects:strict-v1
```

- Header name: `X-Forge-Capability-AspectIri` (follows the `X-Forge-` platform namespace).
- Value: the raw IRI string passed unchanged to `DispatchAsync` as `capabilityAspectIri`.
- Absent, empty, or whitespace-only header → `null` → fully permissive dispatch.

### `ICapabilityAspectIriProvider` contract

```csharp
public interface ICapabilityAspectIriProvider
{
    ValueTask<string?> GetCapabilityAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
```

The default implementation (`HeaderCapabilityAspectIriProvider`) is registered as a
singleton by `AddCapabilityHttp()` and is replaceable by any custom implementation.

## Consequences

- Callers control validation strength per request by setting a header — no code changes
  needed to switch policies.
- Absent header is silently permissive; applications that want fail-closed behaviour
  can replace `ICapabilityAspectIriProvider` with a custom implementation that throws or
  returns a default policy IRI.
- The `X-Forge-` header namespace is used consistently with the platform convention.
