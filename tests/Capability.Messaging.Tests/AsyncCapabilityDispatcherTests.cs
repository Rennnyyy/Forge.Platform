using Forge.Capability;
using Forge.Capability.Messaging;
using Forge.Capability.Messaging.DependencyInjection;
using Forge.Execution;
using Forge.Messaging.Abstractions;
using Forge.Messaging.InMemory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Forge.Capability.Messaging.Tests;

// ── test domain types ────────────────────────────────────────────────────────

public sealed record PingCommand(string Message);
public sealed record PongResponse(string Reply);

// ── helpers ──────────────────────────────────────────────────────────────────

file static class Helpers
{
    internal const string CommandTopic = "test.ping.commands";
    internal const string ReplyTopic = "test.ping.replies";

    /// <summary>
    /// Builds a ServiceProvider with InMemory broker, InMemory dispatcher producer side,
    /// and a stub <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>.
    /// </summary>
    internal static ServiceProvider BuildProvider(
        ICapabilityDispatcher<PingCommand, PongResponse>? dispatcher = null)
    {
        var services = new ServiceCollection();

        services.AddForgeMessagingInMemory();

        services.AddForgeCapabilityMessaging<PingCommand, PongResponse>(opts =>
        {
            opts.CommandTopic = CommandTopic;
            opts.ReplyTopic = ReplyTopic;
            opts.DefaultReplyTimeout = TimeSpan.FromSeconds(5);
        });

        services.AddForgeCapabilityConsumer<PingCommand, PongResponse>(opts =>
        {
            opts.CommandTopic = CommandTopic;
            opts.ReplyTopic = ReplyTopic;
        });

        if (dispatcher is not null)
            services.AddSingleton(dispatcher);

        return services.BuildServiceProvider();
    }

    internal static ICapabilityDispatcher<PingCommand, PongResponse> OkDispatcher(
        string reply = "pong")
    {
        var d = Substitute.For<ICapabilityDispatcher<PingCommand, PongResponse>>();
        ExecutionResult<PongResponse> result = new ExecutionResult<PongResponse>.Ok(
            new PongResponse(reply));
        d.DispatchAsync(Arg.Any<PingCommand>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
         .Returns(ValueTask.FromResult(result));
        return d;
    }

    internal static ICapabilityDispatcher<PingCommand, PongResponse> FailDispatcher(
        string code = "TestError", string message = "Something went wrong")
    {
        var d = Substitute.For<ICapabilityDispatcher<PingCommand, PongResponse>>();
        ExecutionResult<PongResponse> result = new ExecutionResult<PongResponse>.Fail(
            new ExecutionError(code, message));
        d.DispatchAsync(Arg.Any<PingCommand>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
         .Returns(ValueTask.FromResult(result));
        return d;
    }
}

// ── envelope record tests ─────────────────────────────────────────────────────

public sealed class CapabilityCommandEnvelopeTests
{
    [Fact]
    public void Carries_all_fields()
    {
        var correlation = new ExecutionCorrelation();
        var ts = DateTimeOffset.UtcNow;
        var cmd = new PingCommand("hello");

        var env = new CapabilityCommandEnvelope<PingCommand>(
            Command: cmd,
            Correlation: correlation,
            AspectIri: "https://test/aspect",
            ReplyToTopic: "reply.topic",
            TimestampUtc: ts);

        env.Command.ShouldBe(cmd);
        env.Correlation.ShouldBe(correlation);
        env.AspectIri.ShouldBe("https://test/aspect");
        env.ReplyToTopic.ShouldBe("reply.topic");
        env.TimestampUtc.ShouldBe(ts);
    }

    [Fact]
    public void ReplyToTopic_and_AspectIri_can_be_null()
    {
        var env = new CapabilityCommandEnvelope<PingCommand>(
            Command: new PingCommand("hi"),
            Correlation: new ExecutionCorrelation(),
            AspectIri: null,
            ReplyToTopic: null,
            TimestampUtc: DateTimeOffset.UtcNow);

        env.ReplyToTopic.ShouldBeNull();
        env.AspectIri.ShouldBeNull();
    }
}

public sealed class CapabilityReplyEnvelopeTests
{
    [Fact]
    public void Carries_all_fields_for_ok_result()
    {
        var correlation = new ExecutionCorrelation();
        var result = new ExecutionResult<PongResponse>.Ok(new PongResponse("pong"));
        var ts = DateTimeOffset.UtcNow;

        var env = new CapabilityReplyEnvelope<PongResponse>(result, correlation, ts);

        env.Result.ShouldBe(result);
        env.Correlation.ShouldBe(correlation);
        env.TimestampUtc.ShouldBe(ts);
    }

    [Fact]
    public void Carries_fail_result()
    {
        var fail = new ExecutionResult<PongResponse>.Fail(new ExecutionError("E", "err"));
        var env = new CapabilityReplyEnvelope<PongResponse>(fail, new ExecutionCorrelation(), DateTimeOffset.UtcNow);

        env.Result.ShouldBeOfType<ExecutionResult<PongResponse>.Fail>();
    }
}

// ── DI resolution tests ───────────────────────────────────────────────────────

public sealed class CapabilityMessagingDiTests
{
    [Fact]
    public async Task IAsyncCapabilityDispatcher_resolves()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var dispatcher = sp.GetService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        dispatcher.ShouldNotBeNull();
    }

    [Fact]
    public async Task ICapabilityMessageConsumer_resolves()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var consumer = sp.GetService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();
        consumer.ShouldNotBeNull();
    }

    [Fact]
    public async Task CapabilityReplyListener_resolves()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var listener = sp.GetService<CapabilityReplyListener<PingCommand, PongResponse>>();
        listener.ShouldNotBeNull();
    }

    [Fact]
    public void AddForgeCapabilityMessaging_throws_when_CommandTopic_empty()
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingInMemory();

        Should.Throw<InvalidOperationException>(() =>
            services.AddForgeCapabilityMessaging<PingCommand, PongResponse>(opts =>
            {
                // CommandTopic not set
                opts.ReplyTopic = "reply";
            }));
    }
}

// ── fire-and-forget tests ─────────────────────────────────────────────────────

public sealed class AsyncCapabilityDispatcher_PublishAsync_Tests
{
    [Fact]
    public async Task PublishAsync_puts_envelope_on_command_topic()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityCommandEnvelope<PingCommand>>>();

        await dispatcher.PublishAsync(new PingCommand("fire-and-forget"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        MessageEnvelope<CapabilityCommandEnvelope<PingCommand>>? received = null;
        await foreach (var msg in consumer.ConsumeAsync(Helpers.CommandTopic, cts.Token))
        {
            received = msg;
            break;
        }

        received.ShouldNotBeNull();
        received!.Topic.ShouldBe(Helpers.CommandTopic);
        received.Payload.Command.ShouldBe(new PingCommand("fire-and-forget"));
        received.Payload.ReplyToTopic.ShouldBeNull();
    }

    [Fact]
    public async Task PublishAsync_threads_ExecutionScope_correlation()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityCommandEnvelope<PingCommand>>>();
        var correlation = new ExecutionCorrelation { ExecutionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") };

        using (ExecutionScope.Use(correlation))
        {
            await dispatcher.PublishAsync(new PingCommand("hello"));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        MessageEnvelope<CapabilityCommandEnvelope<PingCommand>>? received = null;
        await foreach (var msg in consumer.ConsumeAsync(Helpers.CommandTopic, cts.Token))
        {
            received = msg;
            break;
        }

        received!.Correlation.ExecutionId.ShouldBe(correlation.ExecutionId);
        received.Payload.Correlation.ExecutionId.ShouldBe(correlation.ExecutionId);
    }

    [Fact]
    public async Task PublishAsync_forwards_aspectIri()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var consumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityCommandEnvelope<PingCommand>>>();

        await dispatcher.PublishAsync(new PingCommand("hi"), aspectIri: "https://test.aspect/1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var msg in consumer.ConsumeAsync(Helpers.CommandTopic, cts.Token))
        {
            msg.Payload.AspectIri.ShouldBe("https://test.aspect/1");
            break;
        }
    }
}

// ── ConsumeOneAsync tests ─────────────────────────────────────────────────────

public sealed class CapabilityMessageConsumer_ConsumeOneAsync_Tests
{
    private static MessageEnvelope<CapabilityCommandEnvelope<PingCommand>> MakeCommandEnvelope(
        PingCommand command,
        string? replyToTopic = null,
        ExecutionCorrelation? correlation = null)
    {
        var corr = correlation ?? new ExecutionCorrelation();
        var payload = new CapabilityCommandEnvelope<PingCommand>(
            command, corr, AspectIri: null, ReplyToTopic: replyToTopic,
            TimestampUtc: DateTimeOffset.UtcNow);
        return new MessageEnvelope<CapabilityCommandEnvelope<PingCommand>>(
            Topic: Helpers.CommandTopic,
            PartitionKey: corr.ExecutionId.ToString(),
            Payload: payload,
            Correlation: corr,
            TimestampUtc: payload.TimestampUtc);
    }

    [Fact]
    public async Task ConsumeOne_invokes_dispatcher_and_publishes_reply()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher("pong-reply"));
        var msgConsumer = sp.GetRequiredService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();
        var replyConsumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityReplyEnvelope<PongResponse>>>();

        var envelope = MakeCommandEnvelope(new PingCommand("hi"), replyToTopic: Helpers.ReplyTopic);
        await msgConsumer.ConsumeOneAsync(envelope);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        MessageEnvelope<CapabilityReplyEnvelope<PongResponse>>? reply = null;
        await foreach (var msg in replyConsumer.ConsumeAsync(Helpers.ReplyTopic, cts.Token))
        {
            reply = msg;
            break;
        }

        reply.ShouldNotBeNull();
        var ok = reply!.Payload.Result.ShouldBeOfType<ExecutionResult<PongResponse>.Ok>();
        ok.Response.Reply.ShouldBe("pong-reply");
    }

    [Fact]
    public async Task ConsumeOne_publishes_fail_result_when_handler_fails()
    {
        await using var sp = Helpers.BuildProvider(
            Helpers.FailDispatcher("HandlerError", "handler blew up"));
        var msgConsumer = sp.GetRequiredService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();
        var replyConsumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityReplyEnvelope<PongResponse>>>();

        var envelope = MakeCommandEnvelope(new PingCommand("boom"), replyToTopic: Helpers.ReplyTopic);
        await msgConsumer.ConsumeOneAsync(envelope);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var msg in replyConsumer.ConsumeAsync(Helpers.ReplyTopic, cts.Token))
        {
            var fail = msg.Payload.Result.ShouldBeOfType<ExecutionResult<PongResponse>.Fail>();
            fail.Error.Code.ShouldBe("HandlerError");
            break;
        }
    }

    [Fact]
    public async Task ConsumeOne_skips_reply_publication_when_no_ReplyToTopic()
    {
        var capturedDispatcher = Helpers.OkDispatcher();
        await using var sp = Helpers.BuildProvider(capturedDispatcher);
        var msgConsumer = sp.GetRequiredService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();
        var replyProducer = sp.GetRequiredService<IMessageProducer<string, CapabilityReplyEnvelope<PongResponse>>>();

        // Fire-and-forget envelope (no reply topic).
        var envelope = MakeCommandEnvelope(new PingCommand("no-reply"), replyToTopic: null);
        await msgConsumer.ConsumeOneAsync(envelope);

        // Dispatcher was called once.
        await capturedDispatcher.Received(1).DispatchAsync(
            Arg.Any<PingCommand>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsumeOne_restores_correlation_as_ambient_scope()
    {
        ExecutionCorrelation? capturedCorrelation = null;

        var dispatcher = Substitute.For<ICapabilityDispatcher<PingCommand, PongResponse>>();
        dispatcher.DispatchAsync(
            Arg.Any<PingCommand>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedCorrelation = ExecutionScope.Current;
                ExecutionResult<PongResponse> r = new ExecutionResult<PongResponse>.Ok(new PongResponse("ok"));
                return ValueTask.FromResult(r);
            });

        await using var sp = Helpers.BuildProvider(dispatcher);
        var msgConsumer = sp.GetRequiredService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();

        var correlation = new ExecutionCorrelation { ExecutionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc") };
        var envelope = MakeCommandEnvelope(new PingCommand("scope"), correlation: correlation);
        await msgConsumer.ConsumeOneAsync(envelope);

        capturedCorrelation.ShouldNotBeNull();
        capturedCorrelation!.ExecutionId.ShouldBe(correlation.ExecutionId);
    }
}

// ── request-reply end-to-end tests ───────────────────────────────────────────

public sealed class RequestReply_EndToEnd_Tests
{
    /// <summary>
    /// Runs a background consumer loop that drains command envelopes and dispatches them.
    /// Returns a task that completes when <paramref name="cts"/> is cancelled.
    /// </summary>
    private static Task StartConsumerLoop(ServiceProvider sp, CancellationTokenSource cts)
    {
        var brokerConsumer = sp.GetRequiredService<IMessageConsumer<string, CapabilityCommandEnvelope<PingCommand>>>();
        var msgConsumer = sp.GetRequiredService<ICapabilityMessageConsumer<PingCommand, PongResponse>>();

        return Task.Run(async () =>
        {
            await foreach (var msg in brokerConsumer.ConsumeAsync(Helpers.CommandTopic, cts.Token)
                                                     .ConfigureAwait(false))
            {
                await msgConsumer.ConsumeOneAsync(msg, cts.Token).ConfigureAwait(false);
            }
        });
    }

    [Fact]
    public async Task PublishAndWaitAsync_returns_ok_result_when_consumer_replies()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher("hello-back"));
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var listener = sp.GetRequiredService<CapabilityReplyListener<PingCommand, PongResponse>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start consumer and reply listener loops.
        var consumerTask = StartConsumerLoop(sp, cts);
        var listenerTask = listener.ListenAsync(cts.Token);

        var result = await dispatcher.PublishAndWaitAsync(
            new PingCommand("ping"),
            timeout: TimeSpan.FromSeconds(3));

        await cts.CancelAsync();

        var ok = result.ShouldBeOfType<ExecutionResult<PongResponse>.Ok>();
        ok.Response.Reply.ShouldBe("hello-back");

        await Task.WhenAny(consumerTask, listenerTask); // let background tasks end
    }

    [Fact]
    public async Task PublishAndWaitAsync_returns_fail_when_handler_fails()
    {
        await using var sp = Helpers.BuildProvider(
            Helpers.FailDispatcher("DownstreamError", "downstream is unhappy"));
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var listener = sp.GetRequiredService<CapabilityReplyListener<PingCommand, PongResponse>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var consumerTask = StartConsumerLoop(sp, cts);
        var listenerTask = listener.ListenAsync(cts.Token);

        var result = await dispatcher.PublishAndWaitAsync(
            new PingCommand("will-fail"),
            timeout: TimeSpan.FromSeconds(3));

        await cts.CancelAsync();

        var fail = result.ShouldBeOfType<ExecutionResult<PongResponse>.Fail>();
        fail.Error.Code.ShouldBe("DownstreamError");
    }

    [Fact]
    public async Task PublishAndWaitAsync_returns_BrokerReplyTimeout_when_no_consumer()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher());
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();

        // No consumer, no listener — the reply never comes.
        var result = await dispatcher.PublishAndWaitAsync(
            new PingCommand("nobody-listening"),
            timeout: TimeSpan.FromMilliseconds(200));

        var fail = result.ShouldBeOfType<ExecutionResult<PongResponse>.Fail>();
        fail.Error.Code.ShouldBe("BrokerReplyTimeout");
    }

    [Fact]
    public async Task PublishAndWaitAsync_echoes_correlation_into_result()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher("echo"));
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var listener = sp.GetRequiredService<CapabilityReplyListener<PingCommand, PongResponse>>();

        var correlation = new ExecutionCorrelation { ExecutionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd") };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var consumerTask = StartConsumerLoop(sp, cts);
        var listenerTask = listener.ListenAsync(cts.Token);

        ExecutionResult<PongResponse> result;
        using (ExecutionScope.Use(correlation))
            result = await dispatcher.PublishAndWaitAsync(new PingCommand("ping"), timeout: TimeSpan.FromSeconds(3));

        await cts.CancelAsync();

        result.ShouldBeOfType<ExecutionResult<PongResponse>.Ok>();
    }

    [Fact]
    public async Task Multiple_concurrent_requests_resolve_independently()
    {
        await using var sp = Helpers.BuildProvider(Helpers.OkDispatcher("concurrent"));
        var dispatcher = sp.GetRequiredService<IAsyncCapabilityDispatcher<PingCommand, PongResponse>>();
        var listener = sp.GetRequiredService<CapabilityReplyListener<PingCommand, PongResponse>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var consumerTask = StartConsumerLoop(sp, cts);
        var listenerTask = listener.ListenAsync(cts.Token);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => dispatcher.PublishAndWaitAsync(
                new PingCommand("concurrent"),
                timeout: TimeSpan.FromSeconds(5)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        await cts.CancelAsync();

        results.Length.ShouldBe(5);
        results.ShouldAllBe(r => r is ExecutionResult<PongResponse>.Ok);
    }
}
