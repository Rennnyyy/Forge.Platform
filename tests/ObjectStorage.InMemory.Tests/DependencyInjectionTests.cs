using Forge.ObjectStorage;
using Forge.ObjectStorage.InMemory;
using Forge.ObjectStorage.InMemory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.ObjectStorage.InMemory.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddForgeObjectStorageInMemory_registers_IObjectStoreProvider()
    {
        var services = new ServiceCollection();
        services.AddForgeObjectStorageInMemory();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IObjectStoreProvider>();

        store.ShouldNotBeNull();
        store.ShouldBeOfType<InMemoryObjectStoreProvider>();
    }

    [Fact]
    public void AddForgeObjectStorageInMemory_registers_InMemoryObjectStoreProvider_as_concrete_type()
    {
        var services = new ServiceCollection();
        services.AddForgeObjectStorageInMemory();

        var provider = services.BuildServiceProvider();
        var concrete = provider.GetService<InMemoryObjectStoreProvider>();

        concrete.ShouldNotBeNull();
    }

    [Fact]
    public void IObjectStoreProvider_and_concrete_resolve_to_same_singleton_instance()
    {
        var services = new ServiceCollection();
        services.AddForgeObjectStorageInMemory();

        var provider = services.BuildServiceProvider();
        var abstraction = provider.GetRequiredService<IObjectStoreProvider>();
        var concrete = provider.GetRequiredService<InMemoryObjectStoreProvider>();

        abstraction.ShouldBeSameAs(concrete);
    }

    [Fact]
    public void AddForgeObjectStorageInMemory_uses_TryAdd_semantics()
    {
        var services = new ServiceCollection();
        var customProvider = new InMemoryObjectStoreProvider();
        services.AddSingleton<IObjectStoreProvider>(customProvider);

        services.AddForgeObjectStorageInMemory();

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IObjectStoreProvider>();

        resolved.ShouldBeSameAs(customProvider);
    }
}
