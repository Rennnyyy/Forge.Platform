using Forge.Execution.Http;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Forge.Execution.Http.Tests;

public sealed class HeaderExecutionAspectIriProviderTests
{
    [Fact]
    public async Task Returns_header_value_when_present()
    {
        var provider = new HeaderExecutionAspectIriProvider("X-Forge-Test-AspectIri");
        var context  = new DefaultHttpContext();
        context.Request.Headers["X-Forge-Test-AspectIri"] = "urn:test-aspect";

        var result = await provider.GetAspectIriAsync(context);

        result.ShouldBe("urn:test-aspect");
    }

    [Fact]
    public async Task Returns_null_when_header_absent()
    {
        var provider = new HeaderExecutionAspectIriProvider("X-Forge-Test-AspectIri");
        var context  = new DefaultHttpContext();

        var result = await provider.GetAspectIriAsync(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_null_for_whitespace_header()
    {
        var provider = new HeaderExecutionAspectIriProvider("X-Forge-Test-AspectIri");
        var context  = new DefaultHttpContext();
        context.Request.Headers["X-Forge-Test-AspectIri"] = "   ";

        var result = await provider.GetAspectIriAsync(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Trims_whitespace_from_header_value()
    {
        var provider = new HeaderExecutionAspectIriProvider("X-Forge-Test-AspectIri");
        var context  = new DefaultHttpContext();
        context.Request.Headers["X-Forge-Test-AspectIri"] = "  urn:trimmed  ";

        var result = await provider.GetAspectIriAsync(context);

        result.ShouldBe("urn:trimmed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_throws_for_empty_header_name(string? headerName)
    {
        Should.Throw<ArgumentException>(() => new HeaderExecutionAspectIriProvider(headerName!));
    }
}
