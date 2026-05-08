using Forge.Capability;
using Forge.Execution;
using Shouldly;

namespace Forge.Capability.Tests;

/// <summary>
/// Behavioral tests for <see cref="CapabilityIdentity"/> and
/// <see cref="CapabilityAttribute"/>. See Capability ADR-0010.
/// </summary>
public sealed class CapabilityIdentityTests
{
    // ───────────────────────────────────────────────────────────────────
    // Valid construction
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("capability")]
    [InlineData("catalog.artists.create")]
    [InlineData("domain.my-service.v2.capability")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("abc-def")]
    [InlineData("domain.foo.bar")]
    public void Valid_identity_string_is_accepted(string value)
    {
        var identity = new CapabilityIdentity(value);
        identity.Value.ShouldBe(value);
    }

    // ───────────────────────────────────────────────────────────────────
    // ToRoutePath
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Single_segment_ToRoutePath_returns_the_same_string()
    {
        new CapabilityIdentity("capability").ToRoutePath().ShouldBe("capability");
    }

    [Fact]
    public void Multi_segment_ToRoutePath_replaces_dots_with_slashes()
    {
        new CapabilityIdentity("catalog.artists.create").ToRoutePath()
            .ShouldBe("catalog/artists/create");
    }

    [Fact]
    public void ToRoutePath_preserves_hyphens_in_segments()
    {
        new CapabilityIdentity("domain.my-service.v2").ToRoutePath()
            .ShouldBe("domain/my-service/v2");
    }

    // ───────────────────────────────────────────────────────────────────
    // ToString
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_returns_the_raw_value()
    {
        new CapabilityIdentity("catalog.artists.create").ToString()
            .ShouldBe("catalog.artists.create");
    }

    // ───────────────────────────────────────────────────────────────────
    // Value equality (record semantics)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_instances_with_the_same_value_are_equal()
    {
        var a = new CapabilityIdentity("catalog.artists.create");
        var b = new CapabilityIdentity("catalog.artists.create");
        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void Two_instances_with_different_values_are_not_equal()
    {
        var a = new CapabilityIdentity("catalog.artists.create");
        var b = new CapabilityIdentity("catalog.artists.delete");
        a.ShouldNotBe(b);
        (a == b).ShouldBeFalse();
    }

    // ───────────────────────────────────────────────────────────────────
    // Validation failures — null / empty
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_value_throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => new CapabilityIdentity(null!));
    }

    [Fact]
    public void Empty_string_throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => new CapabilityIdentity(""));
    }

    // ───────────────────────────────────────────────────────────────────
    // Validation failures — invalid segment characters
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CatalogArtists")]        // uppercase letters
    [InlineData("catalog_artists")]       // underscore
    [InlineData("catalog..artists")]      // consecutive dots → empty segment
    [InlineData(".catalog")]              // leading dot → empty first segment
    [InlineData("catalog.")]             // trailing dot → empty last segment
    [InlineData("catalog.-artists")]      // segment starting with hyphen
    [InlineData("catalog.artists-")]     // segment ending with hyphen
    public void Invalid_identity_string_throws_ArgumentException(string value)
    {
        var ex = Should.Throw<ArgumentException>(() => new CapabilityIdentity(value));
        ex.ParamName.ShouldBe("value");
    }
}

/// <summary>
/// Behavioral tests for <see cref="CapabilityAttribute"/>.
/// </summary>
public sealed class CapabilityAttributeTests
{
    // ───────────────────────────────────────────────────────────────────
    // Attribute stores the identity
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Attribute_constructor_stores_validated_identity()
    {
        var attr = new CapabilityAttribute("catalog.artists.create");
        attr.Identity.Value.ShouldBe("catalog.artists.create");
    }

    [Fact]
    public void Attribute_identity_ToRoutePath_works()
    {
        var attr = new CapabilityAttribute("catalog.artists.create");
        attr.Identity.ToRoutePath().ShouldBe("catalog/artists/create");
    }

    // ───────────────────────────────────────────────────────────────────
    // Reflection — attribute is readable from a decorated handler type
    // ───────────────────────────────────────────────────────────────────

    [Capability("catalog.artists.create")]
    private sealed class SampleHandler
        : ICapabilityHandler<SampleCommand, SampleResponse>
    {
        public ValueTask<ExecutionResult<SampleResponse>> HandleAsync(
            SampleCommand command,
            CapabilityContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ExecutionResult<SampleResponse>>(
                new ExecutionResult<SampleResponse>.Ok(new SampleResponse()));
    }

    private sealed record SampleCommand;
    private sealed record SampleResponse;

    [Fact]
    public void Attribute_is_readable_via_reflection_from_handler_type()
    {
        var attr = typeof(SampleHandler)
            .GetCustomAttributes(typeof(CapabilityAttribute), inherit: false)
            .Cast<CapabilityAttribute>()
            .SingleOrDefault();

        attr.ShouldNotBeNull();
        attr!.Identity.Value.ShouldBe("catalog.artists.create");
    }

    [Fact]
    public void Attribute_on_handler_type_ToRoutePath_produces_expected_path()
    {
        var attr = (CapabilityAttribute)typeof(SampleHandler)
            .GetCustomAttributes(typeof(CapabilityAttribute), inherit: false)
            .Single();

        attr.Identity.ToRoutePath().ShouldBe("catalog/artists/create");
    }

    // ───────────────────────────────────────────────────────────────────
    // Validation — invalid identity string is rejected at construction
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Attribute_with_invalid_identity_throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => new CapabilityAttribute("INVALID_IDENTITY"));
    }
}
