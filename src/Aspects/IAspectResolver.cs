using Forge.Entity;
using Forge.Repository;

namespace Forge.Aspects;

/// <summary>
/// Resolves a declared <see cref="Forge.Repository.IAspect"/> to its registered <see cref="IOperationAspect"/>
/// for a given entity type and operation kind. Throws <see cref="AspectNotRegisteredException"/>
/// when the combination is not registered. See Aspects ADR-0003, ADR-0009.
/// </summary>
public interface IAspectResolver
{
    /// <summary>
    /// Return the registered <see cref="IOperationAspect"/> matching <paramref name="aspect"/>,
    /// <paramref name="entityType"/>, and <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="AspectNotRegisteredException">
    /// Thrown when the aspect is not registered for the given type and kind.
    /// </exception>
    IOperationAspect Resolve(Forge.Repository.IAspect aspect, Type entityType, AspectKind kind);
}
