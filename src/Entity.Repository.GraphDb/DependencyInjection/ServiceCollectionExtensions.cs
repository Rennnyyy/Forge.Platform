using Forge.Entity.Repository.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Entity.Repository.GraphDb.DependencyInjection;

/// <summary>DI extensions for the Ontotext GraphDB backend.</summary>
public static class ServiceCollectionExtensions
{
    public const string ConfigurationSection = "Forge:GraphDb";

    public static ForgeEntityRepositoryBuilder UseGraphDb(
        this ForgeEntityRepositoryBuilder builder,
        Action<GraphDbOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration is { } config)
            builder.Services.Configure<GraphDbOptions>(config.GetSection(ConfigurationSection));
        if (configure is not null)
            builder.Services.PostConfigure(configure);

        builder.Services.AddHttpClient<GraphDbEntityStore>();
        builder.Services.TryAddSingleton<IEntityStore>(sp => sp.GetRequiredService<GraphDbEntityStore>());
        return builder;
    }
}
