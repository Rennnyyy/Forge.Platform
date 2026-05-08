using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forge.Authorization.Http;

/// <summary>
/// Startup filter that enforces <see cref="AuthorizationOptions.RequireExplicitGuard"/>.
/// When the option is <see langword="true"/> and the only registered
/// <see cref="IAspectGuard"/> is <see cref="AllowAllAspectGuard"/>, the application fails
/// to start with a descriptive exception — preventing silent permit-all behaviour in
/// production.
/// </summary>
internal sealed class AllowAllGuardStartupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            var options = app.ApplicationServices
                .GetRequiredService<IOptions<AuthorizationOptions>>().Value;

            if (options.RequireExplicitGuard)
            {
                var guard = app.ApplicationServices.GetService<IAspectGuard>();

                if (guard is null or AllowAllAspectGuard)
                    throw new InvalidOperationException(
                        "Forge Authorization: 'Forge:Authorization:RequireExplicitGuard' is true " +
                        "but no explicit IAspectGuard is registered — " +
                        "AllowAllAspectGuard permits every operation unconditionally. " +
                        "Either supply a real guard via AddForgeAuthorization(yourGuard) " +
                        "or set 'Forge:Authorization:RequireExplicitGuard' to false " +
                        "in your environment configuration (e.g. appsettings.Development.json).");
            }

            next(app);
        };
    }
}
