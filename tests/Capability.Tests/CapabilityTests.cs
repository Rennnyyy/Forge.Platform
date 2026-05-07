using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.Message;
using Forge.Capability;
using Forge.Authorization;
using NSubstitute;
using Shouldly;

namespace Forge.Capability.Tests;

/// <summary>
/// Behavioral tests for the core Capability types:
/// <see cref="CapabilityContext"/>, <see cref="CapabilityResult{TResponse}"/>,
/// and <see cref="ICapabilityHandler{TCommand,TResponse}"/>.
/// </summary>
public sealed class CapabilityContextTests
{
    [Fact]
    public void Default_constructed_context_has_null_aspect_and_null_agent_token()
    {
        var context = new CapabilityContext();

        context.Aspect.ShouldBeNull();
        context.AgentToken.ShouldBeNull();
    }

    [Fact]
    public void Context_with_populated_aspect_retains_it()
    {
        var capAspect = new CapabilityAspect { Iri = "urn:test-cap" };

        var context = new CapabilityContext
        {
            Aspect = capAspect,
            AgentToken = "agent-x",
        };

        context.Aspect.ShouldBeSameAs(capAspect);
        context.AgentToken.ShouldBe("agent-x");
    }
}

public sealed class CapabilityResultTests
{
    private sealed record MyResponse(string Value);

    [Fact]
    public void Ok_result_with_no_events_has_empty_events_list()
    {
        var result = new CapabilityResult<MyResponse>.Ok(new MyResponse("hello"));

        result.Response.Value.ShouldBe("hello");
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Ok_result_with_populated_events_round_trips()
    {
        var events = new object[] { "event-1", 42 };
        var result = new CapabilityResult<MyResponse>.Ok(new MyResponse("world"))
        {
            Events = events,
        };

        result.Events.Count.ShouldBe(2);
        result.Events[0].ShouldBe("event-1");
        result.Events[1].ShouldBe(42);
    }

    [Fact]
    public void Fail_result_carries_error_code_and_message()
    {
        var result = new CapabilityResult<MyResponse>.Fail(
            new CapabilityError("NOT_FOUND", "Artist not found"));

        result.Error.Code.ShouldBe("NOT_FOUND");
        result.Error.Message.ShouldBe("Artist not found");
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Pattern_match_distinguishes_ok_from_fail()
    {
        CapabilityResult<MyResponse> ok = new CapabilityResult<MyResponse>.Ok(new MyResponse("v"));
        CapabilityResult<MyResponse> fail = new CapabilityResult<MyResponse>.Fail(
            new CapabilityError("ERR", "bad"));

        var okBranch = ok switch
        {
            CapabilityResult<MyResponse>.Ok o => o.Response.Value,
            CapabilityResult<MyResponse>.Fail f => f.Error.Code,
            _ => "unexpected",
        };
        var failBranch = fail switch
        {
            CapabilityResult<MyResponse>.Ok o => o.Response.Value,
            CapabilityResult<MyResponse>.Fail f => f.Error.Code,
            _ => "unexpected",
        };

        okBranch.ShouldBe("v");
        failBranch.ShouldBe("ERR");
    }
}

public sealed class CapabilityHandlerTests
{
    private sealed record PingCommand(string Message);
    private sealed record PingResponse(string Echo);

    private sealed class PingHandler : ICapabilityHandler<PingCommand, PingResponse>
    {
        public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
            PingCommand command,
            CapabilityContext context,
            CancellationToken cancellationToken = default)
        {
            // The handler reads context.AgentToken — no dependency on ValidationContext.
            CapabilityResult<PingResponse> result = new CapabilityResult<PingResponse>.Ok(
                new PingResponse($"{command.Message} from {context.AgentToken ?? "anonymous"}"));
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Minimal_handler_compiles_and_returns_ok_result_with_default_context()
    {
        ICapabilityHandler<PingCommand, PingResponse> handler = new PingHandler();
        var context = new CapabilityContext();
        var command = new PingCommand("ping");

        var result = await handler.HandleAsync(command, context);

        var ok = result.ShouldBeOfType<CapabilityResult<PingResponse>.Ok>();
        ok.Response.Echo.ShouldBe("ping from anonymous");
        ok.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_can_return_fail_result()
    {
        ICapabilityHandler<PingCommand, PingResponse> handler = new FailingHandler();
        var context = new CapabilityContext();
        var command = new PingCommand("ping");

        var result = await handler.HandleAsync(command, context);

        var fail = result.ShouldBeOfType<CapabilityResult<PingResponse>.Fail>();
        fail.Error.Code.ShouldBe("INVALID");
        fail.Error.Message.ShouldBe("Command rejected");
    }

    [Fact]
    public async Task Handler_reads_agent_token_from_context_established_by_dispatcher()
    {
        // Arrange: host code establishes the ambient scope before calling DispatchAsync.
        var engine = NSubstitute.Substitute.For<Forge.Aspects.Message.IMessageAspectEngine>();
        var store = NSubstitute.Substitute.For<Forge.Aspects.Abstractions.IAspectStore>();
        var dispatcher = new CapabilityDispatcher<PingCommand, PingResponse>(new PingHandler(), engine, store);

        CapabilityResult<PingResponse> result;
        using (AuthorizationContext.Use("user-007"))
        {
            result = await dispatcher.DispatchAsync(new PingCommand("ping"));
        }

        // Assert: the handler received the agent token through CapabilityContext.
        var ok = result.ShouldBeOfType<CapabilityResult<PingResponse>.Ok>();
        ok.Response.Echo.ShouldBe("ping from user-007");
    }

    private sealed class FailingHandler : ICapabilityHandler<PingCommand, PingResponse>
    {
        public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
            PingCommand command,
            CapabilityContext context,
            CancellationToken cancellationToken = default)
        {
            CapabilityResult<PingResponse> result = new CapabilityResult<PingResponse>.Fail(
                new CapabilityError("INVALID", "Command rejected"));
            return ValueTask.FromResult(result);
        }
    }
}
