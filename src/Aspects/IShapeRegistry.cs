using Forge.Entity;
using Forge.Repository;

namespace Forge.Aspects;

/// <summary>
/// In-process registry of code-origin SHACL shapes. Populated at startup by
/// <c>AddForgeAspects()</c> DI extension. Thread-safe for reads after startup.
/// The name <c>"noop"</c> is reserved and must not be registered externally.
/// </summary>
public interface IShapeRegistry
{
    /// <summary>
    /// Register <paramref name="aspect"/> as valid for <paramref name="entityType"/> and
    /// <paramref name="kind"/>. Throws <see cref="InvalidOperationException"/> if the
    /// aspect's name is <c>"noop"</c> (reserved) or if an identical registration already exists.
    /// </summary>
    void Register(IOperationAspect aspect, Type entityType, AspectKind kind);

    /// <summary>
    /// Return the registered <see cref="IOperationAspect"/> for the given identity, entity type,
    /// and kind, or <c>null</c> if no such registration exists.
    /// </summary>
    IOperationAspect? TryGet(Forge.Repository.IAspect aspect, Type entityType, AspectKind kind);
}
