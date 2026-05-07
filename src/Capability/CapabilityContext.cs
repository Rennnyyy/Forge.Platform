using Forge.Aspects;
using Forge.Aspects.Abstractions;

namespace Forge.Capability;

public sealed class CapabilityContext
{
    /// <summary>
    /// The resolved <see cref="CapabilityAspect"/> for this dispatch, or <c>null</c>
    /// when no capability aspect IRI was supplied (fully permissive execution).
    /// </summary>
    public CapabilityAspect? Aspect { get; init; }

    /// <summary>
    /// The agent identity token captured from <see cref="Forge.Authorization.AuthorizationContext.CurrentAgentToken"/>
    /// at the moment <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/> was called.
    /// If no <c>AuthorizationContext.Use(…)</c> scope was active. See Capability ADR-0008.
    /// </summary>
    public string? AgentToken { get; init; }
}
