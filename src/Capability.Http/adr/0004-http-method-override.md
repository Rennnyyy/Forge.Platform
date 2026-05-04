# 0004 — Per-handler HTTP method via `[CapabilityEndpoint]`

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0002 states HTTP verb is always `POST` in v1 and notes that a future extension point
would allow verb selection. Applications with PUT and PATCH capabilities now need this
selection without waiting for a more complex overload mechanism.

Capability ADR-0010 explicitly excludes a `Method` property from `[Capability]` to keep
that attribute transport-agnostic. The HTTP method therefore belongs in a dedicated,
HTTP-transport-local attribute.

**GET & DELETE is explicitly out of scope** for `MapCapabilities()` auto-discovery: Minimal API
does not bind complex types from the request body for `GET`/`DELETE` requests by default.
Applications that expose GET endpoints with route/query-string parameters must register
them manually (see sample `Program.cs` for the recommended pattern).

## Options

1. **New `[CapabilityEndpoint(string method)]` attribute in `Forge.Capability.Http`.**
   `MapCapabilities()` reads the attribute from each handler type; absent = `"POST"`.
   Uses `app.MapMethods([method], route, handler)` instead of the hardcoded `app.MapPost`.
   The attribute is transport-specific and lives in the HTTP slice, not in core Capability.
2. **`MapCapabilities(Func<Type, string> methodSelector)` overload** — configure the
   verb via a delegate at the call site.
   Con: the mapping between handler type and HTTP method is no longer co-located with
   the handler; split context makes the codebase harder to navigate.
3. **Keep `POST` only; require manual `MapPost`/`MapPut` for other verbs.**
   Con: defeats the auto-discovery benefit for common write operations.

## Decision

Option 1.

### `CapabilityEndpointAttribute` contract

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CapabilityEndpointAttribute : Attribute
{
    public CapabilityEndpointAttribute(string method);
    public string Method { get; }  // upper-cased at construction time
}
```

Usage:

```csharp
[Capability("demo.catalog.items.update")]
[CapabilityEndpoint("PUT")]
public sealed class UpdateItemHandler : ICapabilityHandler<UpdateItemCommand, UpdateItemResponse>
```

### `MapCapabilities()` change

Replace `app.MapPost(routePath, handler)` with
`app.MapMethods(routePath, [httpMethod], handler)` where `httpMethod` is read from
`[CapabilityEndpoint]` on the handler type, defaulting to `"POST"` when absent.

### GET endpoints — manual registration pattern

```csharp
// Registered AFTER AddCapabilityHttp() — not auto-discovered.
builder.Services.AddCapabilityHandler<GetItemQuery, GetItemResponse, GetItemHandler>();

// Manual endpoint: route-parameter binding requires explicit plumbing.
app.MapGet("demo/catalog/items/{id}", async (
    Guid id,
    ICapabilityDispatcher<GetItemQuery, GetItemResponse> dispatcher,
    ICapabilityAspectIriProvider provider,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var iri = await provider.GetCapabilityAspectIriAsync(httpContext, ct);
    return await dispatcher.DispatchAsync(new GetItemQuery(id), iri, ct) switch { ... };
});
```

## Consequences

- PUT and PATCH capabilities are auto-registered with zero manual route plumbing.
- GET capabilities require a small amount of manual endpoint code — documented once per
  GET handler; no plumbing is hidden.
- The change is backward-compatible: all existing handlers without `[CapabilityEndpoint]`
  continue to register as `POST`.
- `[Capability]` remains transport-agnostic per Capability ADR-0010.
