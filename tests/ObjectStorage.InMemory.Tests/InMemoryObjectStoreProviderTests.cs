using Forge.ObjectStorage;
using Forge.ObjectStorage.InMemory;
using Shouldly;

namespace Forge.ObjectStorage.InMemory.Tests;

public sealed class InMemoryObjectStoreProviderTests
{
    // ── GetStore identity ─────────────────────────────────────────────────────

    [Fact]
    public void GetStore_returns_same_instance_for_same_key()
    {
        var provider = new InMemoryObjectStoreProvider();

        var s1 = provider.GetStore("media");
        var s2 = provider.GetStore("media");

        s1.ShouldBeSameAs(s2);
    }

    [Fact]
    public void GetStore_returns_different_instances_for_different_keys()
    {
        var provider = new InMemoryObjectStoreProvider();

        var sA = provider.GetStore("store-a");
        var sB = provider.GetStore("store-b");

        sA.ShouldNotBeSameAs(sB);
    }

    // ── Store isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Stores_resolved_by_different_keys_are_isolated()
    {
        var provider = new InMemoryObjectStoreProvider();
        var storeA = provider.GetStore("a");
        var storeB = provider.GetStore("b");

        await storeA.UploadAsync(
            "key",
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("in-a")),
            "text/plain");

        (await storeB.ExistsAsync("key")).ShouldBeFalse();
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_causes_new_GetStore_to_return_a_fresh_empty_store()
    {
        var provider = new InMemoryObjectStoreProvider();
        var original = provider.GetStore("media");
        await original.UploadAsync(
            "file",
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data")),
            "text/plain");

        provider.Reset();

        var afterReset = provider.GetStore("media");
        afterReset.ShouldNotBeSameAs(original);
        (await afterReset.ExistsAsync("file")).ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_does_not_affect_already_resolved_store_instances()
    {
        var provider = new InMemoryObjectStoreProvider();
        var held = provider.GetStore("media");
        await held.UploadAsync(
            "held-file",
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("held")),
            "text/plain");

        provider.Reset();

        // The previously resolved reference is untouched
        (await held.ExistsAsync("held-file")).ShouldBeTrue();
    }

    // ── Return type ───────────────────────────────────────────────────────────

    [Fact]
    public void GetStore_returns_IObjectStore()
    {
        var provider = new InMemoryObjectStoreProvider();
        var store = provider.GetStore("x");
        (store is IObjectStore).ShouldBeTrue();
    }
}
