using Forge.Execution;
using Forge.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BranchEntity = Forge.Branch.Branch;

namespace Forge.Branch.Http;

/// <summary>
/// Middleware that establishes an ambient <see cref="BranchScope"/> for every HTTP
/// request. Reads the <c>X-Forge-BranchIri</c> request header via the registered
/// <see cref="IBranchIriProvider"/>. When the header is absent the configured default
/// branch IRI (<see cref="BranchOptions.DefaultBranchIri"/>) is used. A structurally
/// invalid header value results in an immediate 400 Bad Request response. The effective
/// branch IRI is echoed as <c>X-Forge-Effective-BranchIri</c> on the response.
/// See Branch.Http ADR-0002.
/// </summary>
internal sealed class BranchScopeMiddleware
{
    internal const string EffectiveBranchIriResponseHeader = "X-Forge-Effective-BranchIri";

    private readonly RequestDelegate _next;
    private readonly IBranchIriProvider _provider;
    private readonly string _defaultBranchIri;
    private readonly IEntityStore _managementStore;

    public BranchScopeMiddleware(
        RequestDelegate next,
        IBranchIriProvider provider,
        IOptions<BranchOptions> options,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _provider = provider;
        _defaultBranchIri = options.Value.DefaultBranchIri;
        _managementStore = services.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
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
            // Validate that a non-default branch IRI actually corresponds to a known
            // branch or snapshot in the management graph. An unknown IRI would silently
            // create an orphan data graph; reject it early with 404 instead.
            if (!string.Equals(effectiveBranchIri, _defaultBranchIri, StringComparison.Ordinal))
            {
                var branch = await _managementStore
                    .LoadAsync<BranchEntity>(effectiveBranchIri, context.RequestAborted)
                    .ConfigureAwait(false);
                if (branch is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(
                        new ExecutionError("BRANCH_NOT_FOUND",
                            $"No branch or snapshot with IRI '{effectiveBranchIri}' was found."),
                        context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[EffectiveBranchIriResponseHeader] = effectiveBranchIri;
                return Task.CompletedTask;
            });

            await _next(context).ConfigureAwait(false);
        }
    }
}
