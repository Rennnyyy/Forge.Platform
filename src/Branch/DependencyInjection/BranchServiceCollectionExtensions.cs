using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Forge.Branch.DependencyInjection;

/// <summary>DI extensions for the Branch slice. See Branch ADR-0001.</summary>
public static class BranchServiceCollectionExtensions
{
    /// <summary>
    /// Configuration section path used by <see cref="AddForgeBranch"/>: <c>Forge:Branch</c>.
    /// </summary>
    public const string ConfigurationSection = "Forge:Branch";

    /// <summary>
    /// Registers Branch infrastructure:
    /// <list type="bullet">
    ///   <item>Binds <see cref="BranchOptions"/> from <c>Forge:Branch</c> config section.</item>
    ///   <item>Populates <see cref="BranchDefault.BranchIri"/> from the configured default.</item>
    ///   <item>Syncs <see cref="BranchOptions.DefaultBranchIri"/> into
    ///         <see cref="EntityRepositoryOptions.DefaultBranchIri"/> so backend stores pick it up.</item>
    ///   <item>Registers a keyed management graph <see cref="ITransactionalEntityStore"/>
    ///         (key <c>"forge.branch.management"</c>) that always targets
    ///         <see cref="BranchOptions.ManagementGraphIri"/>.</item>
    ///   <item>Registers a hosted service that upserts the default <see cref="Branch"/>
    ///         entity at application start.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Must be called after <c>AddForgeEntityRepository()</c> and the chosen backend
    /// (<c>UseInMemory()</c> / <c>UseGraphDb()</c>).
    /// </remarks>
    public static IServiceCollection AddForgeBranch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── 1. Bind BranchOptions ─────────────────────────────────────────────
        services.Configure<BranchOptions>(configuration.GetSection(ConfigurationSection));

        // ── 2. Populate BranchDefault at registration time ────────────────────
        // Read the configured value immediately so it is available synchronously
        // to any code that reads BranchDefault.BranchIri before the DI container is built.
        var defaultBranchIri = configuration[$"{ConfigurationSection}:{nameof(BranchOptions.DefaultBranchIri)}"]
            ?? new BranchOptions().DefaultBranchIri;
        BranchDefault.BranchIri = defaultBranchIri;

        // ── 3. Sync into EntityRepositoryOptions ─────────────────────────────
        // PostConfigure guarantees this runs after all Configure calls, making it the
        // authoritative value regardless of registration order.
        services.PostConfigure<EntityRepositoryOptions>(opts =>
            opts.DefaultBranchIri = BranchDefault.BranchIri);

        // ── 4. Keyed management graph store ───────────────────────────────────
        // Uses EntityStoreFactory (registered by UseInMemory / UseGraphDb) to create a
        // store instance with NamedGraph = ManagementGraphIri, permanently bypassing
        // BranchScope for all management-graph operations.
        // The raw store is then wrapped with BranchGuardedTransactionalStore to enforce
        // the branch protection invariants (no delete of default branch, no drop of
        // management graph).
        services.AddKeyedSingleton<ITransactionalEntityStore>(
            "forge.branch.management",
            (sp, _) =>
            {
                var branchOpts = sp.GetRequiredService<IOptions<BranchOptions>>().Value;
                var factory = sp.GetRequiredService<EntityStoreFactory>();
                ITransactionalEntityStore raw = factory(new EntityRepositoryOptions
                {
                    NamedGraph = branchOpts.ManagementGraphIri,
                });
                return new BranchGuardedTransactionalStore(
                    raw,
                    branchOpts.DefaultBranchIri,
                    branchOpts.ManagementGraphIri);
            });

        // Also expose as keyed IEntityStore (for IEntityRepository<Branch> resolution).
        services.AddKeyedSingleton<IEntityStore>(
            "forge.branch.management",
            (sp, _) => sp.GetRequiredKeyedService<ITransactionalEntityStore>("forge.branch.management"));

        // ── 5. Startup upsert ─────────────────────────────────────────────────
        services.AddHostedService<DefaultBranchStartupService>();

        return services;
    }
}
