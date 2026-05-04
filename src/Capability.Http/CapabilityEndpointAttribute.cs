namespace Forge.Capability.Http;

/// <summary>
/// Optional HTTP-transport companion to <see cref="Forge.Capability.CapabilityAttribute"/>
/// that overrides the HTTP method used when
/// <see cref="EndpointRouteBuilderExtensions.MapCapabilities"/> auto-registers this handler
/// as a Minimal API endpoint.
/// <para>
/// When absent, the endpoint is registered as <c>POST</c> (default per Capability.Http ADR-0002).
/// </para>
/// <para>
/// <b>GET endpoints are not supported by <c>MapCapabilities()</c></b> — Minimal API does not
/// bind complex types from the request body for GET requests. Register GET endpoints manually
/// via <c>app.MapGet</c>; see Capability.Http ADR-0004 for the recommended pattern.
/// </para>
/// See Capability.Http ADR-0004.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CapabilityEndpointAttribute : Attribute
{
    /// <param name="method">
    /// The HTTP method string (e.g. <c>"PUT"</c>, <c>"PATCH"</c>, <c>"DELETE"</c>).
    /// The value is normalised to upper-case at construction time.
    /// </param>
    public CapabilityEndpointAttribute(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Method = method.ToUpperInvariant();
    }

    /// <summary>The upper-case HTTP method string (e.g. <c>"PUT"</c>).</summary>
    public string Method { get; }
}
