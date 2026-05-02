using Forge.Entity.Repository;

namespace Forge.Entity.Aspects;

/// <summary>
/// Thrown at commit time when a <see cref="TransactionOperation"/> declares an aspect
/// that is not registered for the operation's entity type and kind.
/// See Aspects ADR-0003.
/// </summary>
public sealed class AspectNotRegisteredException : Exception
{
    /// <summary>The name of the aspect that was not registered.</summary>
    public string AspectName { get; }

    /// <summary>The entity type for which registration was expected.</summary>
    public Type EntityType { get; }

    /// <summary>The operation kind for which registration was expected.</summary>
    public AspectKind Kind { get; }

    public AspectNotRegisteredException(string aspectName, Type entityType, AspectKind kind)
        : base($"Aspect '{aspectName}' is not registered for entity type '{entityType.Name}' with kind '{kind}'.")
    {
        AspectName = aspectName;
        EntityType = entityType;
        Kind = kind;
    }
}
