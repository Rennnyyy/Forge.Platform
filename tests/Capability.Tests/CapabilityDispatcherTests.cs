using System.Collections.Immutable;
using Forge.Aspects.Abstractions;
using Forge.Capability;
using Forge.Execution;
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
/// Tests exercise the pipeline defined in Capability ADR-0002, ADR-0006, and ADR-0011:
/// aspects are resolved from <see cref="IAspectStore"/> by IRI.
/// </summary>
public sealed class CapabilityDispatcherTests
{
    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static ICapabilityDispatcher<TestCommand, TestResponse> BuildDispatcher(
        ICapabilityHandler<TestCommand, TestResponse> handler,
        IMessageAspectEngine? engine = null,
        IAspectStore? store = null)
    {
        engine ??= Substitute.For<IMessageAspectEngine>();
        store ??= Substitute.For<IAspectStore>();
        return new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);
    }

    /// <summary>
    /// Builds a store stub that returns <paramref name="capAspect"/> by its IRI
    /// and each supplied message aspect by its own IRI.
    /// </summary>
    private static IAspectStore StoreWith(
        CapabilityAspect capAspect,
        params (string iri, IMessageAspect aspect)[] messageAspects)
    {
        var store = Substitute.For<IAspectStore>();
        store.ResolveCapabilityAspect(capAspect.Iri).Returns(capAspect);
        foreach (var (iri, aspect) in messageAspects)
            store.ResolveMessage(iri).Returns(aspect);
        return store;
    }

    private static ICapabilityHandler<TestCommand, TestResponse> OkHandler(
        TestResponse? response = null,
        IReadOnlyList<object>? events = null)
    {
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        ExecutionResult<TestResponse> result = new ExecutionResult<TestResponse>.Ok(
            response ?? new TestResponse("ok"))
        {
            Events = events ?? [],
        };
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(result));
        return handler;
    }

    private static ICapabilityHandler<TestCommand, TestResponse> FailHandler(ExecutionError error)
    {
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        ExecutionResult<TestResponse> result = new ExecutionResult<TestResponse>.Fail(error);
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(result));
        return handler;
    }

    // ───────────────────────────────────────────────────────────────────
    // 1. Null IRI → fully permissive, handler is called
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Null_aspects_dispatches_without_validation()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var handler = OkHandler(new TestResponse("pong"));
        var dispatcher = BuildDispatcher(handler, engine);

        var result = await dispatcher.DispatchAsync(new TestCommand("ping"), capabilityAspectIri: null);

        var ok = result.ShouldBeOfType<ExecutionResult<TestResponse>.Ok>();
        ok.Response.Output.ShouldBe("pong");

        // Engine is called with null aspect for both command and response — both are no-ops inside the engine.
        await engine.Received(2)
            .ValidateAsync(Arg.Any<object>(), null, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 2. Command aspect IRI → engine called with command & resolved commandAspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Command_aspect_is_passed_to_engine_for_command()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var commandAspect = Substitute.For<IMessageAspect>();
        commandAspect.Iri.Returns("urn:cmd");
        var command = new TestCommand("hi");
        var handler = OkHandler();
        var capAspect = new CapabilityAspect { Iri = "urn:cap", CommandAspectIri = "urn:cmd" };
        var store = StoreWith(capAspect, ("urn:cmd", commandAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        await dispatcher.DispatchAsync(command, "urn:cap");

        await engine.Received(1)
            .ValidateAsync(command, commandAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 3. Command validation failure → exception propagates; handler not called
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Command_validation_failure_propagates_and_handler_is_not_called()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var commandAspect = Substitute.For<IMessageAspect>();
        commandAspect.Iri.Returns("urn:cmd");
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        var violation = new MessageAspectViolationException(
            typeof(TestCommand), "cmd-aspect",
            [new AspectViolation("urn:x", null, "http://www.w3.org/ns/shacl#Violation", "Bad command.", null)]);

        engine.ValidateAsync(Arg.Any<object>(), commandAspect, Arg.Any<CancellationToken>())
              .Throws(violation);

        var capAspect = new CapabilityAspect { Iri = "urn:cap", CommandAspectIri = "urn:cmd" };
        var store = StoreWith(capAspect, ("urn:cmd", commandAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        await Should.ThrowAsync<MessageAspectViolationException>(
            () => dispatcher.DispatchAsync(new TestCommand("bad"), "urn:cap").AsTask());

        await handler.DidNotReceive()
            .HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 4. CapabilityContext passed to handler carries the resolved CapabilityAspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handler_receives_context_with_resolved_capability_aspect()
    {
        var capAspect = new CapabilityAspect
        {
            Iri = "urn:cap",
            CommandAspectIri = "urn:cmd",
            ResponseAspectIri = "urn:resp",
            EventAspectIris = ImmutableDictionary<Type, string>.Empty
                                    .Add(typeof(TestEvent), "urn:evt"),
        };
        var commandAspect = Substitute.For<IMessageAspect>();
        commandAspect.Iri.Returns("urn:cmd");
        var responseAspect = Substitute.For<IMessageAspect>();
        responseAspect.Iri.Returns("urn:resp");
        var eventAspect = Substitute.For<IMessageAspect>();
        eventAspect.Iri.Returns("urn:evt");
        var store = StoreWith(capAspect,
            ("urn:cmd", commandAspect),
            ("urn:resp", responseAspect),
            ("urn:evt", eventAspect));

        CapabilityContext? capturedContext = null;
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Do<CapabilityContext>(c => capturedContext = c), Arg.Any<CancellationToken>())
               .Returns(ci => ValueTask.FromResult<ExecutionResult<TestResponse>>(
                   new ExecutionResult<TestResponse>.Ok(new TestResponse("ok"))));

        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            handler, Substitute.For<IMessageAspectEngine>(), store);
        await dispatcher.DispatchAsync(new TestCommand("x"), "urn:cap");

        capturedContext.ShouldNotBeNull();
        capturedContext!.Aspect.ShouldNotBeNull();
        capturedContext.Aspect!.Iri.ShouldBe("urn:cap");
        capturedContext.Aspect.CommandAspectIri.ShouldBe("urn:cmd");
        capturedContext.Aspect.ResponseAspectIri.ShouldBe("urn:resp");
        capturedContext.Aspect.EventAspectIris[typeof(TestEvent)].ShouldBe("urn:evt");
    }

    // ───────────────────────────────────────────────────────────────────
    // 5. Response aspect IRI → engine called with response & resolved responseAspect
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Response_aspect_is_passed_to_engine_for_response()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var responseAspect = Substitute.For<IMessageAspect>();
        responseAspect.Iri.Returns("urn:resp");
        var response = new TestResponse("result");
        var handler = OkHandler(response);
        var capAspect = new CapabilityAspect { Iri = "urn:cap", ResponseAspectIri = "urn:resp" };
        var store = StoreWith(capAspect, ("urn:resp", responseAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        await dispatcher.DispatchAsync(new TestCommand("q"), "urn:cap");

        await engine.Received(1)
            .ValidateAsync(response, responseAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 6. Fail result → response engine NOT called for response
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fail_result_skips_response_validation()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var responseAspect = Substitute.For<IMessageAspect>();
        responseAspect.Iri.Returns("urn:resp");
        var handler = FailHandler(new ExecutionError("ERR", "oops"));
        var capAspect = new CapabilityAspect { Iri = "urn:cap", ResponseAspectIri = "urn:resp" };
        var store = StoreWith(capAspect, ("urn:resp", responseAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        var result = await dispatcher.DispatchAsync(new TestCommand("q"), "urn:cap");

        result.ShouldBeOfType<ExecutionResult<TestResponse>.Fail>();
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
        var engine = Substitute.For<IMessageAspectEngine>();
        var eventAspect = Substitute.For<IMessageAspect>();
        eventAspect.Iri.Returns("urn:evt");
        var evt = new TestEvent("created");
        var handler = OkHandler(events: [evt]);
        var capAspect = new CapabilityAspect
        {
            Iri = "urn:cap",
            EventAspectIris = ImmutableDictionary<Type, string>.Empty.Add(typeof(TestEvent), "urn:evt"),
        };
        var store = StoreWith(capAspect, ("urn:evt", eventAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        await dispatcher.DispatchAsync(new TestCommand("go"), "urn:cap");

        await engine.Received(1)
            .ValidateAsync(evt, eventAspect, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 8. Event type not in EventAspectIris → engine called with null (permissive)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Event_with_no_registered_aspect_is_permissive()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var evt = new TestEvent("unknown");
        var handler = OkHandler(events: [evt]);
        // EventAspectIris is empty — no aspect for TestEvent.
        var capAspect = new CapabilityAspect { Iri = "urn:cap" };
        var store = StoreWith(capAspect);
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(handler, engine, store);

        await dispatcher.DispatchAsync(new TestCommand("go"), "urn:cap");

        await engine.Received(1)
            .ValidateAsync(evt, null, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 9. Result is passed through unchanged
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_returns_handler_result_unchanged()
    {
        var response = new TestResponse("untouched");
        var events = new object[] { new TestEvent("e1") };
        var handler = OkHandler(response, events);
        var dispatcher = BuildDispatcher(handler);

        var result = await dispatcher.DispatchAsync(new TestCommand("x"));

        var ok = result.ShouldBeOfType<ExecutionResult<TestResponse>.Ok>();
        ok.Response.ShouldBeSameAs(response);
        ok.Events.Count.ShouldBe(1);
    }

    // ───────────────────────────────────────────────────────────────────
    // 10. Active AuthorizationContext scope → AgentToken forwarded to handler context
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agent_token_from_validation_context_is_forwarded_to_handler_context()
    {
        CapabilityContext? capturedContext = null;
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Do<CapabilityContext>(c => capturedContext = c), Arg.Any<CancellationToken>())
               .Returns(ci => ValueTask.FromResult<ExecutionResult<TestResponse>>(
                   new ExecutionResult<TestResponse>.Ok(new TestResponse("ok"))));

        var dispatcher = BuildDispatcher(handler);

        using (Forge.Authorization.AuthorizationContext.Use("agent-42"))
        {
            await dispatcher.DispatchAsync(new TestCommand("x"));
        }

        capturedContext.ShouldNotBeNull();
        capturedContext!.AgentToken.ShouldBe("agent-42");
    }

    // ───────────────────────────────────────────────────────────────────
    // 11. No AuthorizationContext scope → AgentToken in context is null
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_validation_context_scope_results_in_null_agent_token()
    {
        CapabilityContext? capturedContext = null;
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Do<CapabilityContext>(c => capturedContext = c), Arg.Any<CancellationToken>())
               .Returns(ci => ValueTask.FromResult<ExecutionResult<TestResponse>>(
                   new ExecutionResult<TestResponse>.Ok(new TestResponse("ok"))));

        var dispatcher = BuildDispatcher(handler);
        await dispatcher.DispatchAsync(new TestCommand("x"));

        capturedContext.ShouldNotBeNull();
        capturedContext!.AgentToken.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────────
    // 12. Guard called for command with correct agent and aspect IRI
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_is_called_for_command_with_agent_and_aspect_iri()
    {
        var guard = Substitute.For<IAspectGuard>();
        var commandAspect = Substitute.For<IMessageAspect>();
        commandAspect.Iri.Returns("urn:cmd-policy");
        var capAspect = new CapabilityAspect { Iri = "urn:cap", CommandAspectIri = "urn:cmd-policy" };
        var store = StoreWith(capAspect, ("urn:cmd-policy", commandAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            OkHandler(), Substitute.For<IMessageAspectEngine>(), store, guard);

        using (Forge.Authorization.AuthorizationContext.Use("agent-1"))
            await dispatcher.DispatchAsync(new TestCommand("x"), "urn:cap");

        await guard.Received(1).AuthorizeAsync("agent-1", "urn:cmd-policy", Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 13. Guard called for command with NoOpIri when no capability aspect supplied
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_receives_noop_aspect_iri_when_no_capability_aspect()
    {
        var guard = Substitute.For<IAspectGuard>();
        // Use FailHandler so the response slot is never reached; only the command guard call fires.
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            FailHandler(new ExecutionError("ERR", "noop-test")),
            Substitute.For<IMessageAspectEngine>(),
            Substitute.For<IAspectStore>(),
            guard);

        await dispatcher.DispatchAsync(new TestCommand("x"), capabilityAspectIri: null);

        await guard.Received(1).AuthorizeAsync(string.Empty, Aspect.NoOpIri, Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 14. Guard called for response (Ok) with correct aspect IRI
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_is_called_for_response_on_ok_result()
    {
        var guard = Substitute.For<IAspectGuard>();
        var responseAspect = Substitute.For<IMessageAspect>();
        responseAspect.Iri.Returns("urn:resp-policy");
        var capAspect = new CapabilityAspect { Iri = "urn:cap", ResponseAspectIri = "urn:resp-policy" };
        var store = StoreWith(capAspect, ("urn:resp-policy", responseAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            OkHandler(), Substitute.For<IMessageAspectEngine>(), store, guard);

        await dispatcher.DispatchAsync(new TestCommand("x"), "urn:cap");

        await guard.Received(1).AuthorizeAsync(string.Empty, "urn:resp-policy", Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 15. Guard NOT called for response when handler returns Fail
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_is_not_called_for_response_on_fail_result()
    {
        var guard = Substitute.For<IAspectGuard>();
        var responseAspect = Substitute.For<IMessageAspect>();
        responseAspect.Iri.Returns("urn:resp-policy");
        var capAspect = new CapabilityAspect { Iri = "urn:cap", ResponseAspectIri = "urn:resp-policy" };
        var store = StoreWith(capAspect, ("urn:resp-policy", responseAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            FailHandler(new ExecutionError("ERR", "oops")),
            Substitute.For<IMessageAspectEngine>(), store, guard);

        await dispatcher.DispatchAsync(new TestCommand("x"), "urn:cap");

        await guard.DidNotReceive().AuthorizeAsync(Arg.Any<string>(), "urn:resp-policy", Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 16. Guard called per event with correct aspect IRI
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_is_called_per_event_with_event_aspect_iri()
    {
        var guard = Substitute.For<IAspectGuard>();
        var eventAspect = Substitute.For<IMessageAspect>();
        eventAspect.Iri.Returns("urn:evt-policy");
        var evt = new TestEvent("created");
        var capAspect = new CapabilityAspect
        {
            Iri = "urn:cap",
            EventAspectIris = ImmutableDictionary<Type, string>.Empty.Add(typeof(TestEvent), "urn:evt-policy"),
        };
        var store = StoreWith(capAspect, ("urn:evt-policy", eventAspect));
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            OkHandler(events: [evt]), Substitute.For<IMessageAspectEngine>(), store, guard);

        await dispatcher.DispatchAsync(new TestCommand("go"), "urn:cap");

        await guard.Received(1).AuthorizeAsync(string.Empty, "urn:evt-policy", Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 17. Unknown capability IRI → AspectNotFoundException (fail-closed)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_capability_iri_throws_AspectNotFoundException()
    {
        var store = Substitute.For<IAspectStore>();
        store.ResolveCapabilityAspect("urn:unknown")
             .Throws(new AspectNotFoundException("capability aspect", "urn:unknown"));
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            handler, Substitute.For<IMessageAspectEngine>(), store);

        await Should.ThrowAsync<AspectNotFoundException>(
            () => dispatcher.DispatchAsync(new TestCommand("x"), "urn:unknown").AsTask());

        await handler.DidNotReceive()
            .HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────────────────────────────────────────────
    // 18. Guard throws on command → exception propagates, handler not called
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_denial_on_command_propagates_and_handler_is_not_called()
    {
        var guard = Substitute.For<IAspectGuard>();
        var handler = Substitute.For<ICapabilityHandler<TestCommand, TestResponse>>();
        guard.AuthorizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(_ => ValueTask.FromException(new UnauthorizedAccessException("denied")));

        var dispatcher = new CapabilityDispatcher<TestCommand, TestResponse>(
            handler, Substitute.For<IMessageAspectEngine>(), Substitute.For<IAspectStore>(), guard);

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => dispatcher.DispatchAsync(new TestCommand("bad")).AsTask());

        await handler.DidNotReceive()
            .HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>());
    }
}
