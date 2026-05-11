using Forge.Branch.DependencyInjection;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Integration tests for <see cref="DefaultBranchStartupService"/>.
/// Uses the InMemory backend so no external infrastructure is required.
/// </summary>
public sealed class DefaultBranchStartupServiceTests
{
    private static IConfiguration BuildConfig(
        string defaultBranchIri = "https://forge-it.net/branches/main",
        string managementGraphIri = "https://forge-it.net/management")
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Forge:Branch:DefaultBranchIri"] = defaultBranchIri,
            ["Forge:Branch:ManagementGraphIri"] = managementGraphIri,
        }).Build();
    }

    private static async Task<AsyncServiceScope> BuildAndStartAsync(IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddForgeEntityRepository(cfg).UseInMemory();
        services.AddForgeBranch(cfg);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();

        // Run all hosted services (triggers DefaultBranchStartupService.StartAsync).
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        return scope;
    }

    // ── Startup upsert ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_creates_default_branch_if_absent()
    {
        await using var scope = await BuildAndStartAsync();

        var store = scope.ServiceProvider.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
        var repo = new EntityRepository<Branch>(store);

        var branch = await repo.FindAsync(BranchDefault.BranchIri);
        branch.ShouldNotBeNull();
        branch.Name.ShouldBe("main");
    }

    [Fact]
    public async Task StartAsync_is_idempotent_when_branch_already_exists()
    {
        await using var scope = await BuildAndStartAsync();

        // Run startup again — should not throw, branch should still exist.
        foreach (var hosted in scope.ServiceProvider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        var store = scope.ServiceProvider.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
        var repo = new EntityRepository<Branch>(store);
        var all = await repo.QueryAllAsync().ToListAsync();

        all.Count.ShouldBe(1, "Duplicate default branch should not be created.");
    }

    [Fact]
    public async Task StartAsync_creates_branch_with_configured_name()
    {
        const string releaseIri = "https://forge-it.net/branches/release";
        var cfg = BuildConfig(defaultBranchIri: releaseIri);
        await using var scope = await BuildAndStartAsync(cfg);

        var store = scope.ServiceProvider.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
        var repo = new EntityRepository<Branch>(store);

        var branch = await repo.FindAsync(releaseIri);
        branch.ShouldNotBeNull();
        branch.Name.ShouldBe("release");
    }

    [Fact]
    public async Task StartAsync_sets_CreatedAt_to_recent_timestamp()
    {
        var before = DateTimeOffset.UtcNow;
        await using var scope = await BuildAndStartAsync();
        var after = DateTimeOffset.UtcNow;

        var store = scope.ServiceProvider.GetRequiredKeyedService<IEntityStore>("forge.branch.management");
        var repo = new EntityRepository<Branch>(store);

        var branch = await repo.FindAsync(BranchDefault.BranchIri);
        branch.ShouldNotBeNull();
        branch.CreatedAt.ShouldBeInRange(before, after);
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
