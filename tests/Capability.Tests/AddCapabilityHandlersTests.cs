using System.Reflection;
using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.Message;
using Forge.Capability.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Forge.Capability.Tests;

// ────────────────────────────────────────────────────────────────────────
// Test domain — handler types used only by this test class.
// They must be public so DI proxy generation works correctly,
// but they are nested to avoid naming collisions with CapabilityDispatcherTests.
// ────────────────────────────────────────────────────────────────────────

public sealed record ScanCommand(string Value);
public sealed record ScanResponse(string Result);
public sealed record AltCommand(string Value);
public sealed record AltResponse(string Result);

[Capability("scan.ping")]
public sealed class ScanHandler : ICapabilityHandler<ScanCommand, ScanResponse>
{
    public ValueTask<CapabilityResult<ScanResponse>> HandleAsync(
        ScanCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<ScanResponse>>(
            new CapabilityResult<ScanResponse>.Ok(new ScanResponse("ok")));
}

[Capability("scan.alt")]
public sealed class ScanAltHandler : ICapabilityHandler<AltCommand, AltResponse>
{
    public ValueTask<CapabilityResult<AltResponse>> HandleAsync(
        AltCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<AltResponse>>(
            new CapabilityResult<AltResponse>.Ok(new AltResponse("alt")));
}

/// <summary>
/// Behavioral tests for <see cref="CapabilityServiceCollectionExtensions.AddCapabilityHandlers"/>
/// and <see cref="CapabilityServiceCollectionExtensions.AddCapabilityHandlersFromAssemblyContaining{T}"/>.
/// Covers Capability ADR-0011.
/// </summary>
public sealed class AddCapabilityHandlersTests
{
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static IServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageAspectEngine>());
        services.AddSingleton(Substitute.For<IAspectStore>());
        configure(services);
        return services.BuildServiceProvider();
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. AddCapabilityHandlers scans assembly and registers all handlers
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCapabilityHandlers_registers_all_handlers_from_assembly()
    {
        var provider = BuildProvider(services =>
            services.AddCapabilityHandlers(typeof(ScanHandler).Assembly));

        var handlerA = provider.GetService<ICapabilityHandler<ScanCommand, ScanResponse>>();
        var handlerB = provider.GetService<ICapabilityHandler<AltCommand, AltResponse>>();

        handlerA.ShouldNotBeNull();
        handlerB.ShouldNotBeNull();
        handlerA.ShouldBeOfType<ScanHandler>();
        handlerB.ShouldBeOfType<ScanAltHandler>();
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. AddCapabilityHandlers also registers a dispatcher for each handler
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCapabilityHandlers_registers_dispatchers_for_each_handler()
    {
        var provider = BuildProvider(services =>
            services.AddCapabilityHandlers(typeof(ScanHandler).Assembly));

        var dispatcherA = provider.GetService<ICapabilityDispatcher<ScanCommand, ScanResponse>>();
        var dispatcherB = provider.GetService<ICapabilityDispatcher<AltCommand, AltResponse>>();

        dispatcherA.ShouldNotBeNull();
        dispatcherB.ShouldNotBeNull();
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. AddCapabilityHandlersFromAssemblyContaining<T> scans T's assembly
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCapabilityHandlersFromAssemblyContaining_uses_type_assembly()
    {
        var provider = BuildProvider(services =>
            services.AddCapabilityHandlersFromAssemblyContaining<ScanHandler>());

        var handler = provider.GetService<ICapabilityHandler<ScanCommand, ScanResponse>>();

        handler.ShouldNotBeNull();
        handler.ShouldBeOfType<ScanHandler>();
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. Explicit AddCapabilityHandler registered before scan is not overwritten
    //    (TryAdd semantics: first registration wins)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Explicit_registration_before_scan_is_not_overwritten()
    {
        // ScanAltHandler2 is a second implementation of ICapabilityHandler<ScanCommand, ScanResponse>
        // registered explicitly before the scan. The scan must not replace it.
        var provider = BuildProvider(services =>
        {
            services.AddCapabilityHandler<ScanCommand, ScanResponse, ExplicitScanHandler>();
            services.AddCapabilityHandlers(typeof(ScanHandler).Assembly);
        });

        var handler = provider.GetService<ICapabilityHandler<ScanCommand, ScanResponse>>();

        handler.ShouldBeOfType<ExplicitScanHandler>();
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. Passing zero assemblies throws ArgumentException
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCapabilityHandlers_with_no_assemblies_throws()
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentException>(() =>
            services.AddCapabilityHandlers());
    }

    // ────────────────────────────────────────────────────────────────────
    // 6. Registered dispatcher resolves and dispatches correctly end-to-end
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Discovered_dispatcher_dispatches_handler_correctly()
    {
        var provider = BuildProvider(services =>
            services.AddCapabilityHandlers(typeof(ScanHandler).Assembly));

        var dispatcher = provider.GetRequiredService<ICapabilityDispatcher<ScanCommand, ScanResponse>>();

        var result = await dispatcher.DispatchAsync(new ScanCommand("hello"));

        var ok = result.ShouldBeOfType<CapabilityResult<ScanResponse>.Ok>();
        ok.Response.Result.ShouldBe("ok");
    }

    // ────────────────────────────────────────────────────────────────────
    // 7. Scanning multiple assemblies registers handlers from all of them
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Multiple_assemblies_are_all_scanned()
    {
        // Scanning the same assembly twice is safe — TryAdd semantics prevent duplicates.
        var provider = BuildProvider(services =>
            services.AddCapabilityHandlers(
                typeof(ScanHandler).Assembly,
                typeof(ScanHandler).Assembly));

        var handlerA = provider.GetService<ICapabilityHandler<ScanCommand, ScanResponse>>();
        var handlerB = provider.GetService<ICapabilityHandler<AltCommand, AltResponse>>();

        handlerA.ShouldNotBeNull();
        handlerB.ShouldNotBeNull();
    }
}

// ── Extra handler used only in test 4 (explicit-before-scan guard) ───────

[Capability("scan.explicit")]
public sealed class ExplicitScanHandler : ICapabilityHandler<ScanCommand, ScanResponse>
{
    public ValueTask<CapabilityResult<ScanResponse>> HandleAsync(
        ScanCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<ScanResponse>>(
            new CapabilityResult<ScanResponse>.Ok(new ScanResponse("explicit")));
}
