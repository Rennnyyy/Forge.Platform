using Microsoft.AspNetCore.Builder;

namespace Forge.Branch.Http.DependencyInjection;

/// <summary>
/// Pipeline-builder extensions for the Branch.Http slice.
/// See Branch.Http ADR-0002.
/// </summary>
public static class BranchHttpApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="BranchScopeMiddleware"/> to the pipeline.
    /// Must be registered after <c>UseExecutionCorrelation()</c> and before any endpoint
    /// middleware. Sets an ambient <see cref="Forge.Repository.BranchScope"/> for every
    /// request so downstream handlers and entity operations target the correct named graph.
    /// See Branch.Http ADR-0002.
    /// </summary>
    public static IApplicationBuilder UseBranchScope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<BranchScopeMiddleware>();
    }
}
