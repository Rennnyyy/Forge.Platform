using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Behavioral tests for <see cref="MessageAspectRegistry"/> / <see cref="IMessageAspectRegistry"/>.
/// Covers: null-on-miss, successful lookup, flags expansion, duplicate registration guard,
/// and seal-after-first-read enforcement.
/// </summary>
public sealed class MessageAspectRegistryTests
{
    private static IMessageAspectRegistry CreateRegistry() =>
        // MessageAspectRegistry is internal; access via Forge.Aspects assembly internals-visible
        // is not needed here — we construct it directly as the concrete type to test the contract.
        new MessageAspectRegistry();

    // ─────────────────────────────────────────────────────────────────────────
    // TryGet — null on miss
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_returns_null_for_unregistered_type()
    {
        var registry = CreateRegistry();

        var result = registry.TryGet(typeof(string), MessageKind.Command);

        result.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TryGet — returns registered aspect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_returns_registered_aspect_after_Register()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("cmd-aspect", shapeTtl: null);

        registry.Register(aspect, typeof(string), MessageKind.Command);
        var result = registry.TryGet(typeof(string), MessageKind.Command);

        result.ShouldBeSameAs(aspect);
    }

    [Fact]
    public void TryGet_returns_null_for_different_kind()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("cmd-aspect", shapeTtl: null);

        registry.Register(aspect, typeof(string), MessageKind.Command);
        var result = registry.TryGet(typeof(string), MessageKind.Response);

        result.ShouldBeNull();
    }

    [Fact]
    public void TryGet_returns_null_for_different_type()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("cmd-aspect", shapeTtl: null);

        registry.Register(aspect, typeof(string), MessageKind.Command);
        var result = registry.TryGet(typeof(int), MessageKind.Command);

        result.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Register — flags expansion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Register_with_combined_flags_stores_each_bit_separately()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("multi-aspect", shapeTtl: null);

        registry.Register(aspect, typeof(string), MessageKind.Command | MessageKind.Response | MessageKind.Event);

        registry.TryGet(typeof(string), MessageKind.Command).ShouldBeSameAs(aspect);
        registry.TryGet(typeof(string), MessageKind.Response).ShouldBeSameAs(aspect);
        registry.TryGet(typeof(string), MessageKind.Event).ShouldBeSameAs(aspect);
    }

    [Fact]
    public void Register_with_two_flag_bits_stores_both_separately()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("cmd-resp", shapeTtl: null);

        registry.Register(aspect, typeof(string), MessageKind.Command | MessageKind.Response);

        registry.TryGet(typeof(string), MessageKind.Command).ShouldBeSameAs(aspect);
        registry.TryGet(typeof(string), MessageKind.Response).ShouldBeSameAs(aspect);
        registry.TryGet(typeof(string), MessageKind.Event).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Register — duplicate throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Register_duplicate_throws_InvalidOperationException()
    {
        var registry = CreateRegistry();
        var aspect1 = new InlineTtlMessageAspect("aspect-a", shapeTtl: null);
        var aspect2 = new InlineTtlMessageAspect("aspect-b", shapeTtl: null);

        registry.Register(aspect1, typeof(string), MessageKind.Command);

        Should.Throw<InvalidOperationException>(() =>
            registry.Register(aspect2, typeof(string), MessageKind.Command));
    }

    [Fact]
    public void Register_duplicate_via_combined_flags_throws_on_overlapping_bit()
    {
        var registry = CreateRegistry();
        var aspect1 = new InlineTtlMessageAspect("aspect-a", shapeTtl: null);
        var aspect2 = new InlineTtlMessageAspect("aspect-b", shapeTtl: null);

        registry.Register(aspect1, typeof(string), MessageKind.Command);

        // Command is already registered; this should throw even though Response is new
        Should.Throw<InvalidOperationException>(() =>
            registry.Register(aspect2, typeof(string), MessageKind.Command | MessageKind.Response));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seal-after-first-read enforcement
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Register_after_TryGet_throws_InvalidOperationException()
    {
        var registry = CreateRegistry();
        var aspect = new InlineTtlMessageAspect("late-aspect", shapeTtl: null);

        // Seal the registry
        _ = registry.TryGet(typeof(string), MessageKind.Command);

        Should.Throw<InvalidOperationException>(() =>
            registry.Register(aspect, typeof(string), MessageKind.Command));
    }
}
