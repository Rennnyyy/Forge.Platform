using System.Collections.Immutable;
using Forge.Aspects.Message;

namespace Forge.Capability;

public sealed class CapabilityContext
{
    public IMessageAspect? CommandAspect { get; init; }
    public IMessageAspect? ResponseAspect { get; init; }
    public IReadOnlyDictionary<Type, IMessageAspect> EventAspects { get; init; }
        = ImmutableDictionary<Type, IMessageAspect>.Empty;
}
