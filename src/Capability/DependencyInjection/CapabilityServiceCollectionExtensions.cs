using Forge.Aspects;
using Forge.Aspects.Message;
using Forge.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Capability.DependencyInjection;

/// <summary>
/// DI extensions for the Capability slice. See Capability ADR-0006 and ADR-0007.
/// </summary>
public static class CapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers a capability handler and its dispatcher for the
    /// (<typeparamref name="TCommand"/>, <typeparamref name="TResponse"/>) pair.
    /// <para>
    /// Requires <c>IMessageAspectEngine</c> to be in the container — call
    /// <c>services.AddForgeAspects()</c> before this method.
    /// </para>
    /// </summary>
    /// <typeparam name="TCommand">The inbound command message type.</typeparam>
    /// <typeparam name="TResponse">The outbound response message type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    public static IServiceCollection AddCapabilityHandler<TCommand, TResponse, THandler>(
        this IServiceCollection services)
        where TCommand : class
        where TResponse : class
        where THandler : class, ICapabilityHandler<TCommand, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<ICapabilityHandler<TCommand, TResponse>, THandler>();
        services.TryAddTransient<ICapabilityDispatcher<TCommand, TResponse>>(sp =>
            new CapabilityDispatcher<TCommand, TResponse>(
                sp.GetRequiredService<ICapabilityHandler<TCommand, TResponse>>(),
                sp.GetRequiredService<IMessageAspectEngine>(),
                sp.GetRequiredService<IAspectStore>(),
                sp.GetService<IAspectGuard>()));

        return services;
    }
}
