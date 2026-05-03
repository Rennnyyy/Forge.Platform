using System.Collections.Immutable;
using Forge.Aspects;
using Forge.Aspects.Message;
using Forge.Capability;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Forge.Capability.Tests;

// Test domain — public so NSubstitute's Castle proxy can access them.
public sealed record TestCommand(string Input);
public sealed record TestResponse(string Output);
public sealed record TestEvent(string Kind);

/// <summary>
/// Behavioral tests for <see cref="CapabilityDispatcher{TCommand,TResponse}"/> /
/// <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>.
/// Tests exercise the six-step pipeline defined in Capability ADR-0002 and ADR-0006,
/// using per-call <see cref="CapabilityAspects"/> per ADR-0007.
/// </summary>
public sealed class CapabilityDispatcherTests
{
    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static ICapabilityDispatcher<TestCommand, TestResponse> BuildDispatcher(
        ICapabilityHandler<TestCommand, TestResponse> handler,
        IMessageAspectEngine? engine = null)
    {
        engine ??= Substitute.For<IMessageAspectEngine>();
        return new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine);
    }

    private static ICapabilityHandler<TestCommand, TestResponse> OkHandler(
        TestResponse? response = null,
        IReadOnlyList<object>? events = null)
    {
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        CapabilityResult<TestResponse> result = new CapabilityResult<TestResponse>.Ok(
            response ?? new TestResponse("ok"))
        {
            Events = events ?? [],
        };
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(result));
        return handler;
    }

    private static ICapabilityHandler<TestCommand, TestResponse> FailHandler(CapabilityError error)
    {
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        CapabilityResult<TestResponse> result = new CapabilityResult<TestResponse>.Fail(error);
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(result));
        return handler;
    }

    // ───────────────────────────────────────────────────────────────────
    // 1. Null aspects → fully permissive, handler is called
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Null_aspects_dispatches_without_validation()
    {
        var engine  = Substitute.For<IMessageAspectEngine>();
        var handler = OkHandler(new TestResponse("pong"));
        var dispatcher = BuildDispatcher(handler, engine);

        var result = await dispatcher.DispatchAsync(new TestCommand("ping"), aspects: null);

        var ok = result.ShouldBeOfType<CapabilityResult<TestResponse>.Ok>();
        ok.Response.Output.ShouldBe("pong");

        // Engine is called with null aspect for both command and response — both are no-ops inside the engine.
        await engine.Received(2)
            .ValidateAsync(Arg.Any<object>(), null, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 2. Command aspect → engine called with command & commandAspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Command_aspect_is_passed_to_engine_for_command()
    {
        var engine        = Substitute.For<IMessageAspectEngine>();
        var commandAspect = Substitute.For<IMessageAspect>();
        var command       = new TestCommand("hi");
        var handler       = OkHandler();
        var aspects       = new CapabilityAspects { CommandAspect = commandAspect };
        var dispatcher    = BuildDispatcher(handler, engine);

        await dispatcher.DispatchAsync(command, aspects);

        await engine.Received(1)
            .ValidateAsync(command, commandAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 3. Command validation failure → exception propagates; handler not called
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Command_validation_failure_propagates_and_handler_is_not_called()
    {
        var engine        = Substitute.For<IMessageAspectEngine>();
        var commandAspect = Substitute.For<IMessageAspect>();
        var handler       = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        var violation     = new MessageAspectViolationException(
            typeof(TestCommand), "cmd-aspect",
            [new AspectViolation("urn:x", null, "http://www.w3.org/ns/shacl#Violation", "Bad command.", null)]);

        engine.ValidateAsync(Arg.Any<object>(), commandAspect, Arg.Any<CancellationToken>())
              .Throws(violation);

        var aspects    = new CapabilityAspects { CommandAspect = commandAspect };
        var dispatcher = BuildDispatcher(handler, engine);

        await Should.ThrowAsync<MessageAspectViolationException>(
            () => dispatcher.DispatchAsync(new TestCommand("bad"), aspects).AsTask());

        await handler.DidNotReceive()
            .HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 4. CapabilityContext passed to handler contains the per-call aspects
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handler_receives_context_with_per_call_aspects()
    {
        var commandAspect  = Substitute.For<IMessageAspect>();
        var responseAspect = Substitute.For<IMessageAspect>();
        var eventAspect    = Substitute.For<IMessageAspect>();
        var aspects        = new CapabilityAspects
        {
            CommandAspect  = commandAspect,
            ResponseAspect = responseAspect,
            EventAspects   = ImmutableDictionary<Type, IMessageAspect>.Empty
                                 .Add(typeof(TestEvent), eventAspect),
        };

        CapabilityContext? capturedContext = null;
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Do<CapabilityContext>(c => capturedContext = c), Arg.Any<CancellationToken>())
               .Returns(ci => ValueTask.FromResult<CapabilityResult<TestResponse>>(
                   new CapabilityResult<TestResponse>.Ok(new TestResponse("ok"))));

        var dispatcher = BuildDispatcher(handler);
        await dispatcher.DispatchAsync(new TestCommand("x"), aspects);

        capturedContext.ShouldNotBeNull();
        capturedContext!.CommandAspect.ShouldBeSameAs(commandAspect);
        capturedContext.ResponseAspect.ShouldBeSameAs(responseAspect);
        capturedContext.EventAspects[typeof(TestEvent)].ShouldBeSameAs(eventAspect);
    }

    // ───────────────────────────────────────────────────────────────────
    // 5. Response aspect → engine called with response & responseAspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Response_aspect_is_passed_to_engine_for_response()
    {
        var engine         = Substitute.For<IMessageAspectEngine>();
        var responseAspect = Substitute.For<IMessageAspect>();
        var response       = new TestResponse("result");
        var handler        = OkHandler(response);
        var aspects        = new CapabilityAspects { ResponseAspect = responseAspect };
        var dispatcher     = BuildDispatcher(handler, engine);

        await dispatcher.DispatchAsync(new TestCommand("q"), aspects);

        await engine.Received(1)
            .ValidateAsync(response, responseAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 6. Fail result → response engine NOT called for response
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fail_result_skips_response_validation()
    {
        var engine         = Substitute.For<IMessageAspectEngine>();
        var responseAspect = Substitute.For<IMessageAspect>();
        var handler        = FailHandler(new CapabilityError("ERR", "oops"));
        var aspects        = new CapabilityAspects { ResponseAspect = responseAspect };
        var dispatcher     = BuildDispatcher(handler, engine);

        var result = await dispatcher.DispatchAsync(new TestCommand("q"), aspects);

        result.ShouldBeOfType<CapabilityResult<TestResponse>.Fail>();
        // Engine called once for the null command aspect; never for the response.
        await engine.DidNotReceive()
            .ValidateAsync(Arg.Any<object>(), responseAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 7. Events → engine called per event with per-type aspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Events_are_validated_per_type_using_event_aspects()
    {
        var engine      = Substitute.For<IMessageAspectEngine>();
        var eventAspect = Substitute.For<IMessageAspect>();
        var evt         = new TestEvent("created");
        var handler     = OkHandler(events: [evt]);
        var aspects     = new CapabilityAspects
        {
            EventAspects = ImmutableDictionary<Type, IMessageAspect>.Empty
                               .Add(typeof(TestEvent), eventAspect),
        };
        var dispatcher = BuildDispatcher(handler, engine);

        await dispatcher.DispatchAsync(new TestCommand("go"), aspects);

        await engine.Received(1)
            .ValidateAsync(evt, eventAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 8. Event type not in EventAspects → engine called with null (permissive)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Event_with_no_registered_aspect_is_permissive()
    {
        var engine  = Substitute.For<IMessageAspectEngine>();
        var evt     = new TestEvent("unknown");
        // EventAspects is empty — no aspect for TestEvent.
        var handler = OkHandler(events: [evt]);
        var aspects = new CapabilityAspects();
        var dispatcher = BuildDispatcher(handler, engine);

        await dispatcher.DispatchAsync(new TestCommand("go"), aspects);

        await engine.Received(1)
            .ValidateAsync(evt, null, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 9. Result is passed through unchanged
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_returns_handler_result_unchanged()
    {
        var response   = new TestResponse("untouched");
        var events     = new object[] { new TestEvent("e1") };
        var handler    = OkHandler(response, events);
        var dispatcher = BuildDispatcher(handler);

        var result = await dispatcher.DispatchAsync(new TestCommand("x"));

        var ok = result.ShouldBeOfType<CapabilityResult<TestResponse>.Ok>();
        ok.Response.ShouldBeSameAs(response);
        ok.Events.Count.ShouldBe(1);
    }
}
