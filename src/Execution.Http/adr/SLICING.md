# SLICING — Forge.Execution.Http

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).
Architectural decisions for this slice are in [ADR-0002](../../Execution/adr/0002-execution-http-sibling.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Execution.Http` | Public ASP.NET contracts and helpers shared across transport slices. | A file belongs here if it is a public type consumed by `Capability.Http` or `Operations.Http`: `IExecutionAspectIriProvider`, `HeaderExecutionAspectIriProvider`, `ExecutionEndpointHelper`, `ExecutionCorrelationMiddleware`. |
| `DependencyInjection/` | `Forge.Execution.Http.DependencyInjection` | Framework-driven registration extensions. | All DI extension classes (`IServiceCollection`, `IApplicationBuilder`) go here. Excluded from the file-count threshold per ADR-0010. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework-driven sub-folder; excluded per ADR-0010 convention. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Execution.Http`)

- `IExecutionAspectIriProvider.cs` — contract that resolves the execution-aspect IRI from an HTTP request (generalises the old `ICapabilityAspectIriProvider`).
- `HeaderExecutionAspectIriProvider.cs` — header-backed implementation; parameterised by header name so each transport slice uses its own header.
- `ExecutionEndpointHelper.cs` — shared invocation wrapper that translates `MessageAspectViolationException` and `AspectException` to 422 responses.
- `ExecutionCorrelationMiddleware.cs` — reads `X-Forge-Correlation-ID`, generates an execution-scoped `ExecutionId`, establishes an `ExecutionCorrelation` scope, and writes `X-Forge-Execution-ID` to the response.

### `DependencyInjection/` (`Forge.Execution.Http.DependencyInjection`)

- `ExecutionHttpServiceCollectionExtensions.cs` — `AddForgeExecutionHttp()` wires `HeaderExecutionAspectIriProvider` and related services.
- `ExecutionHttpApplicationBuilderExtensions.cs` — `UseForgeExecutionCorrelation()` adds `ExecutionCorrelationMiddleware` to the pipeline.
