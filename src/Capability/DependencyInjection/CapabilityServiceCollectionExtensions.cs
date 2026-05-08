using System.Reflection;
using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.Message;
using Forge.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Capability.DependencyInjection;

/// <summary>
/// DI extensions for the Capability slice. See Capability ADR-0006, ADR-0007, and ADR-0011.
/// </summary>
public static class CapabilityServiceCollectionExtensions
{
    private static readonly Type HandlerInterfaceOpenType = typeof(ICapabilityHandler<,>);

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
        AddCapabilityHandlerCore(services, typeof(TCommand), typeof(TResponse), typeof(THandler));
        return services;
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> for all concrete, non-abstract types that
    /// implement <see cref="ICapabilityHandler{TCommand,TResponse}"/> (including internal
    /// handler classes) and registers each matching pair. Uses <c>TryAdd</c> semantics —
    /// a handler already registered explicitly is not overwritten.
    /// <para>
    /// Requires <c>IMessageAspectEngine</c> to be in the container — call
    /// <c>services.AddForgeAspects()</c> before this method.
    /// </para>
    /// </summary>
    /// <param name="assemblies">One or more assemblies to scan. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddCapabilityHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (assemblies is null || assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != HandlerInterfaceOpenType)
                        continue;

                    var args = iface.GetGenericArguments();
                    AddCapabilityHandlerCore(services, args[0], args[1], type);
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Convenience overload that scans the assembly containing <typeparamref name="T"/>.
    /// Equivalent to <c>AddCapabilityHandlers(typeof(T).Assembly)</c>.
    /// </summary>
    public static IServiceCollection AddCapabilityHandlersFromAssemblyContaining<T>(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddCapabilityHandlers(services, typeof(T).Assembly);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void AddCapabilityHandlerCore(
        IServiceCollection services,
        Type commandType,
        Type responseType,
        Type handlerType)
    {
        // Ensure IAspectGuard is always resolvable. A real guard registered before this
        // call (e.g. via AddForgeAuthorization) takes precedence; AllowAllAspectGuard is
        // the explicit fallback so the DI graph is never broken at startup.
        services.TryAddSingleton<IAspectGuard>(AllowAllAspectGuard.Instance);

        var handlerServiceType = typeof(ICapabilityHandler<,>).MakeGenericType(commandType, responseType);
        var dispatcherServiceType = typeof(ICapabilityDispatcher<,>).MakeGenericType(commandType, responseType);
        var dispatcherImplType = typeof(CapabilityDispatcher<,>).MakeGenericType(commandType, responseType);

        services.TryAddTransient(handlerServiceType, handlerType);
        services.TryAdd(ServiceDescriptor.Describe(
            dispatcherServiceType,
            sp =>
            {
                var handler = sp.GetRequiredService(handlerServiceType);
                var engine = sp.GetRequiredService<IMessageAspectEngine>();
                var store = sp.GetRequiredService<IAspectStore>();
                var guard = sp.GetRequiredService<IAspectGuard>();
                return Activator.CreateInstance(dispatcherImplType, handler, engine, store, guard)!;
            },
            ServiceLifetime.Transient));
    }
}
