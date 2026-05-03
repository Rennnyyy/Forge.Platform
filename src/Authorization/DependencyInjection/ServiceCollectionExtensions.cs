using Forge.Repository;
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
    /// The guard to use. When <see langword="null"/>, <see cref="AllowAllOperationGuard.Instance"/>
    /// is used — making this registration safe to include unconditionally in any host.
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

        var rawDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITransactionalEntityStore));
        if (rawDescriptor is null)
            return services;

        services.Remove(rawDescriptor);

        if (rawDescriptor.ImplementationInstance is ITransactionalEntityStore existingInstance)
        {
            services.AddSingleton<ITransactionalEntityStore>(
                new GuardedTransactionalStore(existingInstance, effectiveGuard));
        }
        else
        {
            services.AddSingleton<ITransactionalEntityStore>(sp =>
            {
                var inner = rawDescriptor.ImplementationFactory is not null
                    ? (ITransactionalEntityStore)rawDescriptor.ImplementationFactory(sp)
                    : (ITransactionalEntityStore)ActivatorUtilities.CreateInstance(
                        sp, rawDescriptor.ImplementationType!);
                return new GuardedTransactionalStore(inner, effectiveGuard);
            });
        }

        return services;
    }
}
