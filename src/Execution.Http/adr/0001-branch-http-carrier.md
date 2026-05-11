# 0001 — Branch HTTP carrier: X-Forge-BranchIri header and BranchScopeMiddleware

- **Status**: accepted
- **Date**: 2026-05-10
- **Author**: agent

## Context

Repository ADR-0002 introduces `BranchScope` (`AsyncLocal<string?>` in `Forge.Repository`)
as the ambient mechanism for routing store operations to the correct named graph. It states
that HTTP middleware is responsible for setting the scope from a request header before the
handler runs, mirroring the `ExecutionCorrelationMiddleware` pattern already present in
this slice.

Without the middleware, every HTTP request silently operates on the ambient-null fallback
path (the configured default branch), making branch-isolated writes impossible over HTTP
without callers constructing `BranchScope.Use(...)` manually inside every handler.

Three decisions from the brainstorm govern this ADR:

1. The header is `X-Forge-BranchIri` (request) — following the `X-Forge-` namespace
   convention (root ADR-0006).
2. The effective branch IRI is echoed as `X-Forge-Effective-BranchIri` (response) for
   debugging transparency.
3. The carrier lives in `Forge.Execution.Http` — no new package.

## Options

### Provider abstraction

**Option A** — Mirror `IExecutionAspectIriProvider` / `HeaderExecutionAspectIriProvider`
exactly: define `IBranchIriProvider` with a single `GetBranchIriAsync(HttpContext, CT)`
method, and a `HeaderBranchIriProvider` implementation parameterized by header name.

**Option B** — Skip the abstraction; hardcode the header read inside the middleware.
Pro: fewer types. Con: no extension point for deployments that derive the branch IRI from
a JWT claim, tenant mapping, or route segment; contradicts the pattern already established
for aspect IRIs.

### Absent header behaviour

**Option I** — Absent header → fall back to `BranchOptions.DefaultBranchIri` and set
the scope explicitly. The scope is always non-null inside a request handler; the null
ambient path is never exercised during HTTP request processing.

**Option II** — Absent header → do not set a scope (null ambient). Consumers fall through
to their own fallback logic.
Con: inconsistent; some consumers may not have access to `BranchOptions`; the brainstorm
decided absent = default branch, always explicit.

### Malformed IRI behaviour

**Option X** — Return `400 Bad Request` immediately; do not fall back to the default.
Rationale: a non-whitespace, non-parseable value in `X-Forge-BranchIri` is almost
certainly a caller bug. Silent fallback would mask it.

**Option Y** — Fall back to the default branch on any invalid value.
Con: hides bugs; violates principle of least surprise.

## Decision

**Option A + Option I + Option X.**

### `IBranchIriProvider`

```csharp
namespace Forge.Execution.Http;

/// <summary>
/// Resolves the branch IRI for the current HTTP request.
/// A <c>null</c> return signals "absent header — use the configured default branch".
/// An absent header is not an error; a structurally invalid value is.
/// See Execution.Http ADR-0001.
/// </summary>
public interface IBranchIriProvider
{
    /// <summary>
    /// Returns the branch IRI from <paramref name="context"/>, or <c>null</c> when no
    /// branch header is present (middleware falls back to the configured default branch).
    /// Implementations must return <c>null</c> for an absent or whitespace header value
    /// and must throw <see cref="InvalidBranchIriException"/> for a structurally invalid
    /// non-empty value.
    /// </summary>
    ValueTask<string?> GetBranchIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
```

### `InvalidBranchIriException`

Thrown by `IBranchIriProvider` implementations when the header value is non-empty but
not a valid absolute URI. Caught by `BranchScopeMiddleware` and translated to `400 Bad
Request` without leaking stack traces.

```csharp
namespace Forge.Execution.Http;

/// <summary>
/// Thrown by <see cref="IBranchIriProvider"/> when the supplied branch IRI value is
/// present but not a valid absolute URI. Caught by <see cref="BranchScopeMiddleware"/>
/// and translated to HTTP 400 Bad Request.
/// </summary>
public sealed class InvalidBranchIriException(string value)
    : Exception($"The value '{value}' is not a valid absolute IRI.");
```

### `HeaderBranchIriProvider`

```csharp
namespace Forge.Execution.Http;

/// <summary>
/// <see cref="IBranchIriProvider"/> implementation that reads the branch IRI from
/// the <c>X-Forge-BranchIri</c> request header. Returns <c>null</c> when the header
/// is absent or whitespace. Throws <see cref="InvalidBranchIriException"/> when the
/// value is present but not a valid absolute URI.
/// See Execution.Http ADR-0001.
/// </summary>
public sealed class HeaderBranchIriProvider : IBranchIriProvider
{
    internal const string BranchIriRequestHeader = "X-Forge-BranchIri";

    public ValueTask<string?> GetBranchIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var raw = context.Request.Headers[BranchIriRequestHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return ValueTask.FromResult<string?>(null);

        var trimmed = raw.Trim();
        if (!Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
            throw new InvalidBranchIriException(trimmed);

        return ValueTask.FromResult<string?>(trimmed);
    }
}
```

`Uri.IsWellFormedUriString(..., UriKind.Absolute)` is the validation gate. Relative URIs
and syntactically malformed strings both fail; the middleware returns `400 Bad Request`.

### `BranchScopeMiddleware`

```csharp
namespace Forge.Execution.Http;

/// <summary>
/// Middleware that establishes an ambient <see cref="BranchScope"/> for every HTTP
/// request. Reads the <c>X-Forge-BranchIri</c> request header via the registered
/// <see cref="IBranchIriProvider"/>. When the header is absent the configured default
/// branch IRI is used. A structurally invalid header value results in an immediate
/// 400 Bad Request response. The effective branch IRI is echoed as
/// <c>X-Forge-Effective-BranchIri</c> on the response.
/// See Execution.Http ADR-0001.
/// </summary>
internal sealed class BranchScopeMiddleware
{
    internal const string EffectiveBranchIriResponseHeader = "X-Forge-Effective-BranchIri";

    private readonly RequestDelegate _next;
    private readonly IBranchIriProvider _provider;
    private readonly string _defaultBranchIri;

    public BranchScopeMiddleware(
        RequestDelegate next,
        IBranchIriProvider provider,
        IOptions<BranchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(provider);
        _next = next;
        _provider = provider;
        _defaultBranchIri = options.Value.DefaultBranchIri;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string effectiveBranchIri;
        try
        {
            effectiveBranchIri =
                await _provider.GetBranchIriAsync(context, context.RequestAborted)
                    .ConfigureAwait(false)
                ?? _defaultBranchIri;
        }
        catch (InvalidBranchIriException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(ex.Message, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        using (BranchScope.Use(effectiveBranchIri))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[EffectiveBranchIriResponseHeader] = effectiveBranchIri;
                return Task.CompletedTask;
            });

            await _next(context).ConfigureAwait(false);
        }
    }
}
```

Design notes:
- `BranchOptions` is resolved from `IOptions<BranchOptions>` injected at construction
  (standard ASP.NET middleware DI pattern — constructor injection, not per-request).
- The `400` response body is the exception message only. No stack trace, no inner
  exception detail — the value is caller-supplied and may be large or contain
  control characters.
- `BranchScope.Use` is called **after** the provider resolves successfully, so the scope
  is always non-null inside any downstream handler during HTTP request processing.
- The echo header is written via `Response.OnStarting` — identical to how
  `ExecutionCorrelationMiddleware` writes `X-Forge-Execution-ID`.

### Header catalogue

| Header | Direction | Meaning |
|--------|-----------|---------|
| `X-Forge-BranchIri` | **Request** | IRI of the target branch named graph. Absent → default branch. Invalid absolute URI → `400`. |
| `X-Forge-Effective-BranchIri` | **Response** | The branch IRI that was actually used for this request (header or default). Always present when middleware is registered. |

Both headers follow the `X-Forge-` namespace convention (root ADR-0006). The root ADR-0006
header catalogue must be amended to record these two entries.

### DI registration

`AddBranchHttp()` extension on `IServiceCollection`:

```csharp
services.AddSingleton<IBranchIriProvider, HeaderBranchIriProvider>();
services.Configure<BranchOptions>(configuration.GetSection("Forge:Branch"));
```

`UseBranchScope()` extension on `IApplicationBuilder`:

```csharp
app.UseMiddleware<BranchScopeMiddleware>();
```

Registration order: `UseBranchScope()` must be called **after** `UseExecutionCorrelation()`
and **before** any endpoint middleware. The branch scope must be active by the time
handlers resolve `IEntityRepository<T>` or construct `EntityTransaction`.

## Consequences

- `Forge.Execution.Http` gains a dependency on `Forge.Repository` (for `BranchScope`)
  and on `Forge.Branch` (for `BranchOptions`). The dependency direction is:
  `Forge.Execution.Http → Forge.Branch → Forge.Repository`.
- Every HTTP handler runs inside a non-null `BranchScope` when this middleware is active.
  The null ambient path in `IEntityStore.NamedGraph` (Repository ADR-0002 fallback table
  row "null + empty") is unreachable during normal HTTP request processing.
- `Capability.Http` and `Operations.Http` do not need to set `BranchScope` themselves —
  it is established by this middleware before any handler runs.
- The `X-Forge-Effective-BranchIri` response header makes branch routing transparent to
  API clients and integration-test tooling (Bruno) without additional
  instrumentation.
- Non-existent branch IRIs are not rejected at middleware time (that would require a
  management graph read per request). They surface as `422 Unprocessable Entity` from the
  repository layer when the first store operation targets the empty or absent named graph
  (brainstorm decision).
