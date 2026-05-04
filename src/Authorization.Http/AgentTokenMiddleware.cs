using Microsoft.AspNetCore.Http;

namespace Forge.Authorization.Http;

/// <summary>
/// Extracts the agent identity token from the HTTP <c>Authorization: Bearer &lt;token&gt;</c>
/// header and establishes an <see cref="AuthorizationContext"/> scope for the duration of
/// the request. See Authorization.Http ADR-0001.
/// </summary>
internal sealed class AgentTokenMiddleware(RequestDelegate next)
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers[AuthorizationHeader].FirstOrDefault();

        if (authHeader is not null
            && authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader[BearerPrefix.Length..].Trim();

            if (!string.IsNullOrWhiteSpace(token))
            {
                using var _ = AuthorizationContext.Use(token);
                await next(context);
                return;
            }
        }

        await next(context);
    }
}
