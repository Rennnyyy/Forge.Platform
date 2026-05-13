using Forge.ObjectStorage;
using Forge.ObjectStorage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.ObjectStorage.InMemory.DependencyInjection;

/// <summary>
/// DI extensions for the InMemory object-storage implementation.
/// See root ADR-0023 and <c>Forge.ObjectStorage.Abstractions</c> ADR-0001.
/// </summary>
public static class ForgeObjectStorageInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="InMemoryObjectStoreProvider"/> as a singleton and
    /// exposes it as both <see cref="InMemoryObjectStoreProvider"/> (concrete) and
    /// <see cref="IObjectStoreProvider"/> (abstraction).
    /// <para>
    /// Uses <c>TryAdd</c> semantics — existing registrations are not overwritten.
    /// </para>
    /// </summary>
    public static IServiceCollection AddForgeObjectStorageInMemory(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryObjectStoreProvider>();
        services.TryAddSingleton<IObjectStoreProvider>(
            sp => sp.GetRequiredService<InMemoryObjectStoreProvider>());

        return services;
    }
}
