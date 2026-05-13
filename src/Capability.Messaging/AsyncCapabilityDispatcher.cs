using Forge.Execution;
using Forge.Messaging.Abstractions;

namespace Forge.Capability.Messaging;

/// <summary>
/// Default implementation of <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}"/>.
/// Publishes <see cref="CapabilityCommandEnvelope{TCommand}"/> messages via
/// <see cref="IMessageProducer{TKey,TValue}"/>. Supports both fire-and-forget
/// (<see cref="PublishAsync"/>) and request-reply (<see cref="PublishAndWaitAsync"/>)
/// dispatch patterns. See root ADR-0022.
/// </summary>
internal sealed class AsyncCapabilityDispatcher<TCommand, TResponse>
    : IAsyncCapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly IMessageProducer<string, CapabilityCommandEnvelope<TCommand>> _producer;
    private readonly PendingReplyRegistry<TCommand, TResponse> _registry;
    private readonly CapabilityMessagingOptions<TCommand, TResponse> _options;

    public AsyncCapabilityDispatcher(
        IMessageProducer<string, CapabilityCommandEnvelope<TCommand>> producer,
        PendingReplyRegistry<TCommand, TResponse> registry,
        CapabilityMessagingOptions<TCommand, TResponse> options)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        _producer = producer;
        _registry = registry;
        _options = options;
    }

    /// <inheritdoc/>
    public ValueTask PublishAsync(
        TCommand command,
        string? aspectIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var correlation = ExecutionScope.Current ?? new ExecutionCorrelation();
        var envelope = BuildEnvelope(command, correlation, aspectIri, replyToTopic: null);
        return _producer.ProduceAsync(envelope, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ExecutionResult<TResponse>> PublishAndWaitAsync(
        TCommand command,
        string? aspectIri = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var effectiveTimeout = timeout ?? _options.DefaultReplyTimeout;
        var correlation = ExecutionScope.Current ?? new ExecutionCorrelation();

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var replyTask = _registry.Register(correlation.ExecutionId, linkedCts.Token);

        var envelope = BuildEnvelope(command, correlation, aspectIri, _options.ReplyTopic);
        await _producer.ProduceAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            return await replyTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                  && !cancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult<TResponse>.Fail(
                new ExecutionError(
                    "BrokerReplyTimeout",
                    $"No reply received within {effectiveTimeout}."));
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private MessageEnvelope<CapabilityCommandEnvelope<TCommand>> BuildEnvelope(
        TCommand command,
        ExecutionCorrelation correlation,
        string? aspectIri,
        string? replyToTopic)
    {
        var payload = new CapabilityCommandEnvelope<TCommand>(
            Command: command,
            Correlation: correlation,
            AspectIri: aspectIri,
            ReplyToTopic: replyToTopic,
            TimestampUtc: DateTimeOffset.UtcNow);

        return new MessageEnvelope<CapabilityCommandEnvelope<TCommand>>(
            Topic: _options.CommandTopic,
            PartitionKey: correlation.ExecutionId.ToString(),
            Payload: payload,
            Correlation: correlation,
            TimestampUtc: payload.TimestampUtc);
    }
}
