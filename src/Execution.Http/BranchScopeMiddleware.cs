using Forge.Branch;
using Forge.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Forge.Execution.Http;

/// <summary>
/// Middleware that establishes an ambient <see cref="BranchScope"/> for every HTTP
/// request. Reads the <c>X-Forge-BranchIri</c> request header via the registered
/// <see cref="IBranchIriProvider"/>. When the header is absent the configured default
/// branch IRI (<see cref="BranchOptions.DefaultBranchIri"/>) is used. A structurally
/// invalid header value results in an immediate 400 Bad Request response. The effective
/// branch IRI is echoed as <c>X-Forge-Effective-BranchIri</c> on the response.
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
        ArgumentNullException.ThrowIfNull(options);
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
