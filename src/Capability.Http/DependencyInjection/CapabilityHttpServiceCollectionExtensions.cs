using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Capability.Http.DependencyInjection;

/// <summary>
/// DI extensions for the Capability.Http slice. See Capability.Http ADR-0001 and ADR-0002.
/// </summary>
public static class CapabilityHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="ICapabilityAspectIriProvider"/> and builds
    /// <see cref="CapabilityHandlerDescriptor"/> entries from the <c>ICapabilityHandler&lt;,&gt;</c>
    /// registrations already present in <paramref name="services"/>.
    /// <para>
    /// Must be called <em>after</em> all <c>AddCapabilityHandler&lt;&gt;()</c> calls so that
    /// the full set of handlers is visible at scan time.
    /// </para>
    /// </summary>
    public static IServiceCollection AddCapabilityHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICapabilityAspectIriProvider, HeaderCapabilityAspectIriProvider>();

        var handlerInterface = typeof(ICapabilityHandler<,>);

        var descriptors = services
            .Where(d =>
                d.ServiceType.IsGenericType &&
                d.ServiceType.GetGenericTypeDefinition() == handlerInterface &&
                d.ImplementationType is not null)
            .Select(d =>
            {
                var args = d.ServiceType.GetGenericArguments();
                return new CapabilityHandlerDescriptor(d.ImplementationType!, args[0], args[1]);
            })
            .ToList();

        foreach (var descriptor in descriptors)
            services.AddSingleton(descriptor);

        return services;
    }
}
