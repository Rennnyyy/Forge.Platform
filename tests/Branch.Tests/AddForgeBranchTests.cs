using Forge.Branch.DependencyInjection;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Tests for <see cref="BranchServiceCollectionExtensions.AddForgeBranch"/>.
/// </summary>
public sealed class AddForgeBranchTests
{
    private static IConfiguration BuildConfig(
        string? defaultBranchIri = null,
        string? managementGraphIri = null)
    {
        var dict = new Dictionary<string, string?>();
        if (defaultBranchIri is not null)
            dict["Forge:Branch:DefaultBranchIri"] = defaultBranchIri;
        if (managementGraphIri is not null)
            dict["Forge:Branch:ManagementGraphIri"] = managementGraphIri;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration? config = null)
    {
        var services = new ServiceCollection();
        var cfg = config ?? BuildConfig();
        var repoBuilder = services.AddForgeEntityRepository(cfg);
        repoBuilder.UseInMemory();
        services.AddForgeBranch(cfg);
        return services.BuildServiceProvider();
    }

    // Async helper for tests that need to resolve the management store (which is async-disposable).
    private static async Task RunWithProviderAsync(
        Func<IServiceProvider, Task> action,
        IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        var services = new ServiceCollection();
        services.AddForgeEntityRepository(cfg).UseInMemory();
        services.AddForgeBranch(cfg);
        await using var provider = services.BuildServiceProvider();
        await action(provider);
    }

    // ── BranchDefault ─────────────────────────────────────────────────────────

    [Fact]
    public void AddForgeBranch_sets_BranchDefault_to_configured_value()
    {
        var cfg = BuildConfig(defaultBranchIri: "https://example.com/branches/custom");
        _ = BuildProvider(cfg);

        BranchDefault.BranchIri.ShouldBe("https://example.com/branches/custom");
    }

    [Fact]
    public void AddForgeBranch_sets_BranchDefault_to_default_when_not_configured()
    {
        _ = BuildProvider(BuildConfig());

        BranchDefault.BranchIri.ShouldBe("https://forge-it.net/branches/main");
    }

    // ── Management store registration ─────────────────────────────────────────

    [Fact]
    public async Task AddForgeBranch_registers_keyed_ITransactionalEntityStore_for_management()
    {
        await RunWithProviderAsync(sp =>
        {
            var store = sp.GetKeyedService<ITransactionalEntityStore>("forge.branch.management");
            store.ShouldNotBeNull();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AddForgeBranch_registers_keyed_IEntityStore_for_management()
    {
        await RunWithProviderAsync(sp =>
        {
            var store = sp.GetKeyedService<IEntityStore>("forge.branch.management");
            store.ShouldNotBeNull();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Management_store_NamedGraph_equals_configured_ManagementGraphIri()
    {
        var cfg = BuildConfig(managementGraphIri: "https://example.com/management");
        await RunWithProviderAsync(sp =>
        {
            var store = sp.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
            store.NamedGraph.ShouldBe("https://example.com/management");
            return Task.CompletedTask;
        }, cfg);
    }

    [Fact]
    public async Task Management_store_NamedGraph_defaults_to_forge_management()
    {
        await RunWithProviderAsync(sp =>
        {
            var store = sp.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
            store.NamedGraph.ShouldBe("https://forge-it.net/management");
            return Task.CompletedTask;
        });
    }

    // ── EntityRepositoryOptions sync ─────────────────────────────────────────

    [Fact]
    public async Task AddForgeBranch_syncs_DefaultBranchIri_into_EntityRepositoryOptions()
    {
        var cfg = BuildConfig(defaultBranchIri: "https://example.com/branches/main");
        await RunWithProviderAsync(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntityRepositoryOptions>>().Value;
            opts.DefaultBranchIri.ShouldBe("https://example.com/branches/main");
            return Task.CompletedTask;
        }, cfg);
    }

    // ── Null guards ──────────────────────────────────────────────────────────

    [Fact]
    public void AddForgeBranch_throws_for_null_services()
    {
        var cfg = BuildConfig();
        Should.Throw<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddForgeBranch(cfg));
    }

    [Fact]
    public void AddForgeBranch_throws_for_null_configuration()
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(
            () => services.AddForgeBranch(null!));
    }
}
