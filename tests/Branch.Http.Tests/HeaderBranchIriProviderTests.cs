using Forge.Branch.Http;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Forge.Branch.Http.Tests;

/// <summary>
/// Behavioural spec for <see cref="HeaderBranchIriProvider"/>.
/// See Execution.Http ADR-0001.
/// </summary>
public sealed class HeaderBranchIriProviderTests
{
    private static HeaderBranchIriProvider Provider() => new();

    // ════════════════════════════════════════════════════════════════════════
    // 1. Absent / whitespace header → null
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns_null_when_header_absent()
    {
        var ctx = new DefaultHttpContext();
        var result = await Provider().GetBranchIriAsync(ctx);
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns_null_for_empty_or_whitespace_header(string value)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[HeaderBranchIriProvider.BranchIriRequestHeader] = value;
        var result = await Provider().GetBranchIriAsync(ctx);
        result.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Valid absolute URI → returns trimmed value
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns_branch_iri_when_valid_absolute_uri_present()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[HeaderBranchIriProvider.BranchIriRequestHeader] =
            "https://forge-it.net/branches/feature-X";

        var result = await Provider().GetBranchIriAsync(ctx);

        result.ShouldBe("https://forge-it.net/branches/feature-X");
    }

    [Fact]
    public async Task Trims_whitespace_from_valid_header_value()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[HeaderBranchIriProvider.BranchIriRequestHeader] =
            "  https://forge-it.net/branches/main  ";

        var result = await Provider().GetBranchIriAsync(ctx);

        result.ShouldBe("https://forge-it.net/branches/main");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Invalid value → throws InvalidBranchIriException
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("branches/relative")]
    [InlineData("::malformed::")]
    public void Throws_InvalidBranchIriException_for_non_absolute_uri(string bad)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[HeaderBranchIriProvider.BranchIriRequestHeader] = bad;

        Should.Throw<InvalidBranchIriException>(() => Provider().GetBranchIriAsync(ctx));
    }
}
