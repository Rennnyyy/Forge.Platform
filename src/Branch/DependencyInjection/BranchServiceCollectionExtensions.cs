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
        //
        // The raw (unguarded) backend is exposed under "forge.branch.management.raw" so
        // AddForgeAspectsForKeyedStore() (called by AddForgeBranchHttp) can obtain an
        // ISparqlQueryStore for the Context pass without going through the guard decorators.
        // See root ADR-0019 (Option β for SPARQL resolution).
        services.AddKeyedSingleton<ITransactionalEntityStore>(
            "forge.branch.management.raw",
            (sp, _) =>
            {
                var branchOpts = sp.GetRequiredService<IOptions<BranchOptions>>().Value;
                var factory = sp.GetRequiredService<EntityStoreFactory>();
                return factory(new EntityRepositoryOptions
                {
                    NamedGraph = branchOpts.ManagementGraphIri,
                });
            });

        // The raw store is wrapped with BranchGuardedTransactionalStore (blocks default-
        // branch delete and management-graph drop), then with
        // SnapshotGuardedTransactionalStore (blocks writes to frozen snapshot graphs).
        // The SnapshotGuardedTransactionalStore is also registered as a keyed singleton so
        // SnapshotStartupService and BranchSeedingService can call InvalidateFrozenSetAsync().
        services.AddKeyedSingleton<SnapshotGuardedTransactionalStore>(
            "forge.branch.management",
            (sp, _) =>
            {
                var branchOpts = sp.GetRequiredService<IOptions<BranchOptions>>().Value;
                var raw = sp.GetRequiredKeyedService<ITransactionalEntityStore>("forge.branch.management.raw");
                ITransactionalEntityStore branchGuarded = new BranchGuardedTransactionalStore(
                    raw,
                    branchOpts.DefaultBranchIri,
                    branchOpts.ManagementGraphIri);
                return new SnapshotGuardedTransactionalStore(branchGuarded);
            });

        services.AddKeyedSingleton<ITransactionalEntityStore>(
            "forge.branch.management",
            (sp, _) => sp.GetRequiredKeyedService<SnapshotGuardedTransactionalStore>("forge.branch.management"));

        // Expose as keyed IEntityStore using the concrete guarded store directly so reads
        // are always served by the guarded chain, independent of any subsequent wrapping
        // of the ITransactionalEntityStore slot (e.g. by AddForgeAspectsForKeyedStore).
        services.AddKeyedSingleton<IEntityStore>(
            "forge.branch.management",
            (sp, _) => sp.GetRequiredKeyedService<SnapshotGuardedTransactionalStore>("forge.branch.management"));

        // Expose ISnapshotFrozenSetInvalidator so BranchSeedingService can call
        // InvalidateFrozenSetAsync() without depending on the internal concrete type.
        services.AddKeyedSingleton<ISnapshotFrozenSetInvalidator>(
            "forge.branch.management",
            (sp, _) => sp.GetRequiredKeyedService<SnapshotGuardedTransactionalStore>("forge.branch.management"));

        // Marker consumed by ManagedEntityAspectValidationService (Forge.Aspects) to verify
        // that AddForgeAspectsForKeyedStore has been called for this store when aspects are
        // active. See root ADR-0019.
        services.AddSingleton(new ManagedEntityStoreKeyRegistration("forge.branch.management"));

        // ── 5. Application services ───────────────────────────────────────────
        services.AddScoped<BranchSeedingService>();

        // ── 6. Startup services ───────────────────────────────────────────────
        services.AddHostedService<DefaultBranchStartupService>();
        services.AddHostedService<SnapshotStartupService>();

        // ── 7. Wrap the unkeyed data store with the snapshot immutability guard ─
        // Capture whatever ITransactionalEntityStore is currently the unkeyed registration
        // (after aspects + authorization decorators have already been applied) and replace
        // it with DataSnapshotGuardedTransactionalStore so that entity writes into frozen
        // snapshot named graphs are rejected at the store level, regardless of caller.
        // Same pattern used by AddForgeAuthorizationHttp (Authorization ADR-0002).
        var existingDataDescriptor = services
            .LastOrDefault(d => d.ServiceType == typeof(ITransactionalEntityStore) && d.ServiceKey is null);

        services.AddSingleton<ITransactionalEntityStore>(sp =>
        {
            ITransactionalEntityStore inner = existingDataDescriptor switch
            {
                { ImplementationInstance: ITransactionalEntityStore inst } => inst,
                { ImplementationFactory: { } f } => (ITransactionalEntityStore)f(sp),
                { ImplementationType: { } t } => (ITransactionalEntityStore)ActivatorUtilities.CreateInstance(sp, t),
                _ => sp.GetRequiredKeyedService<ITransactionalEntityStore>(
                    ForgeEntityRepositoryBuilder.AspectsTxKey),
            };
            var guard = sp.GetRequiredKeyedService<SnapshotGuardedTransactionalStore>(
                "forge.branch.management");
            return new DataSnapshotGuardedTransactionalStore(inner, guard);
        });

        return services;
    }
}
