using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Authorization.Http.DependencyInjection;

/// <summary>
/// Service-registration extensions for the Authorization.Http slice.
/// </summary>
public static class AuthorizationHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the HTTP-layer authorization services:
    /// <list type="bullet">
    ///   <item>Binds <see cref="AuthorizationOptions"/> from the
    ///         <c>Forge:Authorization</c> configuration section when
    ///         <paramref name="configuration"/> is supplied; default option values apply
    ///         otherwise (<see cref="AuthorizationOptions.RequireExplicitGuard"/> = <see langword="true"/>).</item>
    ///   <item>Registers <see cref="AllowAllGuardStartupFilter"/> which fails startup when
    ///         <see cref="AuthorizationOptions.RequireExplicitGuard"/> is <see langword="true"/>
    ///         and no explicit <see cref="IAspectGuard"/> is wired.</item>
    /// </list>
    /// Call this method in web applications alongside
    /// <c>AddForgeAuthorization()</c>. In development environments, suppress the
    /// guard check by setting <c>Forge:Authorization:RequireExplicitGuard</c> to
    /// <see langword="false"/> (e.g. in <c>appsettings.Development.json</c>).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">
    /// Optional configuration root used to bind <see cref="AuthorizationOptions"/>.
    /// When <see langword="null"/>, the default option values are used.
    /// </param>
    public static IServiceCollection AddForgeAuthorizationHttp(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
            services.Configure<AuthorizationOptions>(
                configuration.GetSection(AuthorizationOptions.ConfigurationSection));

        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IStartupFilter, AllowAllGuardStartupFilter>());

        return services;
    }
}
