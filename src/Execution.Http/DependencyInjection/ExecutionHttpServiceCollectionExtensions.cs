using Forge.Branch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Execution.Http.DependencyInjection;

/// <summary>
/// DI extensions for the Execution.Http slice.
/// See Execution ADR-0002 and Execution.Http ADR-0001.
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

    /// <summary>
    /// Registers branch-scope infrastructure required by <see cref="BranchScopeMiddleware"/>.
    /// Binds <see cref="BranchOptions"/> from <paramref name="configuration"/> under
    /// <c>Forge:Branch</c> and registers <see cref="HeaderBranchIriProvider"/> as the
    /// singleton <see cref="IBranchIriProvider"/>.
    /// Call <c>UseBranchScope()</c> on the pipeline builder to activate the middleware.
    /// See Execution.Http ADR-0001.
    /// </summary>
    public static IServiceCollection AddBranchHttp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<BranchOptions>(configuration.GetSection("Forge:Branch"));
        services.AddSingleton<IBranchIriProvider, HeaderBranchIriProvider>();
        return services;
    }
}
