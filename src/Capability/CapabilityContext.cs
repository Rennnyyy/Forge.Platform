using System.Collections.Immutable;
using Forge.Aspects.Message;

namespace Forge.Capability;

public sealed class CapabilityContext
{
    public IMessageAspect? CommandAspect { get; init; }
    public IMessageAspect? ResponseAspect { get; init; }
    public IReadOnlyDictionary<Type, IMessageAspect> EventAspects { get; init; }
        = ImmutableDictionary<Type, IMessageAspect>.Empty;

    /// <summary>
    /// The agent identity token captured from <see cref="Forge.Authorization.AuthorizationContext.CurrentAgentToken"/>
    /// at the moment <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/> was called.
    /// If no <c>AuthorizationContext.Use(…)</c> scope was active. See Capability ADR-0008.
    /// </summary>
    public string? AgentToken { get; init; }
}
