using Forge.Messaging.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Forge.Capability.Messaging;

/// <summary>
/// Generic <see cref="BackgroundService"/> that consumes
/// <see cref="CapabilityCommandEnvelope{TCommand}"/> messages from the command topic
/// and delegates each one to <see cref="ICapabilityMessageConsumer{TCommand,TResponse}"/>.
/// <para>
/// Registered automatically for each handler pair by the non-generic
/// <c>AddForgeCapabilityMessaging()</c> overload. Applications do not need to create
/// a type-specific subclass — see Capability.Messaging ADR-0001.
/// </para>
/// </summary>
internal sealed class CapabilityCommandPumpService<TCommand, TResponse>(
    IMessageConsumer<string, CapabilityCommandEnvelope<TCommand>> consumer,
    ICapabilityMessageConsumer<TCommand, TResponse> messageConsumer,
    CapabilityMessagingOptions<TCommand, TResponse> options) : BackgroundService
    where TCommand : class
    where TResponse : class
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in consumer.ConsumeAsync(options.CommandTopic, stoppingToken)
                                               .ConfigureAwait(false))
            await messageConsumer.ConsumeOneAsync(envelope, stoppingToken).ConfigureAwait(false);
    }
}
