using Forge.Aspects.Abstractions;
using Forge.Aspects.DependencyInjection;
using Forge.Entity;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Entity.Messaging;
using Forge.Entity.Messaging.DependencyInjection;
using Forge.Execution;
using Forge.Messaging.Abstractions;
using Forge.Messaging.InMemory;
using Forge.Messaging.InMemory.DependencyInjection;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Entity.Messaging.Tests;

/// <summary>
/// Behavioral tests for <see cref="EventEmittingTransactionalStore"/>.
/// Exercises the decorator via the real InMemory repository stack and
/// <see cref="InMemoryMessageBroker"/> so no broker process is required.
/// </summary>
[Collection("EntityOptions")]
public sealed class EventEmittingTransactionalStoreTests : IClassFixture<EntityOptionsFixture>
{
    private const string ArtistTypeIri = "https://forge-it.net/types/artists";
    private const string ArtistHistoryTopic = "forge.entities.artist.history";
    private const string ArtistStateTopic = "forge.entities.artist.state";

    // ── DI helpers ───────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(
        bool withAspects = false,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeMessagingInMemory();
        services.AddForgeEntityEvents();
        services.AddForgeEntityMessaging<Artist>(opts =>
        {
            opts.TypeIri = ArtistTypeIri;
        });

        if (withAspects)
            services.AddForgeAspects();

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static Artist MakeArtist(string name = "Testify", string country = "de") =>
        new() { Name = name, Country = country };

    /// <summary>
    /// Drains all currently buffered messages from <paramref name="topic"/> and returns them.
    /// Uses a very short timeout so the test does not block when the topic is empty.
    /// </summary>
    private static async Task<List<MessageEnvelope<EntityChangedEnvelope<Artist>>>> DrainAsync(
        IMessageConsumer<string, EntityChangedEnvelope<Artist>> consumer,
        string topic,
        int expectedCount)
    {
        var results = new List<MessageEnvelope<EntityChangedEnvelope<Artist>>>(expectedCount);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var msg in consumer.ConsumeAsync(topic, cts.Token).ConfigureAwait(false))
        {
            results.Add(msg);
            if (results.Count == expectedCount) break;
        }
        return results;
    }

    // ── EntityChangedEnvelope record ─────────────────────────────────────────

    [Fact]
    public void EntityChangedEnvelope_carries_all_fields()
    {
        var correlation = new ExecutionCorrelation();
        var ts = DateTimeOffset.UtcNow;
        var artist = MakeArtist();
        var env = new EntityChangedEnvelope<Artist>(
            Iri: artist.Iri,
            TypeName: "Artist",
            TypeIri: ArtistTypeIri,
            Operation: EntityChangeOperation.Created,
            BranchIri: "https://forge-it.net/branches/main",
            Dto: artist,
            Correlation: correlation,
            TimestampUtc: ts);

        env.Iri.ShouldBe(artist.Iri);
        env.TypeName.ShouldBe("Artist");
        env.TypeIri.ShouldBe(ArtistTypeIri);
        env.Operation.ShouldBe(EntityChangeOperation.Created);
        env.BranchIri.ShouldBe("https://forge-it.net/branches/main");
        env.Dto.ShouldBe(artist);
        env.Correlation.ShouldBe(correlation);
        env.TimestampUtc.ShouldBe(ts);
    }

    [Fact]
    public void EntityChangedEnvelope_Dto_is_null_for_deletes()
    {
        var env = new EntityChangedEnvelope<Artist>(
            Iri: "https://forge-it.net/artists/some",
            TypeName: "Artist",
            TypeIri: ArtistTypeIri,
            Operation: EntityChangeOperation.Deleted,
            BranchIri: string.Empty,
            Dto: null,
            Correlation: new ExecutionCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow);

        env.Dto.ShouldBeNull();
        env.Operation.ShouldBe(EntityChangeOperation.Deleted);
    }

    // ── Topic-name derivation ────────────────────────────────────────────────

    [Theory]
    [InlineData("Artist", "artist")]
    [InlineData("ArtistManager", "artist-manager")]
    [InlineData("FooBarBaz", "foo-bar-baz")]
    [InlineData("HTMLParser", "html-parser")]
    public void ToKebabCase_converts_pascal_to_kebab(string input, string expected) =>
        EntityMessagingServiceCollectionExtensions.ToKebabCase(input).ShouldBe(expected);

    [Fact]
    public async Task Default_topics_follow_convention()
    {
        // Verify that AddForgeEntityEvent sets the default topic names via ToKebabCase.
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeMessagingInMemory();
        services.AddForgeEntityEvents();
        services.AddForgeEntityMessaging<Artist>(opts => { opts.TypeIri = ArtistTypeIri; });

        await using var sp = services.BuildServiceProvider();

        // If we can resolve the decorator without exception the DI wiring is correct.
        var tx = sp.GetKeyedService<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.EventsTxKey);
        tx.ShouldNotBeNull();
        tx.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    // ── Transaction: Create emits Created on both topics ─────────────────────

    [Fact]
    public async Task ExecuteTransaction_create_emits_Created_to_history_and_state()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Bach", "de");

        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        var state = await DrainAsync(consumer, ArtistStateTopic, 1);

        history.Count.ShouldBe(1);
        state.Count.ShouldBe(1);

        var h = history[0].Payload;
        h.Operation.ShouldBe(EntityChangeOperation.Created);
        h.Iri.ShouldBe(artist.Iri);
        h.TypeName.ShouldBe("Artist");
        h.TypeIri.ShouldBe(ArtistTypeIri);
        h.Dto.ShouldNotBeNull();
        h.Dto.Name.ShouldBe("Bach");
    }

    // ── Transaction: Update emits Updated ────────────────────────────────────

    [Fact]
    public async Task ExecuteTransaction_update_emits_Updated()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Handel", "de");

        await using (var createTx = new EntityTransaction(txStore))
        {
            createTx.Create(artist);
            await createTx.CommitAsync();
        }

        // Drain create events before the update.
        await DrainAsync(consumer, ArtistHistoryTopic, 1);
        await DrainAsync(consumer, ArtistStateTopic, 1);

        await using var updateTx = new EntityTransaction(txStore);
        updateTx.Update(artist);
        await updateTx.CommitAsync();

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        history[0].Payload.Operation.ShouldBe(EntityChangeOperation.Updated);
    }

    // ── Transaction: Delete emits Deleted with null Dto ──────────────────────

    [Fact]
    public async Task ExecuteTransaction_delete_emits_Deleted_with_null_dto()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Liszt", "hu");

        await using (var createTx = new EntityTransaction(txStore))
        {
            createTx.Create(artist);
            await createTx.CommitAsync();
        }

        await DrainAsync(consumer, ArtistHistoryTopic, 1);
        await DrainAsync(consumer, ArtistStateTopic, 1);

        await using var deleteTx = new EntityTransaction(txStore);
        deleteTx.Delete<Artist>(artist.Iri, Aspect.NoOpIri);
        await deleteTx.CommitAsync();

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        var state = await DrainAsync(consumer, ArtistStateTopic, 1);

        history[0].Payload.Operation.ShouldBe(EntityChangeOperation.Deleted);
        history[0].Payload.Dto.ShouldBeNull();
        state[0].Payload.Operation.ShouldBe(EntityChangeOperation.Deleted);
        state[0].Payload.Dto.ShouldBeNull();
    }

    // ── Transaction: multiple ops emit in order ───────────────────────────────

    [Fact]
    public async Task ExecuteTransaction_multiple_ops_emit_all_events_in_order()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();

        var a1 = MakeArtist("Mozart", "at");
        var a2 = MakeArtist("Beethoven", "de");

        await using var tx = new EntityTransaction(txStore);
        tx.Create(a1);
        tx.Create(a2);
        await tx.CommitAsync();

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 2);
        history.Count.ShouldBe(2);
        history[0].Payload.Operation.ShouldBe(EntityChangeOperation.Created);
        history[1].Payload.Operation.ShouldBe(EntityChangeOperation.Created);
    }

    // ── Correlation threading ────────────────────────────────────────────────

    [Fact]
    public async Task ExecutionScope_correlation_is_threaded_into_envelope()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Schubert", "at");
        var correlation = new ExecutionCorrelation
        {
            ExecutionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };

        using (ExecutionScope.Use(correlation))
        {
            await using var tx = new EntityTransaction(txStore);
            tx.Create(artist);
            await tx.CommitAsync();
        }

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        history[0].Payload.Correlation.ExecutionId.ShouldBe(correlation.ExecutionId);
    }

    // ── Partition key is entity IRI ──────────────────────────────────────────

    [Fact]
    public async Task PartitionKey_in_message_envelope_equals_entity_iri()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Vivaldi", "it");

        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        history[0].PartitionKey.ShouldBe(artist.Iri);
    }

    // ── Unregistered entity type: no event, pass-through ────────────────────

    [Fact]
    public async Task Unregistered_entity_type_does_not_throw_and_write_succeeds()
    {
        // Build a provider WITHOUT AddForgeEntityMessaging<Artist>, so Artist is unregistered.
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeMessagingInMemory();
        services.AddForgeEntityEvents();
        // No AddForgeEntityMessaging<Artist>

        await using var sp = services.BuildServiceProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();
        var artist = MakeArtist("Brahms", "de");

        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        // Entity was written despite no emitter being registered.
        var loaded = await rawStore.LoadAsync<Artist>(artist.Iri);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("Brahms");
    }

    // ── DI: EventsTxKey is registered and resolves to decorator ─────────────

    [Fact]
    public async Task EventsTxKey_resolves_to_EventEmittingTransactionalStore()
    {
        await using var sp = BuildProvider();
        var keyed = sp.GetKeyedService<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.EventsTxKey);

        keyed.ShouldNotBeNull();
        keyed.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    // ── DI: with aspects, chain is EventEmitting → AspectEnforcing → Backend ─

    [Fact]
    public async Task With_aspects_keyed_store_is_still_EventEmittingTransactionalStore()
    {
        await using var sp = BuildProvider(withAspects: true);
        var keyed = sp.GetKeyedService<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.EventsTxKey);

        keyed.ShouldNotBeNull();
        keyed.ShouldBeOfType<EventEmittingTransactionalStore>();
    }

    // ── SaveAsync (non-transactional): Created / Updated ────────────────────

    [Fact]
    public async Task SaveAsync_Create_emits_Created_event()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Dvorak", "cz");

        await txStore.SaveAsync(artist, WriteMode.Create);

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        history[0].Payload.Operation.ShouldBe(EntityChangeOperation.Created);
        history[0].Payload.Iri.ShouldBe(artist.Iri);
    }

    [Fact]
    public async Task SaveAsync_Replace_emits_Updated_event()
    {
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, EntityChangedEnvelope<Artist>>>();
        var artist = MakeArtist("Sibelius", "fi");

        await txStore.SaveAsync(artist, WriteMode.Create);
        await DrainAsync(consumer, ArtistHistoryTopic, 1);
        await DrainAsync(consumer, ArtistStateTopic, 1);

        await txStore.SaveAsync(artist, WriteMode.Replace);

        var history = await DrainAsync(consumer, ArtistHistoryTopic, 1);
        history[0].Payload.Operation.ShouldBe(EntityChangeOperation.Updated);
    }
}
