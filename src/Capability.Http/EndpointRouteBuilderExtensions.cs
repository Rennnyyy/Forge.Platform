using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Capability.Http;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that auto-discover and
/// register capability handlers as Minimal API endpoints.
/// See Capability.Http ADR-0002.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private static readonly MethodInfo RegisterEndpointMethod =
        typeof(EndpointRouteBuilderExtensions)
            .GetMethod(nameof(RegisterEndpoint), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(RegisterEndpoint)} method.");

    /// <summary>
    /// Scans all <see cref="CapabilityHandlerDescriptor"/> entries registered by
    /// <c>AddCapabilityHttp()</c> and maps one <c>POST</c> Minimal API endpoint per
    /// capability. The route path is derived from the handler's <c>[Capability]</c>
    /// identity via <see cref="CapabilityIdentity.ToRoutePath"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a handler is missing <c>[Capability]</c>, or when two handlers
    /// share the same command type.
    /// </exception>
    public static IEndpointRouteBuilder MapCapabilities(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var descriptors = app.ServiceProvider
            .GetServices<CapabilityHandlerDescriptor>()
            .ToList();

        // Guard: duplicate command types
        var duplicates = descriptors
            .GroupBy(d => d.CommandType)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            var detail = string.Join("; ", duplicates.Select(g =>
                $"command '{g.Key.FullName}' handled by: {string.Join(", ", g.Select(d => d.HandlerType.FullName))}"));
            throw new InvalidOperationException(
                $"Duplicate capability command registrations detected — {detail}. " +
                "Each command type must have exactly one handler.");
        }

        foreach (var descriptor in descriptors)
        {
            // Guard: missing [Capability] attribute
            var attr = descriptor.HandlerType.GetCustomAttribute<CapabilityAttribute>(inherit: false);
            if (attr is null)
                throw new InvalidOperationException(
                    $"Handler type '{descriptor.HandlerType.FullName}' is missing the [Capability] " +
                    "attribute. All handlers discovered by MapCapabilities() must carry [Capability(\"...\")].");

            var isCrud       = descriptor.HandlerType.GetCustomAttribute<CrudCapabilityHandlerAttribute>(inherit: false) is not null;
            var prefix       = isCrud ? "api/entities" : "api/capabilities";
            var routePath    = $"{prefix}/{attr.Identity.ToRoutePath()}";
            var endpointAttr = descriptor.HandlerType.GetCustomAttribute<CapabilityEndpointAttribute>(inherit: false);
            var httpMethod   = endpointAttr?.Method ?? "POST";

            // Guard: bodyless HTTP methods (ADR-0005)
            if (httpMethod is "GET" or "DELETE")
                throw new InvalidOperationException(
                    $"Handler type '{descriptor.HandlerType.FullName}' uses HTTP method '{httpMethod}', " +
                    "which is not supported by MapCapabilities(). ASP.NET Minimal API does not bind " +
                    "complex types from the request body for GET or DELETE requests. " +
                    "Remove [CapabilityEndpoint] or use POST, PUT, or PATCH instead.");

            RegisterEndpointMethod
                .MakeGenericMethod(descriptor.CommandType, descriptor.ResponseType)
                .Invoke(null, [app, routePath, httpMethod]);
        }

        return app;
    }

    private static void RegisterEndpoint<TCommand, TResponse>(
        IEndpointRouteBuilder app,
        string routePath,
        string httpMethod)
        where TCommand : class
        where TResponse : class
    {
        app.MapMethods(routePath, [httpMethod], async (
            TCommand command,
            ICapabilityDispatcher<TCommand, TResponse> dispatcher,
            ICapabilityAspectIriProvider provider,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var iri = await provider.GetCapabilityAspectIriAsync(httpContext, ct);
            var result = await dispatcher.DispatchAsync(command, iri, ct);

            return result switch
            {
                CapabilityResult<TResponse>.Ok ok     => Results.Ok(ok.Response),
                CapabilityResult<TResponse>.Fail fail  => Results.UnprocessableEntity(fail.Error),
                _                                      => Results.StatusCode(500),
            };
        });
    }
}
