using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Tests for the <c>IMessageAspect</c> type hierarchy introduced in Trunk 02.
/// Covers <see cref="InlineTtlMessageAspect"/> constructor guards, <see cref="MessageKind"/>
/// flag combinations, and <see cref="MessageAspectViolationException"/> property correctness.
/// </summary>
public sealed class MessageAspectTypeTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // InlineTtlMessageAspect — constructor guards
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InlineTtlMessageAspect_null_name_throws()
    {
        Should.Throw<ArgumentException>(() => new InlineTtlMessageAspect(null!, shapeTtl: null));
    }

    [Fact]
    public void InlineTtlMessageAspect_empty_name_throws()
    {
        Should.Throw<ArgumentException>(() => new InlineTtlMessageAspect("", shapeTtl: null));
    }

    [Fact]
    public void InlineTtlMessageAspect_whitespace_name_throws()
    {
        Should.Throw<ArgumentException>(() => new InlineTtlMessageAspect("   ", shapeTtl: null));
    }

    [Fact]
    public void InlineTtlMessageAspect_noop_name_throws()
    {
        var ex = Should.Throw<ArgumentException>(() => new InlineTtlMessageAspect("noop", shapeTtl: null));
        ex.ParamName.ShouldBe("name");
    }

    [Fact]
    public void InlineTtlMessageAspect_null_shapeTtl_is_valid()
    {
        var aspect = new InlineTtlMessageAspect("my-message-aspect", shapeTtl: null);
        aspect.Name.ShouldBe("my-message-aspect");
        aspect.ShapeTtl.ShouldBeNull();
    }

    [Fact]
    public void InlineTtlMessageAspect_with_shapeTtl_stores_correctly()
    {
        const string ttl = "@prefix sh: <http://www.w3.org/ns/shacl#> .";
        var aspect = new InlineTtlMessageAspect("shaped-aspect", ttl);
        aspect.Name.ShouldBe("shaped-aspect");
        aspect.ShapeTtl.ShouldBe(ttl);
    }

    [Fact]
    public void InlineTtlMessageAspect_implements_IMessageAspect()
    {
        var aspect = new InlineTtlMessageAspect("test", shapeTtl: null);
        aspect.ShouldBeAssignableTo<IMessageAspect>();
    }

    [Fact]
    public void InlineTtlMessageAspect_implements_IAspect()
    {
        var aspect = new InlineTtlMessageAspect("test", shapeTtl: null);
        aspect.ShouldBeAssignableTo<Forge.Repository.IAspect>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MessageKind — flag combinations
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MessageKind_individual_values_are_distinct_powers_of_two()
    {
        ((int)MessageKind.Command).ShouldBe(1);
        ((int)MessageKind.Response).ShouldBe(2);
        ((int)MessageKind.Event).ShouldBe(4);
    }

    [Fact]
    public void MessageKind_flags_can_be_combined()
    {
        var combined = MessageKind.Command | MessageKind.Response;
        combined.HasFlag(MessageKind.Command).ShouldBeTrue();
        combined.HasFlag(MessageKind.Response).ShouldBeTrue();
        combined.HasFlag(MessageKind.Event).ShouldBeFalse();
    }

    [Fact]
    public void MessageKind_all_flags_combined()
    {
        var all = MessageKind.Command | MessageKind.Response | MessageKind.Event;
        all.HasFlag(MessageKind.Command).ShouldBeTrue();
        all.HasFlag(MessageKind.Response).ShouldBeTrue();
        all.HasFlag(MessageKind.Event).ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MessageAspectViolationException — property correctness
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MessageAspectViolationException_carries_correct_properties()
    {
        var violations = new List<AspectViolation>
        {
            new("urn:msg:1", null, "http://www.w3.org/ns/shacl#Violation", "Field required.", null),
        };

        var ex = new MessageAspectViolationException(
            typeof(string),
            "my-aspect",
            violations);

        ex.MessageType.ShouldBe(typeof(string));
        ex.AspectName.ShouldBe("my-aspect");
        ex.Violations.ShouldBeSameAs(violations);
        ex.Violations.Count.ShouldBe(1);
    }

    [Fact]
    public void MessageAspectViolationException_message_includes_aspect_name_and_type()
    {
        var violations = new List<AspectViolation>
        {
            new("urn:msg:1", null, "http://www.w3.org/ns/shacl#Violation", "Missing required field.", null),
        };

        var ex = new MessageAspectViolationException(typeof(int), "cmd-aspect", violations);

        ex.Message.ShouldContain("cmd-aspect");
        ex.Message.ShouldContain("Int32");
        ex.Message.ShouldContain("Missing required field.");
    }

    [Fact]
    public void MessageAspectViolationException_multiple_violations_shows_count()
    {
        var violations = new List<AspectViolation>
        {
            new("urn:msg:1", null, "http://www.w3.org/ns/shacl#Violation", "First violation.", null),
            new("urn:msg:2", null, "http://www.w3.org/ns/shacl#Violation", "Second violation.", null),
            new("urn:msg:3", null, "http://www.w3.org/ns/shacl#Violation", "Third violation.", null),
        };

        var ex = new MessageAspectViolationException(typeof(object), "multi-aspect", violations);

        ex.Message.ShouldContain("+2 more");
    }

    [Fact]
    public void MessageAspectViolationException_empty_violations_does_not_throw()
    {
        var violations = new List<AspectViolation>();
        var ex = new MessageAspectViolationException(typeof(object), "empty-aspect", violations);

        ex.Violations.ShouldBeEmpty();
        ex.Message.ShouldContain("(no message)");
    }

    [Fact]
    public void MessageAspectViolationException_is_Exception()
    {
        var ex = new MessageAspectViolationException(typeof(object), "a", new List<AspectViolation>());
        ex.ShouldBeAssignableTo<Exception>();
    }
}
