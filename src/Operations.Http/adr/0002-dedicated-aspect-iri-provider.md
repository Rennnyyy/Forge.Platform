# 0002 — Operations.Http resolves its aspect-IRI provider directly, not via the shared DI slot

- **Status**: accepted
- **Date**: 2026-05-07
- **Author**: agent
- **Supersedes**: the provider-registration section of [ADR-0001](0001-operations-http-slice.md)

## Context

ADR-0001 specified that `AddOperationEndpointsHttp()` registers the operation-aspect
IRI provider via:

```csharp
services.TryAddSingleton<IExecutionAspectIriProvider>(
    _ => new HeaderExecutionAspectIriProvider("X-Forge-Operation-AspectIri"));
```

`TryAddSingleton` was chosen so that a caller who register a custom provider first
would have it preserved. However, `Capability.Http` registers the _same_ DI slot
(`IExecutionAspectIriProvider`) with `X-Forge-Capability-AspectIri` via its own
`TryAddSingleton` call inside `AddCapabilityHttp()`. When both slices are present in
one DI container — the dominant production case — the first registration wins and
`Operations.Http` endpoints silently read from `X-Forge-Capability-AspectIri` instead
of `X-Forge-Operation-AspectIri`, which is a silent, hard-to-diagnose correctness bug.

## Options

1. **Resolve the provider directly in `MapOperations()`** — create a
   `HeaderExecutionAspectIriProvider("X-Forge-Operation-AspectIri")` once at
   route-registration time and capture it in the endpoint lambda closures. The shared
   `IExecutionAspectIriProvider` DI slot is no longer touched by `Operations.Http`.
   Pro: no DI conflict; deterministic; the header name is a public constant.
   Con: a caller cannot substitute a custom provider via DI (the slot is separate).

2. **Use keyed DI** (`AddKeyedSingleton` / `[FromKeyedServices]`).
   Pro: allows custom providers per slice while avoiding conflict.
   Con: keyed DI is not fully supported in ASP.NET Core Minimal API parameter binding
   without extra plumbing; adds observable API surface (a public key constant).

3. **Define a separate `IOperationAspectIriProvider` interface** (a sub-interface or
   marker wrapper).
   Pro: clean DI disambiguation.
   Con: new public type purely for wiring; callers still must implement or wrap.

## Decision

Option 1.

`MapOperations()` creates its own `HeaderExecutionAspectIriProvider` instance pinned to
`"X-Forge-Operation-AspectIri"` at call time and passes it as a closure to each
endpoint lambda. `Operations.Http` no longer calls `TryAddSingleton<IExecutionAspectIriProvider>`.

`OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader` remains a public
constant so callers can reference the correct header name without a magic string.

## Consequences

- Operations endpoints always read from `X-Forge-Operation-AspectIri`, regardless of
  what is registered in DI for `IExecutionAspectIriProvider`.
- `Capability.Http` and `Operations.Http` can coexist in the same DI container without
  either silently hijacking the other's header.
- A caller who wants to supply a fully custom provider for `Operations.Http` can
  subclass or wrap `MapOperations()` with a different extension method; this is an
  intentionally rare scenario not warranting a DI hook.
- The `IExecutionAspectIriProvider` endpoint-parameter injection is removed from the
  three CUD endpoint lambdas in `RegisterEndpointsFor<T>()`.
