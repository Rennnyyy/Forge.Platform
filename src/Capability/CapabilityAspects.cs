using System.Collections.Immutable;
using Forge.Aspects;
using Forge.Aspects.Abstractions;

namespace Forge.Capability;

/// <summary>
/// Carries the per-execution validation aspects for a single capability dispatch call.
/// Passed to <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/>.
/// A <c>null</c> instance (or null individual properties) means permissive — no SHACL
/// validation is applied for that message slot. See Capability ADR-0007.
/// </summary>
public sealed record CapabilityAspects
{
    /// <summary>
    /// Aspect applied to the incoming command before the handler is invoked.
    /// <c>null</c> = permissive.
    /// </summary>
    public IMessageAspect? CommandAspect { get; init; }

    /// <summary>
    /// Aspect applied to the outgoing response after the handler returns.
    /// <c>null</c> = permissive.
    /// </summary>
    public IMessageAspect? ResponseAspect { get; init; }

    /// <summary>
    /// Aspects keyed by event CLR type, applied to each event in
    /// <see cref="CapabilityResult{TResponse}.Events"/> after the handler returns.
    /// A missing key is treated as permissive for that event type.
    /// </summary>
    public IReadOnlyDictionary<Type, IMessageAspect> EventAspects { get; init; }
        = ImmutableDictionary<Type, IMessageAspect>.Empty;
}
