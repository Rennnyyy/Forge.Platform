using Forge.Entity;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Forge.Repository.GraphDb.DependencyInjection;

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

        builder.Services.AddTransient<GraphDbAuthHandler>();
        builder.Services.AddHttpClient<GraphDbEntityStore>()
            .AddHttpMessageHandler<GraphDbAuthHandler>();
        // Register under the well-known backend key so aspect/auth decorators can resolve
        // the raw store at provider-build time regardless of call order.
        builder.Services.TryAddKeyedSingleton<IEntityStore>(
            ForgeEntityRepositoryBuilder.BackendStoreKey,
            (sp, _) => sp.GetRequiredService<GraphDbEntityStore>());
        builder.Services.TryAddSingleton<IEntityStore>(sp => sp.GetRequiredService<GraphDbEntityStore>());
        builder.Services.TryAddSingleton<EntityStoreFactory>(sp =>
        {
            var registry = sp.GetRequiredService<IRdfMapperRegistry>();
            var gdbOpts = sp.GetRequiredService<IOptions<GraphDbOptions>>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return (EntityStoreFactory)((opts) =>
            {
                var http = httpClientFactory.CreateClient(nameof(GraphDbEntityStore));
                return new GraphDbEntityStore(http, registry,
                    new OptionsWrapper<EntityRepositoryOptions>(opts), gdbOpts);
            });
        });
        return builder;
    }
}
