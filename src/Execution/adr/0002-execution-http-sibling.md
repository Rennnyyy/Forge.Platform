# 0002 — Forge.Execution.Http: ASP.NET transport companion

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

ADR-0001 introduced `Forge.Execution` as a pure, framework-agnostic contract layer
(`ExecutionError`, `ExecutionResult<TResponse>`, `ExecutionCorrelation`). Three
ASP.NET-specific concerns were deliberately excluded from it to avoid forcing a
`Microsoft.AspNetCore.App` reference onto every consumer:

1. **Aspect-IRI resolution from HTTP requests** — `ICapabilityAspectIriProvider` in
   `Forge.Capability.Http` is a seam that both the capability and operations HTTP slices
   need, but with different header names (`X-Forge-Capability-AspectIri` vs
   `X-Forge-Operation-AspectIri`). A generalized, parameterized version is needed.

2. **Correlation middleware** — reading `X-Forge-Correlation-ID` from the incoming
   request, generating the execution-scoped `ExecutionId`, establishing an
   `ExecutionContext` scope, and writing `X-Forge-Execution-ID` to the response.

3. **Exception-to-422 mapping** — `Capability.Http` ADR-0007 and ADR-0008 catch
   `MessageAspectViolationException` and `AspectViolationException` inside endpoint
   lambdas. `Operations.Http` needs the same mapping. Duplicating try/catch blocks in
   each slice's endpoint factories is fragile and will drift.

A single `Forge.Execution.Http` sibling slice addresses all three without spreading
ASP.NET dependencies into the pure `Forge.Execution` layer.

## Options

1. **New `src/Execution.Http/` sibling** — ASP.NET transport companion, depends on
   `Forge.Execution` and `Microsoft.AspNetCore.App`. Provides the three shared
   HTTP-layer concerns. `Capability.Http` and `Operations.Http` depend on it instead
   of duplicating the logic.

2. **Embed HTTP concerns in `Forge.Execution`** — take the `FrameworkReference` there.
   Pro: one fewer project. Con: `Forge.Execution` can no longer be used in non-HTTP
   transports without dragging ASP.NET in; contradicts ADR-0001's stated goal.

3. **Embed in `Forge.Capability.Http`** — `Operations.Http` takes a `ProjectReference`
   to `Capability.Http` just for shared infrastructure. Con: wrong dependency polarity;
   `Operations.Http` must not imply a capability dependency.

## Decision

Option 1.

### Dependency graph

```
Forge.Execution.Http
  → Forge.Execution
  → Forge.Aspects          (for exception catch types)
  → Microsoft.AspNetCore.App
```

### Public surface

#### `IExecutionAspectIriProvider`

Generalizes `ICapabilityAspectIriProvider`. Replaces it across the platform;
`Forge.Capability.Http` migrates to this interface (see Capability.Http ADR-0009 for
the full migration record).

```csharp
namespace Forge.Execution.Http;

public interface IExecutionAspectIriProvider
{
    ValueTask<string?> GetAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
```

#### `HeaderExecutionAspectIriProvider`

Parameterized header-backed implementation. Each transport slice passes its own header
name at registration time:

```csharp
namespace Forge.Execution.Http;

public sealed class HeaderExecutionAspectIriProvider : IExecutionAspectIriProvider
{
    private readonly string _headerName;

    public HeaderExecutionAspectIriProvider(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        _headerName = headerName;
    }

    public ValueTask<string?> GetAspectIriAsync(HttpContext context, CancellationToken _)
    {
        var value = context.Request.Headers[_headerName].FirstOrDefault();
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return ValueTask.FromResult<string?>(trimmed);
    }
}
```

| Registering slice | Header name |
|-------------------|-------------|
| `Forge.Capability.Http` | `X-Forge-Capability-AspectIri` |
| `Forge.Operations.Http` | `X-Forge-Operation-AspectIri`  |

Applications that need a custom resolution strategy (e.g. read from a JWT claim,
derive from tenant settings) replace the registration with their own
`IExecutionAspectIriProvider` implementation.

#### `ExecutionCorrelationMiddleware`

Establishes the `ExecutionContext` scope for each request.

Pipeline responsibilities:
1. Read `X-Forge-Correlation-ID` from the request headers → `CallerCorrelationId`
   (null when absent or whitespace).
2. Generate a new `ExecutionId` (`Guid.NewGuid().ToString()`).
3. Construct `ExecutionCorrelation` and open an `ExecutionContext.Use(…)` scope.
4. Write `X-Forge-Execution-ID: {ExecutionId}` to the response headers before the
   response body is written (via `context.Response.OnStarting`).
5. Forward to the next middleware inside the scope.

```csharp
internal sealed class ExecutionCorrelationMiddleware(RequestDelegate next)
{
    private const string CorrelationRequestHeader  = "X-Forge-Correlation-ID";
    private const string ExecutionResponseHeader   = "X-Forge-Execution-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var callerCorrelation = context.Request.Headers[CorrelationRequestHeader]
            .FirstOrDefault()?.Trim();
        var callerCorrelationId = string.IsNullOrWhiteSpace(callerCorrelation)
            ? null : callerCorrelation;

        var correlation = new ExecutionCorrelation
        {
            ExecutionId         = Guid.NewGuid().ToString(),
            CallerCorrelationId = callerCorrelationId,
        };

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ExecutionResponseHeader] = correlation.ExecutionId;
            return Task.CompletedTask;
        });

        using var _ = ExecutionContext.Use(correlation);
        await next(context);
    }
}
```

#### `ExecutionEndpointHelper`

Static helper that endpoint lambdas in `Capability.Http` and `Operations.Http` call to
apply the shared exception-to-422 mapping. Centralises the try/catch pattern; each slice
no longer embeds it inline.

```csharp
namespace Forge.Execution.Http;

public static class ExecutionEndpointHelper
{
    /// <summary>
    /// Executes <paramref name="dispatchAsync"/> and maps known aspect violation
    /// exceptions to a 422 response carrying an <see cref="ExecutionError"/>.
    /// All other exceptions propagate unchanged.
    /// </summary>
    public static async ValueTask<IResult> InvokeAsync(
        Func<ValueTask<IResult>> dispatchAsync)
    {
        try
        {
            return await dispatchAsync();
        }
        catch (MessageAspectViolationException ex)
        {
            return Results.UnprocessableEntity(
                new ExecutionError("SHACL_VIOLATION", ex.Message));
        }
        catch (AspectViolationException ex)
        {
            return Results.UnprocessableEntity(
                new ExecutionError("ENTITY_SHACL_VIOLATION", ex.Message));
        }
    }
}
```

### DI and pipeline registration

```csharp
// services
services.AddExecutionHttp();          // registers ExecutionCorrelationMiddleware

// pipeline
app.UseExecutionCorrelation();        // adds ExecutionCorrelationMiddleware
```

`UseExecutionCorrelation()` must be called before `MapCapabilities()` /
`MapOperations()` / `UseAgentTokenMiddleware()` so that `ExecutionContext.Current` is
populated when endpoint lambdas run.

## Consequences

- `ICapabilityAspectIriProvider` in `Forge.Capability.Http` is deprecated; Capability.Http
  ADR-0009 records its replacement with `IExecutionAspectIriProvider`.
- `Forge.Capability.Http` and `Forge.Operations.Http` drop their own try/catch blocks and
  call `ExecutionEndpointHelper.InvokeAsync` instead.
- Adding a third transport slice (messaging, gRPC) requires only a `ProjectReference` to
  `Forge.Execution`; it never needs `Forge.Execution.Http`.
- `ExecutionId` appears in response headers and is therefore available in Bruno collection
  assertions for tracing test executions (`res.headers["X-Forge-Execution-ID"]`).
- `CallerCorrelationId` is propagated in `ExecutionContext.Current` and can be forwarded
  by any outbound HTTP client middleware (not wired in v1 — left to application code).
