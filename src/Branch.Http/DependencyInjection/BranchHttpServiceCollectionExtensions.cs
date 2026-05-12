using Forge.Aspects;
using Forge.Aspects.DependencyInjection;
using Forge.Branch.DependencyInjection;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Branch.Http.DependencyInjection;

/// <summary>
/// DI extensions for the Branch HTTP layer. See Branch.Http ADR-0001 and root ADR-0019.
/// </summary>
public static class BranchHttpServiceCollectionExtensions
{
    private const string ManagementStoreKey = "forge.branch.management";

    /// <summary>
    /// Registers all Branch infrastructure (<see cref="BranchServiceCollectionExtensions.AddForgeBranch"/>)
    /// and additionally wires <c>AspectEnforcingTransactionalStore</c> on the management
    /// keyed store (obligation 2 of root ADR-0019).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this method instead of <c>AddForgeBranch()</c> in any application that also calls
    /// <c>AddForgeAspects()</c>. Using <c>AddForgeBranch()</c> alone leaves the management
    /// store outside the aspect enforcement chain, which is detected and rejected at startup
    /// by <c>ManagedEntityAspectValidationService</c>.
    /// </para>
    /// <para>
    /// The SPARQL backend used by the aspect Context pass is resolved via the raw (unguarded)
    /// management store registered under <c>"forge.branch.management.raw"</c> by
    /// <c>AddForgeBranch()</c>. See Branch.Http ADR-0001 (Option β).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddForgeBranchHttp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddForgeBranch(configuration);

        // The sparqlStoreResolver casts the raw (unguarded) backend — already registered
        // under ManagementStoreKey + ".raw" by AddForgeBranch() — to ISparqlQueryStore.
        // The inner (guarded chain) is discovered automatically by AddForgeAspectsForKeyedStore
        // via descriptor capture. See Branch.Http ADR-0001 and root ADR-0019.
        services.AddForgeAspectsForKeyedStore(
            ManagementStoreKey,
            sparqlStoreResolver: sp =>
                (ISparqlQueryStore)sp.GetRequiredKeyedService<ITransactionalEntityStore>(
                    ManagementStoreKey + ".raw"));

        return services;
    }
}
