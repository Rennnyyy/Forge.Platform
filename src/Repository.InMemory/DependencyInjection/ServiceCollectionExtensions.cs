using Forge.Entity;
using Forge.Repository.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        builder.Services.TryAddSingleton<IEntityStore>(sp => sp.GetRequiredService<InMemoryEntityStore>());
        return builder;
    }
}
