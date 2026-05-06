# 0001 — Operations.Http slice: entity CRUD as direct REST endpoints

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

`Forge.Capability.Generators` can emit full capability handler classes for entity CRUD
via `[CrudCapabilities]`, surfaced over HTTP by `Forge.Capability.Http`. That path runs
through the capability dispatcher pipeline: aspect-store lookup, message-SHACL validation,
`CapabilityContext` construction, handler invocation, response validation. For many
entity-centric use cases this pipeline is heavier than needed: the entity's persistence
surface already exists as generated active-record methods in `Forge.Operations`, and the
only server-side safety net needed is an optional `IOperationAspect` SHACL constraint
on the write, not a full message-shape validation pass.

A lighter, direct path is needed:

1. The developer annotates an entity class with `[OperationEndpoints]`.
2. `app.MapOperations()` auto-registers a standard REST endpoint set for every annotated
   entity type discovered in the registered assemblies.
3. Write operations go through `EntityTransaction` (reaching
   `AspectEnforcingTransactionalStore` when aspects are wired) with an aspect IRI read
   from the `X-Forge-Operation-AspectIri` request header.
4. Read and List are plain `EntityOperations.ReadAsync / ListAsync` calls.
5. Errors and results use `ExecutionError` / `ExecutionResult<T>` from `Forge.Execution`.

### Design questions addressed by this ADR

1. **Where does `[OperationEndpoints]` live?** In `Forge.Operations` (no HTTP dep) —
   following the same pattern as `[CrudCapabilities]` in `Forge.Capability`.
2. **Route convention?** Full REST resource model: collection + item routes, all five
   HTTP verbs, IRI via query string for item-scoped operations.
3. **Discovery mechanism?** Assembly scanning (consistent with `AddCapabilityHandlers`).
4. **Message-SHACL?** Not applied — `Operations.Http` does not wire an
   `IMessageAspectEngine`. Request JSON is deserialized and validated only by the
   ASP.NET model-binding pipeline.
5. **Aspect IRI propagation?** The raw IRI from the header is passed directly to
   `EntityTransaction.Create / Update / Delete` as the `aspectIri` parameter.
   `AspectEnforcingTransactionalStore` resolves and applies the `IOperationAspect`.

## Options

1. **New `src/Operations.Http/` slice with `[OperationEndpoints]` in `Forge.Operations`
   and `MapOperations()` in `Forge.Operations.Http`.**
   Follows the `Capability` / `Capability.Http` split precedent exactly.
2. Expose CRUD directly through the existing `Capability.Http` pipeline with the
   dual-prefix routing from ADR-0006.
   Con: forces message-SHACL on every CRUD write; heavier than required; Capability.Http
   ADR-0009 retires the dual-prefix logic anyway.
3. Hand-write one `app.MapPost / MapGet / MapPut / MapDelete` per entity.
   Con: zero-ceremony goal violated; routes drift; no aspect-IRI header convention.

## Decision

Option 1.

---

### Dependency graph

```
Forge.Operations.Http
  → Forge.Operations
  → Forge.Execution.Http       (IExecutionAspectIriProvider, ExecutionEndpointHelper,
                                 ExecutionCorrelation, ExecutionResult<T>, ExecutionError)
  → Forge.Repository.Transaction  (ITransactionalEntityStore, EntityTransaction)
  → Microsoft.AspNetCore.App
```

`Forge.Capability` and `Forge.Capability.Http` are **not** dependencies.

---

### `[OperationEndpoints]` attribute — in `Forge.Operations`

```csharp
namespace Forge.Operations;

/// <summary>
/// Opts an <c>[Entity]</c>-annotated class into REST endpoint registration by
/// <c>Forge.Operations.Http</c>'s <c>MapOperations()</c>.
/// </summary>
/// <param name="path">
/// The URL path segment used as the collection resource identifier.
/// Defaults to <c>typeof(T).Name.ToLowerInvariant()</c> when absent.
/// Convention: use the plural form (e.g. <c>"artists"</c>, <c>"albums"</c>).
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OperationEndpointsAttribute : Attribute
{
    public OperationEndpointsAttribute(string? path = null)
    {
        Path = path;
    }

    /// <summary>
    /// The plural noun segment used in the route (e.g. <c>"artists"</c>).
    /// When <see langword="null"/>, <c>MapOperations()</c> falls back to
    /// <c>typeof(T).Name.ToLowerInvariant()</c>.
    /// </summary>
    public string? Path { get; }
}
```

---

### REST route convention

All five verbs are registered for every `[OperationEndpoints]` entity.
Item-scoped operations (Read, Update, Delete) receive the entity IRI via the
`?iri=…` query string parameter. This avoids route-parameter encoding issues with
full IRI strings that contain slashes and special characters.

| Verb   | Route                          | Operation | Body |
|--------|--------------------------------|-----------|------|
| `POST` | `api/entities/{path}`          | Create    | JSON command |
| `GET`  | `api/entities/{path}`          | List      | — |
| `GET`  | `api/entities/{path}?iri=…`    | Read      | — |
| `PUT`  | `api/entities/{path}?iri=…`    | Update    | JSON command |
| `DELETE` | `api/entities/{path}?iri=…`  | Delete    | — |

`MapOperations()` registers the GET pair as two separate `app.MapGet` calls distinguished
by whether the `iri` query-string parameter is present:

* `GET api/entities/{path}` — no `iri` parameter → List
* `GET api/entities/{path}?iri=…` — `iri` present → Read

In Minimal API, query-string binding is parameter-presence-based: if the `iri` parameter
is declared as `string? iri = null`, both routes share the same handler and branch
internally.

---

### Command / Response shape

`Operations.Http` does not use generated command records from `Forge.Capability.Generators`.
It binds entity properties directly, using a convention-based approach:

- **Create** — binds the full entity record from JSON body (the entity type is the
  command). Returns `ExecutionResult<OperationCreatedResponse>` where
  `OperationCreatedResponse(string Iri)`.
- **List** — returns `ExecutionResult<OperationListResponse<T>>` where
  `OperationListResponse<T>(IReadOnlyList<T> Items)`.
- **Read** — returns `ExecutionResult<T>` with the loaded entity, or
  `ExecutionResult.Fail(new ExecutionError("NOT_FOUND", …))` when absent.
- **Update** — binds the entity record from JSON body; the `iri` query parameter sets
  the entity's IRI before save. Returns `ExecutionResult<OperationUpdatedResponse>`
  where `OperationUpdatedResponse(string Iri)`.
- **Delete** — no body; uses `iri` query parameter. Returns
  `ExecutionResult<OperationDeletedResponse>` where `OperationDeletedResponse()`.

`OperationCreatedResponse`, `OperationListResponse<T>`, `OperationUpdatedResponse`, and
`OperationDeletedResponse` are defined in `Forge.Operations.Http`.

---

### Aspect IRI and operation constraints

The aspect IRI is read from `X-Forge-Operation-AspectIri` via
`IExecutionAspectIriProvider` registered by `AddOperationEndpointsHttp()`:

```csharp
services.TryAddSingleton<IExecutionAspectIriProvider>(
    _ => new HeaderExecutionAspectIriProvider("X-Forge-Operation-AspectIri"));
```

For CUD endpoints, the resolved IRI (or `Aspect.NoOpIri` when absent) is passed to
`EntityTransaction`:

```csharp
var aspectIri = await provider.GetAspectIriAsync(httpContext, ct)
    ?? Aspect.NoOpIri;

await using var tx = new EntityTransaction(txStore);
tx.Create(entity, aspectIri);
await tx.CommitAsync(ct);
```

Read and List are pure `EntityOperations` calls; no aspect IRI is consumed for them.

---

### Exception mapping

All endpoint lambdas delegate to `ExecutionEndpointHelper.InvokeAsync(…)` from
`Forge.Execution.Http`. `AspectViolationException` → 422 `ENTITY_SHACL_VIOLATION`;
`MessageAspectViolationException` → 422 `SHACL_VIOLATION` (should not occur in this
slice since message-SHACL is not wired, but is handled for safety).

---

### DI and pipeline registration

```csharp
// services
builder.Services.AddOperationEndpointsHttp(typeof(MyMarker).Assembly);
// or
builder.Services.AddOperationEndpointsHttp(assemblyA, assemblyB);

// pipeline — after UseAgentTokenMiddleware, UseExecutionCorrelation
app.MapOperations();
```

`AddOperationEndpointsHttp(params Assembly[])` scans the supplied assemblies for
concrete `[Entity]`-annotated types that also carry `[OperationEndpoints]` and registers
an `OperationEndpointDescriptor` (internal) per type. `MapOperations()` resolves all
descriptors and installs the five endpoint pairs.

A convenience overload `AddOperationEndpointsHttpFromAssemblyContaining<T>()` is
provided following the pattern of `AddCapabilityHandlersFromAssemblyContaining<T>`.

`AddOperationEndpointsHttp()` also registers the `IExecutionAspectIriProvider` singleton
(with `TryAddSingleton` semantics so a custom provider registered first is preserved) and
requires `ITransactionalEntityStore` to be in the container.

---

### Coexistence with `[CrudCapabilities]` / `MapCapabilities()`

`[OperationEndpoints]` and `[CrudCapabilities]` may coexist on the same entity class.
They produce separate endpoint sets under separate routes:

| Annotation | Route prefix | Dispatch path |
|------------|-------------|----------------|
| `[OperationEndpoints]` | `api/entities/` | Direct `EntityTransaction` |
| `[CrudCapabilities]` | `api/capabilities/` | `ICapabilityDispatcher` + message-SHACL |

Applications choose which surface (or both) to expose. There is no collision because the
prefixes are distinct after Capability.Http ADR-0009 retires the dual-prefix logic.

---

### Project layout

```
src/Operations.Http/
  OperationEndpointDescriptor.cs      (internal)
  OperationEndpointAttribute.cs       → lives in Forge.Operations, not here
  OperationEndpointsEndpointRouteBuilderExtensions.cs
  OperationResponses.cs               (Created/Updated/Deleted/List response types)
  DependencyInjection/
    OperationEndpointsHttpServiceCollectionExtensions.cs
  adr/
    README.md
    0001-operations-http-slice.md
  Forge.Operations.Http.csproj
```

The flat structure is at 5 files (excluding ADR and DI sub-folder), within the ADR-0010
threshold for flat layout.

## Consequences

- Entity CRUD is reachable over HTTP with minimal boilerplate: annotate + call
  `AddOperationEndpointsHttp` + `MapOperations()`.
- The capability dispatcher pipeline is not involved; message-SHACL validation is not
  applied. Applications that need message-level SHACL for CRUD should use
  `[CrudCapabilities]` + `MapCapabilities()` instead.
- Operation-aspect (`IOperationAspect`) constraints are reachable end-to-end via the
  `EntityTransaction` path, consistent with the capability path (Capability ADR-0015).
- `api/entities/` is the exclusive URL domain of `MapOperations()` after Capability.Http
  ADR-0009 retires the old dual-prefix logic.
- `ExecutionCorrelation` is populated per-request (via `UseExecutionCorrelation()`) and
  available to operation endpoint lambdas via `ExecutionContext.Current`.
- Test project: `tests/Operations.Http.Tests/` follows the existing HTTP test slice
  pattern (`tests/Capability.Http.Tests/`).
