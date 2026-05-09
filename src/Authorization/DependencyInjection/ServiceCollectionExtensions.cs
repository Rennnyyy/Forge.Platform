using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// Must be called <em>after</em> a backend (e.g. <c>UseInMemory()</c>) has been
    /// registered, so that the <see cref="ITransactionalEntityStore"/> descriptor is
    /// already present in the collection.
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
                // Prefer the aspect-validating store (AddForgeAspects' keyed registration) so the
                // decorator stack is always: Guard → AspectEnforcing → Backend.
                inner =
                    sp.GetKeyedService<ITransactionalEntityStore>(
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

            return new GuardedTransactionalStore(inner, effectiveGuard);
        });

        return services;
    }
}
