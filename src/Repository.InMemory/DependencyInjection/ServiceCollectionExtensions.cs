using Forge.Entity;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Forge.Repository.InMemory.DependencyInjection;

/// <summary>DI extensions for the InMemory backend.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="InMemoryEntityStore"/> as the active <see cref="IEntityStore"/>.
    /// Idempotent — calling twice is a no-op.
    /// </summary>
    public static ForgeEntityRepositoryBuilder UseInMemory(this ForgeEntityRepositoryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<InMemoryEntityStore>();
        // Register under the well-known backend key so aspect/auth decorators can resolve
        // the raw store at provider-build time regardless of call order.
        builder.Services.TryAddKeyedSingleton<IEntityStore>(
            ForgeEntityRepositoryBuilder.BackendStoreKey,
            (sp, _) => sp.GetRequiredService<InMemoryEntityStore>());
        builder.Services.TryAddSingleton<IEntityStore>(sp => sp.GetRequiredService<InMemoryEntityStore>());
        builder.Services.TryAddSingleton<EntityStoreFactory>(sp =>
        {
            var registry = sp.GetRequiredService<IRdfMapperRegistry>();
            return (EntityStoreFactory)((opts) =>
                new InMemoryEntityStore(registry, new OptionsWrapper<EntityRepositoryOptions>(opts)));
        });
        return builder;
    }
}
