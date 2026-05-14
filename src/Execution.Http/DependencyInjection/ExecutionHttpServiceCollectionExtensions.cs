using Microsoft.Extensions.DependencyInjection;

namespace Forge.Execution.Http.DependencyInjection;

/// <summary>
/// DI extensions for the Execution.Http slice.
/// See Execution ADR-0002 and Execution.Http ADR-0002.
/// </summary>
public static class ExecutionHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Execution.Http infrastructure. Call this before
    /// <c>UseExecutionCorrelation()</c>.
    /// </summary>
    public static IServiceCollection AddExecutionHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
