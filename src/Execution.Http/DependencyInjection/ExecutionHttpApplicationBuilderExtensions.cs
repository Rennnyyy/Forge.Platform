using Microsoft.AspNetCore.Builder;

namespace Forge.Execution.Http.DependencyInjection;

/// <summary>
/// Pipeline-builder extension for the Execution.Http slice.
/// See Execution ADR-0002.
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
}
