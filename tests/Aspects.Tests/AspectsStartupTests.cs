using Forge.Entity;
using Forge.Aspects;
using Forge.Aspects.DependencyInjection;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
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
    public void AddForgeAspects_throws_InvalidOperationException_when_store_is_not_ISparqlQueryStore()
    {
        // Register a stub IEntityStore that does NOT implement ISparqlQueryStore.
        var stubStore = Substitute.For<IEntityStore, ITransactionalEntityStore>();

        var services = new ServiceCollection();
        services.AddSingleton<IEntityStore>(stubStore);
        services.AddForgeAspects();

        using var sp = services.BuildServiceProvider();

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
}
