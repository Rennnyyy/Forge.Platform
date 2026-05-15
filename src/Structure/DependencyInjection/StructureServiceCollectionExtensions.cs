using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Structure.DependencyInjection;

/// <summary>
/// DI extensions for the Variant slice. See Variant ADR-0004.
/// </summary>
public static class StructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="StructureFilteringStore"/> decorator on the unkeyed
    /// <see cref="IEntityStore"/>. When <see cref="StructureScope.Current"/> is active,
    /// <see cref="IEntityStore.QueryByTypeAsync{T}"/> for <see cref="Usage"/> entities
    /// will transparently return only the satisfied edges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the correct decorator chain (VariantFiltering → AspectEnforcing → Backend),
    /// call <c>AddForgeStructure()</c> <em>after</em> any call to <c>AddForgeAspects()</c>.
    /// Calling it before <c>AddForgeAspects()</c> still produces correct filtering results
    /// but inverts the layer ordering (AspectEnforcing → VariantFiltering → Backend).
    /// </para>
    /// <para>
    /// May be called in any order relative to the backend (<c>UseInMemory()</c> /
    /// <c>UseGraphDb()</c>). When no unkeyed <see cref="IEntityStore"/> has been registered
    /// at call time, the decorator falls back to resolving
    /// <see cref="ForgeEntityRepositoryBuilder.BackendStoreKey"/> at provider-build time.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddForgeStructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Capture the current unkeyed IEntityStore at registration time (e.g. AspectEnforcingEntityStore
        // if AddForgeAspects() was called first, or the raw backend if called directly).
        var existingDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IEntityStore) && d.ServiceKey is null);

        if (existingDescriptor is not null)
            services.Remove(existingDescriptor);

        services.AddSingleton<IEntityStore>(sp =>
        {
            IEntityStore inner;

            if (existingDescriptor is not null)
            {
                inner = ResolveFromDescriptor(existingDescriptor, sp);
            }
            else
            {
                // No unkeyed IEntityStore at registration time; fall back to the well-known
                // backend key registered by UseInMemory() / UseGraphDb().
                inner = sp.GetKeyedService<IEntityStore>(ForgeEntityRepositoryBuilder.BackendStoreKey)
                    ?? throw new InvalidOperationException(
                        "AddForgeStructure() requires an IEntityStore backend. " +
                        "Either call UseInMemory() or UseGraphDb() via AddForgeEntityRepository() " +
                        "(at any point before the host is built), or register an IEntityStore " +
                        "directly before calling AddForgeStructure().");
            }

            return new StructureFilteringStore(inner);
        });

        return services;
    }

    private static IEntityStore ResolveFromDescriptor(ServiceDescriptor d, IServiceProvider sp) => d switch
    {
        { ImplementationFactory: { } f } => (IEntityStore)f(sp),
        { ImplementationInstance: { } i } => (IEntityStore)i,
        { ImplementationType: { } t } => (IEntityStore)ActivatorUtilities.CreateInstance(sp, t),
        _ => throw new InvalidOperationException("Unexpected IEntityStore service descriptor shape.")
    };
}
