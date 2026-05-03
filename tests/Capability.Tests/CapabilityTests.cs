using System.Collections.Immutable;
using Forge.Aspects.Message;
using Forge.Capability;
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
    public void Default_constructed_context_has_null_aspects_and_empty_event_aspects()
    {
        var context = new CapabilityContext();

        context.CommandAspect.ShouldBeNull();
        context.ResponseAspect.ShouldBeNull();
        context.EventAspects.ShouldBeEmpty();
    }

    [Fact]
    public void Context_with_populated_aspects_retains_them()
    {
        var commandAspect = Substitute.For<IMessageAspect>();
        var responseAspect = Substitute.For<IMessageAspect>();
        var eventAspect = Substitute.For<IMessageAspect>();

        var context = new CapabilityContext
        {
            CommandAspect = commandAspect,
            ResponseAspect = responseAspect,
            EventAspects = ImmutableDictionary<Type, IMessageAspect>.Empty
                .Add(typeof(string), eventAspect),
        };

        context.CommandAspect.ShouldBeSameAs(commandAspect);
        context.ResponseAspect.ShouldBeSameAs(responseAspect);
        context.EventAspects[typeof(string)].ShouldBeSameAs(eventAspect);
    }
}

public sealed class CapabilityResultTests
{
    private sealed record MyResponse(string Value);

    [Fact]
    public void Result_with_no_events_has_empty_events_list()
    {
        var result = new CapabilityResult<MyResponse>
        {
            Response = new MyResponse("hello"),
        };

        result.Response.Value.ShouldBe("hello");
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Result_with_populated_events_round_trips()
    {
        var events = new object[] { "event-1", 42 };
        var result = new CapabilityResult<MyResponse>
        {
            Response = new MyResponse("world"),
            Events = events,
        };

        result.Events.Count.ShouldBe(2);
        result.Events[0].ShouldBe("event-1");
        result.Events[1].ShouldBe(42);
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
            var result = new CapabilityResult<PingResponse>
            {
                Response = new PingResponse(command.Message),
            };
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Minimal_handler_compiles_and_returns_result_with_default_context()
    {
        ICapabilityHandler<PingCommand, PingResponse> handler = new PingHandler();
        var context = new CapabilityContext();
        var command = new PingCommand("ping");

        var result = await handler.HandleAsync(command, context);

        result.Response.Echo.ShouldBe("ping");
        result.Events.ShouldBeEmpty();
    }
}
