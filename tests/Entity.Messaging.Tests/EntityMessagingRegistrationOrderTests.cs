using Forge.Aspects.DependencyInjection;
using Forge.Entity.Messaging;
using Forge.Entity.Messaging.DependencyInjection;
using Forge.Messaging.InMemory.DependencyInjection;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Entity.Messaging.Tests;

/// <summary>
/// Verifies that <see cref="EntityMessagingServiceCollectionExtensions.AddForgeEntityEvents"/>
/// is order-independent relative to <c>AddForgeAspects()</c> as required by Entity.Messaging ADR-0001.
///
/// The unkeyed <see cref="ITransactionalEntityStore"/> must resolve to
/// <see cref="EventEmittingTransactionalStore"/> regardless of whether
/// <c>AddForgeAspects()</c> or <c>AddForgeEntityEvents()</c> is called first.
/// </summary>
public sealed class EntityMessagingRegistrationOrderTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeMessagingInMemory();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ── order-independence tests ──────────────────────────────────────────────

    [Fact]
    public async Task Unkeyed_store_is_EventEmitting_when_Aspects_registered_before_EntityEvents()
    {
        await using var sp = BuildProvider(services =>
        {
            services.AddForgeAspects();      // first
            services.AddForgeEntityEvents(); // second
        });

        var store = sp.GetRequiredService<ITransactionalEntityStore>();
        store.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    [Fact]
    public async Task Unkeyed_store_is_EventEmitting_when_EntityEvents_registered_before_Aspects()
    {
        await using var sp = BuildProvider(services =>
        {
            services.AddForgeEntityEvents(); // first
            services.AddForgeAspects();      // second
        });

        var store = sp.GetRequiredService<ITransactionalEntityStore>();
        store.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    [Fact]
    public async Task Unkeyed_store_is_EventEmitting_when_only_EntityEvents_registered()
    {
        await using var sp = BuildProvider(services =>
        {
            services.AddForgeEntityEvents();
        });

        var store = sp.GetRequiredService<ITransactionalEntityStore>();
        store.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    [Fact]
    public async Task EventsTxKey_store_is_always_EventEmitting_regardless_of_order()
    {
        await using var sp = BuildProvider(services =>
        {
            services.AddForgeAspects();
            services.AddForgeEntityEvents();
        });

        var store = sp.GetRequiredKeyedService<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.EventsTxKey);
        store.ShouldBeOfType<EventEmittingTransactionalStore>();
    }
}
