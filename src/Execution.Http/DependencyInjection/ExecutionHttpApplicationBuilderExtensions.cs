using Microsoft.AspNetCore.Builder;

namespace Forge.Execution.Http.DependencyInjection;

/// <summary>
/// Pipeline-builder extension for the Execution.Http slice.
/// See Execution ADR-0002 and Execution.Http ADR-0001.
/// </summary>
public static class ExecutionHttpApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="ExecutionCorrelationMiddleware"/> to the pipeline.
    /// Should be registered early so all downstream handlers see a populated
    /// <see cref="ExecutionScope.Current"/>.
    /// </summary>
    public static IApplicationBuilder UseExecutionCorrelation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ExecutionCorrelationMiddleware>();
    }

    /// <summary>
    /// Adds <see cref="BranchScopeMiddleware"/> to the pipeline.
    /// Must be registered after <c>UseExecutionCorrelation()</c> and before any endpoint
    /// middleware. Sets an ambient <see cref="Forge.Repository.BranchScope"/> for every
    /// request so downstream handlers and entity operations target the correct named graph.
    /// See Execution.Http ADR-0001.
    /// </summary>
    public static IApplicationBuilder UseBranchScope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<BranchScopeMiddleware>();
    }
}
