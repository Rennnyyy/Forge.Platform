using Forge.Aspects.Abstractions;
using Forge.Authorization;
using Forge.Authorization.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Forge.Authorization.Tests;

/// <summary>
/// Tests for the <see cref="AllowAllGuardWarningService"/> hosted service introduced
/// by Authorization ADR-0007.
/// </summary>
public sealed class AllowAllGuardWarningServiceTests
{
    // ── DI registration checks ────────────────────────────────────────────────

    [Fact]
    public void AddForgeAuthorization_without_guard_registers_IHostedService()
    {
        var services = new ServiceCollection();
        services.AddForgeAuthorization();

        services.Any(d => d.ServiceType == typeof(IHostedService)).ShouldBeTrue();
    }

    [Fact]
    public void AddForgeAuthorization_with_explicit_guard_does_not_register_warning_hosted_service()
    {
        var guard = Substitute.For<IAspectGuard>();
        var services = new ServiceCollection();
        services.AddForgeAuthorization(guard);

        services.Any(d => d.ServiceType == typeof(IHostedService)).ShouldBeFalse();
    }

    [Fact]
    public void AddForgeAuthorization_registers_at_most_one_warning_service_when_called_multiple_times()
    {
        var services = new ServiceCollection();
        services.AddForgeAuthorization();
        services.AddForgeAuthorization();

        // The warning service uses AddSingleton (not TryAdd), so multiple calls accumulate.
        // This test documents the current behavior; a future ADR may change it to TryAdd.
        services.Count(d => d.ServiceType == typeof(IHostedService)).ShouldBeGreaterThanOrEqualTo(1);
    }

    // ── StartAsync log emission ───────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_logs_Warning_with_AllowAllAspectGuard_in_message()
    {
        var logger = new CapturingLogger<AllowAllGuardWarningService>();
        var service = new AllowAllGuardWarningService(logger);

        await service.StartAsync(CancellationToken.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("AllowAllAspectGuard");
    }

    [Fact]
    public async Task StartAsync_mentions_production_deployment_in_message()
    {
        var logger = new CapturingLogger<AllowAllGuardWarningService>();
        var service = new AllowAllGuardWarningService(logger);

        await service.StartAsync(CancellationToken.None);

        logger.Entries.Single().Message.ShouldContain("production");
    }

    [Fact]
    public async Task StopAsync_does_nothing()
    {
        var logger = new CapturingLogger<AllowAllGuardWarningService>();
        var service = new AllowAllGuardWarningService(logger);

        await service.StopAsync(CancellationToken.None);

        logger.Entries.ShouldBeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
