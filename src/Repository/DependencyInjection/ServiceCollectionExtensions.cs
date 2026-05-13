using Forge.Entity;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Repository.DependencyInjection;

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
/// Factory delegate that backends (<c>UseInMemory</c>, <c>UseGraphDb</c>) register so that
/// <c>Forge.Branch.AddForgeBranch()</c> can create the management graph store without knowing
/// the concrete backend type. The delegate receives the desired <see cref="EntityRepositoryOptions"/>
/// (typically with <c>NamedGraph</c> set to the management graph IRI) and returns a fully
/// functional, independently configured transactional store.
/// </summary>
public delegate ITransactionalEntityStore EntityStoreFactory(EntityRepositoryOptions options);

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddForgeEntityRepository"/>.
/// Backend packages add extension methods (<c>UseInMemory</c>, <c>UseGraphDb</c>).
/// </summary>
public sealed class ForgeEntityRepositoryBuilder
{
    /// <summary>
    /// Keyed-service key under which <c>UseInMemory()</c> and <c>UseGraphDb()</c> register the
    /// raw backend <see cref="IEntityStore"/> <em>before</em> any decorator is applied.
    /// <see cref="Forge.Aspects.DependencyInjection.AspectsServiceCollectionExtensions.AddForgeAspects"/>
    /// and
    /// <see cref="Forge.Authorization.DependencyInjection.AuthorizationServiceCollectionExtensions.AddForgeAuthorization"/>
    /// resolve from this key at provider-build time, making them order-independent relative to the
    /// backend registration.
    /// </summary>
    public const string BackendStoreKey = "forge.repository.backend";

    /// <summary>
    /// Keyed-service key under which
    /// <see cref="Forge.Aspects.DependencyInjection.AspectsServiceCollectionExtensions.AddForgeAspects"/>
    /// registers the <see cref="Forge.Repository.Transaction.ITransactionalEntityStore"/> that has
    /// already been wrapped with SHACL/SPARQL aspect validation.
    /// <see cref="Forge.Authorization.DependencyInjection.AuthorizationServiceCollectionExtensions.AddForgeAuthorization"/>
    /// resolves from this key (if present) so the guard runs <em>outside</em> aspect validation
    /// regardless of the order in which the two methods are called.
    /// </summary>
    public const string AspectsTxKey = "forge.aspects.tx";

    /// <summary>
    /// Keyed-service key under which
    /// <c>Forge.Entity.Messaging.DependencyInjection.EntityMessagingServiceCollectionExtensions.AddForgeEntityMessaging</c>
    /// registers the <see cref="Forge.Repository.Transaction.ITransactionalEntityStore"/> that has
    /// been wrapped with entity-change event emission.
    /// <see cref="Forge.Authorization.DependencyInjection.AuthorizationServiceCollectionExtensions.AddForgeAuthorization"/>
    /// resolves from this key (if present) so the guard remains the outermost decorator.
    /// Full chain: Guard → EventEmitting → AspectEnforcing → Backend.
    /// See root ADR-0021.
    /// </summary>
    public const string EventsTxKey = "forge.events.tx";

    public IServiceCollection Services { get; }
    public IConfiguration? Configuration { get; }

    public ForgeEntityRepositoryBuilder(IServiceCollection services, IConfiguration? configuration)
    {
        Services = services;
        Configuration = configuration;
    }
}
