using Forge.Aspects.Abstractions;
using Forge.Execution;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Forge.Authorization.DependencyInjection;

/// <summary>
/// DI extensions for the Authorization slice. See Authorization ADR-0004.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Decorates the registered <see cref="ITransactionalEntityStore"/> with a
    /// <see cref="GuardedTransactionalStore"/> that enforces pre-commit authorization.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="guard">
    /// The guard to use. When <see langword="null"/>, <see cref="AllowAllAspectGuard.Instance"/>
    /// is used — making this registration safe to include unconditionally in any host.
    /// The resolved guard is also registered as <see cref="IAspectGuard"/> in the DI container
    /// (via <c>TryAddSingleton</c>) so capability dispatchers can resolve it without a
    /// separate explicit registration.
    /// </param>
    /// <remarks>
    /// May be called in any order relative to the backend (<c>UseInMemory()</c> /
    /// <c>UseGraphDb()</c>) and
    /// <see cref="Forge.Aspects.DependencyInjection.AspectsServiceCollectionExtensions.AddForgeAspects"/>.
    /// The inner <see cref="ITransactionalEntityStore"/> is resolved via keyed services at
    /// provider-build time rather than at registration time (see ADR-0014).
    /// </remarks>
    public static IServiceCollection AddForgeAuthorization(
        this IServiceCollection services,
        IAspectGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var effectiveGuard = guard ?? AllowAllAspectGuard.Instance;

        // Make the resolved guard visible in the DI container so that capability
        // dispatchers (and any diagnostics tooling) can resolve IAspectGuard without
        // needing a separate explicit registration. TryAdd semantics: a guard registered
        // by the application before calling AddForgeAuthorization takes precedence.
        services.TryAddSingleton<IAspectGuard>(effectiveGuard);

        // Register the agent token accessor so capability dispatchers can read
        // AuthorizationContext.CurrentAgentToken without a direct reference to
        // Forge.Authorization. See Capability ADR-0019.
        services.TryAddSingleton<IAgentTokenAccessor, AuthorizationAgentTokenAccessor>();

        // Warn at startup when the permissive allow-all guard is active so operators can
        // detect it in structured logs before reaching production. See Authorization ADR-0007.
        if (effectiveGuard is AllowAllAspectGuard)
            services.AddSingleton<IHostedService, AllowAllGuardWarningService>();

        // Capture any unkeyed ITransactionalEntityStore that was registered *before* this call
        // (e.g. AspectEnforcingTransactionalStore from AddForgeAspects, or a direct backend
        // registration). If none exists yet the inner store is resolved at provider-build time,
        // making AddForgeAuthorization() order-independent relative to backend/aspect registration.
        var rawDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(ITransactionalEntityStore) && d.ServiceKey is null);
        if (rawDescriptor is not null)
            services.Remove(rawDescriptor);

        services.AddSingleton<ITransactionalEntityStore>(sp =>
        {
            ITransactionalEntityStore inner;

            if (rawDescriptor is not null)
            {
                // Descriptor captured at registration time — resolve it directly.
                inner = rawDescriptor switch
                {
                    { ImplementationInstance: ITransactionalEntityStore inst } => inst,
                    { ImplementationFactory: { } f } => (ITransactionalEntityStore)f(sp),
                    { ImplementationType: { } t } =>
                        (ITransactionalEntityStore)ActivatorUtilities.CreateInstance(sp, t),
                    _ => throw new InvalidOperationException(
                        "Unexpected ITransactionalEntityStore service descriptor shape.")
                };
            }
            else
            {
                // No ITransactionalEntityStore was registered before AddForgeAuthorization() —
                // resolve at provider-build time so that call order does not matter.
                // Prefer the outermost completed decorator so the stack is always:
                //   Guard → EventEmitting → AspectEnforcing → Backend  (ADR-0021)
                inner =
                    sp.GetKeyedService<ITransactionalEntityStore>(
                        ForgeEntityRepositoryBuilder.EventsTxKey)
                    // Entity events not used — prefer the aspect-validating store (ADR-0014).
                    ?? sp.GetKeyedService<ITransactionalEntityStore>(
                        ForgeEntityRepositoryBuilder.AspectsTxKey)
                    // Aspects not used — fall back to the raw backend store directly.
                    ?? (sp.GetKeyedService<IEntityStore>(
                            ForgeEntityRepositoryBuilder.BackendStoreKey)
                        is ITransactionalEntityStore backendTx ? backendTx : null)
                    ?? throw new InvalidOperationException(
                        "AddForgeAuthorization() requires an ITransactionalEntityStore to be " +
                        "available. Ensure UseInMemory() or UseGraphDb() is called via " +
                        "AddForgeEntityRepository() at any point before the host is built, " +
                        "or register an ITransactionalEntityStore directly before the host is built.");
            }

            // Guard check — applies to all host types. HTTP hosts additionally get
            // AllowAllGuardStartupFilter which fires slightly earlier and provides a richer
            // error context. This factory-time check covers generic-host / background-service
            // scenarios where no IStartupFilter is run.
            // Check is conditioned on AuthorizationOptions being explicitly configured so that
            // test hosts using a plain ServiceCollection without configuration binding do not
            // accidentally fail when they have not opted into policy enforcement.
            var authOpts = sp.GetService<IOptions<AuthorizationOptions>>();
            if (authOpts is not null && authOpts.Value.RequireExplicitGuard &&
                effectiveGuard is AllowAllAspectGuard)
                throw new InvalidOperationException(
                    "Forge Authorization: 'Forge:Authorization:RequireExplicitGuard' is true " +
                    "but no explicit IAspectGuard has been registered — " +
                    "AllowAllAspectGuard permits every operation unconditionally. " +
                    "Either supply a real guard via AddForgeAuthorization(yourGuard) " +
                    "or set 'Forge:Authorization:RequireExplicitGuard' to false " +
                    "in your environment configuration (e.g. appsettings.Development.json).");

            return new GuardedTransactionalStore(inner, effectiveGuard);
        });

        return services;
    }
}
