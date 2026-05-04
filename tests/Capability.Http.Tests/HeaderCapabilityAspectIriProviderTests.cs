using Forge.Capability.Http;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;

namespace Forge.Capability.Http.Tests;

public sealed class HeaderCapabilityAspectIriProviderTests
{
    private readonly ICapabilityAspectIriProvider _provider = new HeaderCapabilityAspectIriProvider();

    // ────────────────────────────────────────────────────────────────────
    // 1. Header present → value returned
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Header_present_returns_value()
    {
        var context = BuildContext(iri: "urn:forge:aspects:strict");

        var result = await _provider.GetCapabilityAspectIriAsync(context);

        result.ShouldBe("urn:forge:aspects:strict");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. Header absent → null
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Header_absent_returns_null()
    {
        var context = BuildContext(iri: null);

        var result = await _provider.GetCapabilityAspectIriAsync(context);

        result.ShouldBeNull();
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. Whitespace-only header → null
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Whitespace_header_returns_null(string headerValue)
    {
        var context = BuildContext(iri: headerValue);

        var result = await _provider.GetCapabilityAspectIriAsync(context);

        result.ShouldBeNull();
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. Value is trimmed
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Header_value_is_trimmed()
    {
        var context = BuildContext(iri: "  urn:forge:aspects:strict  ");

        var result = await _provider.GetCapabilityAspectIriAsync(context);

        result.ShouldBe("urn:forge:aspects:strict");
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static HttpContext BuildContext(string? iri)
    {
        var context = new DefaultHttpContext();
        if (iri is not null)
            context.Request.Headers[HeaderCapabilityAspectIriProvider.HeaderName] = iri;
        return context;
    }
}
