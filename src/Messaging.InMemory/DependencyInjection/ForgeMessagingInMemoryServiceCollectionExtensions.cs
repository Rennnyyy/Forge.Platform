using Forge.Messaging.Abstractions;
using Forge.Messaging.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Messaging.InMemory.DependencyInjection;

/// <summary>
/// DI extensions for the InMemory messaging implementation.
/// See root ADR-0020.
/// </summary>
public static class ForgeMessagingInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="InMemoryMessageBroker"/> as a singleton and wires
    /// open-generic registrations so that <see cref="IMessageProducer{TKey,TValue}"/>
    /// and <see cref="IMessageConsumer{TKey,TValue}"/> resolve to their InMemory
    /// implementations for any type pair.
    /// <para>
    /// Uses <c>TryAdd</c> semantics — existing registrations are not overwritten.
    /// </para>
    /// </summary>
    public static IServiceCollection AddForgeMessagingInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryMessageBroker>();
        services.TryAddSingleton(typeof(IMessageProducer<,>), typeof(InMemoryMessageProducer<,>));
        services.TryAddSingleton(typeof(IMessageConsumer<,>), typeof(InMemoryMessageConsumer<,>));

        return services;
    }
}
