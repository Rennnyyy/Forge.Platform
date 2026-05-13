using Microsoft.Extensions.Hosting;

namespace Forge.Capability.Messaging;

/// <summary>
/// Generic <see cref="BackgroundService"/> that drives the
/// <see cref="CapabilityReplyListener{TCommand,TResponse}"/> loop, routing inbound
/// <see cref="CapabilityReplyEnvelope{TResponse}"/> messages back to waiting
/// <see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}.PublishAndWaitAsync"/> calls.
/// <para>
/// Registered automatically for each handler pair by the non-generic
/// <c>AddForgeCapabilityMessaging()</c> overload. Applications do not need to create
/// a type-specific subclass — see Capability.Messaging ADR-0001.
/// </para>
/// </summary>
internal sealed class CapabilityReplyPumpService<TCommand, TResponse>(
    CapabilityReplyListener<TCommand, TResponse> listener) : BackgroundService
    where TCommand : class
    where TResponse : class
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        listener.ListenAsync(stoppingToken);
}
