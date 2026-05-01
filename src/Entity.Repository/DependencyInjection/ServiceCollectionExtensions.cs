using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Entity.Repository.DependencyInjection;

/// <summary>
/// DI extensions for the Repository slice. Each backend (InMemory, GraphDb) registers its own
/// <see cref="IEntityStore"/>; this extension wires the type-agnostic services
/// (<see cref="IRdfMapperRegistry"/>, <see cref="IEntityRepository{T}"/> open generic).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configuration section path used by <see cref="AddForgeEntityRepository"/>:
    /// <c>Forge:EntityRepository</c>.
    /// </summary>
    public const string ConfigurationSection = "Forge:EntityRepository";

    /// <summary>
    /// Bind <see cref="EntityRepositoryOptions"/> from <paramref name="configuration"/> and
    /// register the type-agnostic Repository services. Returns a builder so backends
    /// (InMemory / GraphDb) can be opted in by either calling
    /// <see cref="ForgeEntityRepositoryBuilder.UseInMemory"/> /
    /// <see cref="ForgeEntityRepositoryBuilder.UseGraphDb"/> directly or letting
    /// <see cref="ForgeEntityRepositoryBuilder.UseFromConfiguration"/> dispatch on
    /// <see cref="EntityRepositoryOptions.Backend"/>.
    /// </summary>
    public static ForgeEntityRepositoryBuilder AddForgeEntityRepository(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
            services.Configure<EntityRepositoryOptions>(configuration.GetSection(ConfigurationSection));

        services.TryAddSingleton<IRdfMapperRegistry>(_ => new RdfMapperRegistry());
        services.TryAdd(ServiceDescriptor.Singleton(typeof(IEntityRepository<>), typeof(EntityRepository<>)));

        return new ForgeEntityRepositoryBuilder(services, configuration);
    }

    /// <summary>
    /// Eagerly register an <see cref="IRdfMapper{T}"/> for a known entity type. Optional —
    /// the registry constructs <see cref="ReflectionRdfMapper{T}"/> lazily on first use.
    /// </summary>
    public static ForgeEntityRepositoryBuilder RegisterEntity<T>(this ForgeEntityRepositoryBuilder builder)
        where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IRdfMapper>(_ => new ReflectionRdfMapper<T>());
        builder.Services.AddSingleton<IRdfMapper<T>>(sp =>
            (IRdfMapper<T>)sp.GetRequiredService<IRdfMapperRegistry>().For<T>());
        return builder;
    }
}

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddForgeEntityRepository"/>.
/// Backend packages add extension methods (<c>UseInMemory</c>, <c>UseGraphDb</c>).
/// </summary>
public sealed class ForgeEntityRepositoryBuilder
{
    public IServiceCollection Services { get; }
    public IConfiguration? Configuration { get; }

    public ForgeEntityRepositoryBuilder(IServiceCollection services, IConfiguration? configuration)
    {
        Services = services;
        Configuration = configuration;
    }
}
