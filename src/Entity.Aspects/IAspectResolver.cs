using Forge.Entity.Repository;

namespace Forge.Entity.Aspects;

/// <summary>
/// Resolves a declared <see cref="IOperationAspect"/> to its registered <see cref="IShapeAspect"/>
/// for a given entity type and operation kind. Throws <see cref="AspectNotRegisteredException"/>
/// when the combination is not registered. See Aspects ADR-0003, ADR-0006.
/// </summary>
public interface IAspectResolver
{
    /// <summary>
    /// Return the registered <see cref="IShapeAspect"/> matching <paramref name="aspect"/>,
    /// <paramref name="entityType"/>, and <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="AspectNotRegisteredException">
    /// Thrown when the aspect is not registered for the given type and kind.
    /// </exception>
    IShapeAspect Resolve(IOperationAspect aspect, Type entityType, AspectKind kind);
}
