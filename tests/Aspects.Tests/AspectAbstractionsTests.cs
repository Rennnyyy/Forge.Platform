using System.Collections.Immutable;
using Forge.Aspects;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Unit tests for the Aspects.Abstractions types:
/// <see cref="Aspect"/>, <see cref="AspectNotFoundException"/>,
/// <see cref="CapabilityAspect"/>, and <see cref="MessageKind"/>.
/// </summary>
public sealed class AspectConstantsTests
{
    [Fact]
    public void NoOpIri_has_expected_value()
    {
        Aspect.NoOpIri.ShouldBe("https://forge-it.net/aspects/noop");
    }

    [Fact]
    public void NoOp_singleton_iri_equals_NoOpIri()
    {
        Aspect.NoOp.Iri.ShouldBe(Aspect.NoOpIri);
    }

    [Fact]
    public void NoOp_is_same_instance_on_repeated_access()
    {
        Aspect.NoOp.ShouldBeSameAs(Aspect.NoOp);
    }

    [Fact]
    public void NoOp_implements_IAspect()
    {
        Aspect.NoOp.ShouldBeAssignableTo<IAspect>();
    }
}

public sealed class AspectNotFoundExceptionTests
{
    [Fact]
    public void Single_arg_ctor_sets_AspectIri_and_Message()
    {
        var ex = new AspectNotFoundException("urn:missing");

        ex.AspectIri.ShouldBe("urn:missing");
        ex.Message.ShouldContain("urn:missing");
    }

    [Fact]
    public void Two_arg_ctor_sets_AspectIri_and_includes_context_in_Message()
    {
        var ex = new AspectNotFoundException("urn:x", "operation foo");

        ex.AspectIri.ShouldBe("urn:x");
        ex.Message.ShouldContain("urn:x");
        ex.Message.ShouldContain("operation foo");
    }

    [Fact]
    public void Is_assignable_to_Exception()
    {
        new AspectNotFoundException("urn:y").ShouldBeAssignableTo<Exception>();
    }
}

public sealed class CapabilityAspectTests
{
    [Fact]
    public void Required_Iri_is_preserved()
    {
        var aspect = new CapabilityAspect { Iri = "urn:cap" };

        aspect.Iri.ShouldBe("urn:cap");
    }

    [Fact]
    public void Default_command_and_response_iris_are_null()
    {
        var aspect = new CapabilityAspect { Iri = "urn:cap" };

        aspect.CommandAspectIri.ShouldBeNull();
        aspect.ResponseAspectIri.ShouldBeNull();
    }

    [Fact]
    public void Default_EventAspectIris_is_empty()
    {
        var aspect = new CapabilityAspect { Iri = "urn:cap" };

        aspect.EventAspectIris.ShouldBeEmpty();
    }

    [Fact]
    public void All_optional_properties_round_trip()
    {
        var eventMap = ImmutableDictionary<Type, string>.Empty
            .Add(typeof(string), "urn:evt");

        var aspect = new CapabilityAspect
        {
            Iri               = "urn:cap",
            CommandAspectIri  = "urn:cmd",
            ResponseAspectIri = "urn:resp",
            EventAspectIris   = eventMap,
        };

        aspect.Iri.ShouldBe("urn:cap");
        aspect.CommandAspectIri.ShouldBe("urn:cmd");
        aspect.ResponseAspectIri.ShouldBe("urn:resp");
        aspect.EventAspectIris[typeof(string)].ShouldBe("urn:evt");
    }

    [Fact]
    public void Implements_IAspect()
    {
        CapabilityAspect aspect = new CapabilityAspect { Iri = "urn:cap" };

        aspect.ShouldBeAssignableTo<IAspect>();
    }

    [Fact]
    public void Record_equality_is_value_based()
    {
        var a = new CapabilityAspect { Iri = "urn:cap", CommandAspectIri = "urn:cmd" };
        var b = new CapabilityAspect { Iri = "urn:cap", CommandAspectIri = "urn:cmd" };

        a.ShouldBe(b);
    }

    [Fact]
    public void Records_with_different_iris_are_not_equal()
    {
        var a = new CapabilityAspect { Iri = "urn:cap-1" };
        var b = new CapabilityAspect { Iri = "urn:cap-2" };

        a.ShouldNotBe(b);
    }

    [Fact]
    public void With_expression_produces_new_record_with_updated_field()
    {
        var original = new CapabilityAspect { Iri = "urn:cap" };
        var updated  = original with { CommandAspectIri = "urn:cmd" };

        updated.Iri.ShouldBe("urn:cap");
        updated.CommandAspectIri.ShouldBe("urn:cmd");
        original.CommandAspectIri.ShouldBeNull();
    }
}

public sealed class MessageKindTests
{
    [Fact]
    public void Command_value_is_1()
    {
        ((int)MessageKind.Command).ShouldBe(1);
    }

    [Fact]
    public void Response_value_is_2()
    {
        ((int)MessageKind.Response).ShouldBe(2);
    }

    [Fact]
    public void Event_value_is_4()
    {
        ((int)MessageKind.Event).ShouldBe(4);
    }

    [Fact]
    public void Flags_can_be_combined()
    {
        var combined = MessageKind.Command | MessageKind.Response | MessageKind.Event;

        combined.HasFlag(MessageKind.Command).ShouldBeTrue();
        combined.HasFlag(MessageKind.Response).ShouldBeTrue();
        combined.HasFlag(MessageKind.Event).ShouldBeTrue();
    }

    [Fact]
    public void Individual_values_do_not_overlap()
    {
        ((int)(MessageKind.Command & MessageKind.Response)).ShouldBe(0);
        ((int)(MessageKind.Command & MessageKind.Event)).ShouldBe(0);
        ((int)(MessageKind.Response & MessageKind.Event)).ShouldBe(0);
    }
}
