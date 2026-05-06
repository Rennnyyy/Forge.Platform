namespace Forge.Capability.Http;

/// <summary>
/// Carries discovery metadata for a single registered capability handler.
/// Populated by <see cref="DependencyInjection.CapabilityHttpServiceCollectionExtensions.AddCapabilityHttp"/>
/// and consumed by <see cref="EndpointRouteBuilderExtensions.MapCapabilities"/>.
/// See Capability.Http ADR-0002.
/// </summary>
internal sealed class CapabilityHandlerDescriptor
{
    public CapabilityHandlerDescriptor(Type handlerType, Type commandType, Type responseType)
    {
        HandlerType = handlerType;
        CommandType = commandType;
        ResponseType = responseType;
    }

    /// <summary>The concrete handler implementation type.</summary>
    public Type HandlerType { get; }

    /// <summary>The TCommand generic argument of the handler's ICapabilityHandler&lt;,&gt; registration.</summary>
    public Type CommandType { get; }

    /// <summary>The TResponse generic argument of the handler's ICapabilityHandler&lt;,&gt; registration.</summary>
    public Type ResponseType { get; }
}
