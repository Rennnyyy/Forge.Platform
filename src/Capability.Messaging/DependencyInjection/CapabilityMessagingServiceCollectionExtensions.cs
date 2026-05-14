using System.Reflection;
using Forge.Capability;
using Forge.Capability.Messaging;
using Forge.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Forge.Capability.Messaging.DependencyInjection;

/// <summary>
/// DI extensions for the Capability.Messaging slice. See root ADR-0022.
/// </summary>
public static class CapabilityMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the producer side of the async capability command bus for the
    /// <typeparamref name="TCommand"/>/<typeparamref name="TResponse"/> pair:
    /// <list type="bullet">
    ///   <item><see cref="IAsyncCapabilityDispatcher{TCommand,TResponse}"/> — fire-and-forget and request-reply publish.</item>
    ///   <item><see cref="PendingReplyRegistry{TCommand,TResponse}"/> — in-process reply matching (singleton).</item>
    ///   <item><see cref="CapabilityReplyListener{TCommand,TResponse}"/> — starts the reply consumer loop.</item>
    /// </list>
    /// Requires <see cref="IMessageProducer{TKey,TValue}"/> and <see cref="IMessageConsumer{TKey,TValue}"/>
    /// to already be registered (e.g. via <c>AddForgeMessagingInMemory()</c>).
    /// </summary>
    public static IServiceCollection AddForgeCapabilityMessaging<TCommand, TResponse>(
        this IServiceCollection services,
        Action<CapabilityMessagingOptions<TCommand, TResponse>> configure)
        where TCommand : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new CapabilityMessagingOptions<TCommand, TResponse>();
        configure(opts);

        if (string.IsNullOrWhiteSpace(opts.CommandTopic))
            throw new InvalidOperationException(
                $"AddForgeCapabilityMessaging<{typeof(TCommand).Name},{typeof(TResponse).Name}>: " +
                $"{nameof(CapabilityMessagingOptions<TCommand, TResponse>.CommandTopic)} must be set.");

        services.TryAddSingleton(opts);
        services.TryAddSingleton<PendingReplyRegistry<TCommand, TResponse>>();

        services.TryAddSingleton<IAsyncCapabilityDispatcher<TCommand, TResponse>>(sp =>
            new AsyncCapabilityDispatcher<TCommand, TResponse>(
                sp.GetRequiredService<IMessageProducer<string, CapabilityCommandEnvelope<TCommand>>>(),
                sp.GetRequiredService<PendingReplyRegistry<TCommand, TResponse>>(),
                sp.GetRequiredService<CapabilityMessagingOptions<TCommand, TResponse>>()));

        services.TryAddSingleton(sp =>
            new CapabilityReplyListener<TCommand, TResponse>(
                sp.GetRequiredService<IMessageConsumer<string, CapabilityReplyEnvelope<TResponse>>>(),
                sp.GetRequiredService<PendingReplyRegistry<TCommand, TResponse>>(),
                sp.GetRequiredService<CapabilityMessagingOptions<TCommand, TResponse>>()));

        return services;
    }

    /// <summary>
    /// Registers the consumer side of the async capability command bus for the
    /// <typeparamref name="TCommand"/>/<typeparamref name="TResponse"/> pair:
    /// <list type="bullet">
    ///   <item><see cref="ICapabilityMessageConsumer{TCommand,TResponse}"/> — handles one command envelope.</item>
    /// </list>
    /// Requires <see cref="ICapabilityDispatcher{TCommand,TResponse}"/> and
    /// <see cref="IMessageProducer{TKey,TValue}"/> to be registered.
    /// </summary>
    public static IServiceCollection AddForgeCapabilityConsumer<TCommand, TResponse>(
        this IServiceCollection services,
        Action<CapabilityMessagingOptions<TCommand, TResponse>>? configure = null)
        where TCommand : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            var opts = new CapabilityMessagingOptions<TCommand, TResponse>();
            configure(opts);
            services.TryAddSingleton(opts);
        }

        services.TryAddSingleton<ICapabilityMessageConsumer<TCommand, TResponse>>(sp =>
            new CapabilityMessageConsumer<TCommand, TResponse>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IMessageProducer<string, CapabilityReplyEnvelope<TResponse>>>()));

        return services;
    }

    /// <summary>
    /// Scans all <see cref="ICapabilityHandler{TCommand,TResponse}"/> registrations already
    /// present in <paramref name="services"/>, and for each handler that carries a
    /// <see cref="CapabilityAttribute"/>, automatically:
    /// <list type="bullet">
    ///   <item>Derives command and reply topic names from the capability identity using the convention
    ///     <c>forge.capabilities.{identity}.commands</c> / <c>forge.capabilities.{identity}.replies</c>.</item>
    ///   <item>Calls <see cref="AddForgeCapabilityMessaging{TCommand,TResponse}"/> and
    ///     <see cref="AddForgeCapabilityConsumer{TCommand,TResponse}"/> for the pair.</item>
    ///   <item>Registers <see cref="CapabilityCommandPumpService{TCommand,TResponse}"/> and
    ///     <see cref="CapabilityReplyPumpService{TCommand,TResponse}"/> as hosted services.</item>
    /// </list>
    /// <para>
    /// Must be called <em>after</em> all <c>AddCapabilityHandler&lt;&gt;()</c> calls so that
    /// the full set of handlers is visible at scan time. Mirrors the auto-discovery pattern
    /// of <c>AddCapabilityHttp()</c>.
    /// </para>
    /// Handlers without <see cref="CapabilityAttribute"/> are silently skipped (they are not
    /// exposed via the messaging transport).
    /// </summary>
    public static IServiceCollection AddForgeCapabilityMessaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var handlerInterface = typeof(ICapabilityHandler<,>);

        var pairs = services
            .Where(d =>
                d.ServiceType.IsGenericType &&
                d.ServiceType.GetGenericTypeDefinition() == handlerInterface &&
                d.ImplementationType is not null)
            .Select(d => new
            {
                HandlerType = d.ImplementationType!,
                CommandType = d.ServiceType.GetGenericArguments()[0],
                ResponseType = d.ServiceType.GetGenericArguments()[1],
            })
            .ToList();

        foreach (var pair in pairs)
        {
            var attr = pair.HandlerType.GetCustomAttribute<CapabilityAttribute>(inherit: false);
            if (attr is null)
                continue; // handlers without [Capability] are not exposed via messaging

            var identity = attr.Identity.Value;
            var commandTopic = $"forge.capabilities.{identity}.commands";
            var replyTopic = $"forge.capabilities.{identity}.replies";

            WireHandlerMethod
                .MakeGenericMethod(pair.CommandType, pair.ResponseType)
                .Invoke(null, [services, commandTopic, replyTopic]);
        }

        return services;
    }

    // ─── Reflection helper ──────────────────────────────────────────────────────

    private static readonly MethodInfo WireHandlerMethod =
        typeof(CapabilityMessagingServiceCollectionExtensions)
            .GetMethod(nameof(WireHandler), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(WireHandler)} method on " +
            $"{nameof(CapabilityMessagingServiceCollectionExtensions)}.");

    private static void WireHandler<TCommand, TResponse>(
        IServiceCollection services, string commandTopic, string replyTopic)
        where TCommand : class
        where TResponse : class
    {
        services.AddForgeCapabilityMessaging<TCommand, TResponse>(opts =>
        {
            opts.CommandTopic = commandTopic;
            opts.ReplyTopic = replyTopic;
        });
        services.AddForgeCapabilityConsumer<TCommand, TResponse>();
        services.AddHostedService<CapabilityCommandPumpService<TCommand, TResponse>>();
        services.AddHostedService<CapabilityReplyPumpService<TCommand, TResponse>>();
    }
}
