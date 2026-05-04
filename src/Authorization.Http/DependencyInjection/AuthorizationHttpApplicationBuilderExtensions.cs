using Microsoft.AspNetCore.Builder;

namespace Forge.Authorization.Http.DependencyInjection;

/// <summary>
/// Registration extensions for the Authorization.Http slice. See Authorization.Http ADR-0001.
/// </summary>
public static class AuthorizationHttpApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="AgentTokenMiddleware"/> to the request pipeline.
    /// The middleware reads <c>Authorization: Bearer &lt;token&gt;</c> and establishes
    /// an <see cref="Forge.Authorization.AuthorizationContext"/> scope for the request.
    /// Call this before <c>MapCapabilities()</c> so the agent token is present when
    /// capability handlers execute.
    /// </summary>
    public static IApplicationBuilder UseAgentTokenMiddleware(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AgentTokenMiddleware>();
    }
}
