using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.DependencyInjection;
using Forge.Entity;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Tests 5–6: startup validation via the DI extension.
/// </summary>
[Collection("EntityOptions")]
public sealed class AspectsStartupTests : IClassFixture<EntityOptionsFixture>
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: AddForgeAspects() throws when the bound store does not implement ISparqlQueryStore
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddForgeAspects_throws_InvalidOperationException_when_store_is_not_ISparqlQueryStore()
    {
        // Register a stub IEntityStore that does NOT implement ISparqlQueryStore.
        var stubStore = Substitute.For<IEntityStore, ITransactionalEntityStore>();

        var services = new ServiceCollection();
        services.AddSingleton<IEntityStore>(stubStore);
        services.AddForgeAspects();

        await using var sp = services.BuildServiceProvider();

        // The decorated ITransactionalEntityStore is resolved lazily — first resolution triggers fail-fast.
        var ex = Should.Throw<InvalidOperationException>(
            () => sp.GetRequiredService<ITransactionalEntityStore>());

        ex.Message.ShouldContain("ISparqlQueryStore");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: Malformed code-origin TTL fails at startup with AspectTtlParseException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Malformed_TTL_throws_AspectTtlParseException_at_startup()
    {
        // Write a temp file with invalid Turtle.
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "THIS IS NOT VALID TURTLE @@@@");
        try
        {
            var services = new ServiceCollection();

            // Register options so the engine wires up correctly.
            services.Configure<Forge.Repository.EntityRepositoryOptions>(_ => { });
            services.AddForgeEntityRepository()
                    .UseInMemory();

            services.AddOperationAspect(tempFile, "https://forge-it.net/aspects/test/malformed-aspect");
            services.AddForgeAspects();

            using var sp = services.BuildServiceProvider();

            // Resolving the decorated store triggers the pending registrations which parse the TTL.
            var ex = Should.Throw<AspectTtlParseException>(
                () => sp.GetRequiredService<ITransactionalEntityStore>());

            ex.TtlExcerpt.ShouldNotBeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests 7–8: registration order independence (Fix #1)
    // AddForgeAspects() must wire correctly even when UseInMemory() is called
    // *after* AddForgeAspects(), i.e. the backend is registered late.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddForgeAspects_before_UseInMemory_still_resolves_correct_decorator_stack()
    {
        var services = new ServiceCollection();
        services.Configure<Forge.Repository.EntityRepositoryOptions>(_ => { });

        // Intentionally call AddForgeAspects BEFORE UseInMemory.
        services.AddForgeAspects();
        services.AddForgeEntityRepository().UseInMemory();

        await using var sp = services.BuildServiceProvider();

        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        txStore.ShouldNotBeNull();

        // The ambient IEntityStore must be an AspectEnforcingEntityStore, not the bare backend.
        var entityStore = sp.GetRequiredService<IEntityStore>();
        entityStore.GetType().Name.ShouldBe("AspectEnforcingEntityStore");

        // Basic operation must succeed end-to-end through the full stack.
        var artist = new Artist { Name = "Late-Wired Artist", Country = "de" };
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        var loaded = await txStore.LoadAsync<Artist>(artist.Iri);
        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Late-Wired Artist");
    }

    [Fact]
    public async Task UseInMemory_before_AddForgeAspects_produces_same_decorator_stack()
    {
        var services = new ServiceCollection();
        services.Configure<Forge.Repository.EntityRepositoryOptions>(_ => { });

        // Standard order: UseInMemory before AddForgeAspects.
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeAspects();

        await using var sp = services.BuildServiceProvider();

        var entityStore = sp.GetRequiredService<IEntityStore>();
        entityStore.GetType().Name.ShouldBe("AspectEnforcingEntityStore");

        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var artist = new Artist { Name = "Standard-Order Artist", Country = "fr" };
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        var loaded = await txStore.LoadAsync<Artist>(artist.Iri);
        loaded.ShouldNotBeNull();
    }
}
