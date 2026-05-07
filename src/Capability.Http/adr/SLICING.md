# SLICING — Forge.Capability.Http

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Capability.Http` | ASP.NET Core endpoint mapping for Capability-annotated handlers. | A file belongs here if it bridges the Capability pattern to the HTTP layer: the `CapabilityEndpointAttribute` for annotating handlers, `EndpointRouteBuilderExtensions` for `MapCapabilities`, `ICapabilityAspectIriProvider` and `HeaderCapabilityAspectIriProvider` for resolving the aspect IRI from request headers, and `CapabilityHandlerDescriptor` for reflection-based handler discovery. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Capability.Http`)

- `CapabilityEndpointAttribute.cs` — marks a `ICapabilityHandler<,>` implementation as an HTTP endpoint.
- `EndpointRouteBuilderExtensions.cs` — `MapCapabilities` extension that registers all annotated capability handlers as Minimal API endpoints.
- `ICapabilityAspectIriProvider.cs` — contract for resolving a capability aspect IRI from the current HTTP request.
- `HeaderCapabilityAspectIriProvider.cs` — default implementation that reads the aspect IRI from a request header.
- `CapabilityHandlerDescriptor.cs` — reflection helper that describes a discovered capability handler (route, command type, response type).
