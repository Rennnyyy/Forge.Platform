using Forge.Entity;
using System.Collections.Concurrent;
using Forge.Repository;

namespace Forge.Aspects;

/// <summary>
/// Default implementation of <see cref="IShapeRegistry"/> and <see cref="IAspectResolver"/>.
/// All writes happen at startup; reads are lock-free after that.
/// </summary>
internal sealed class ShapeRegistry : IShapeRegistry, IAspectResolver
{
    // Key: (aspectName, entityType, flags-expanded single kind)
    private readonly ConcurrentDictionary<(string AspectName, Type EntityType, AspectKind Kind), IWriteAspect>
        _registrations = new();

    // ------------------------------------------------------------------ IShapeRegistry

    public void Register(IWriteAspect aspect, Type entityType, AspectKind kind)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(entityType);

        if (aspect.Name == Aspect.NoOp.Name)
            throw new InvalidOperationException("The aspect name 'noop' is reserved and cannot be registered.");

        // Expand flags: register one entry per individual AspectKind bit.
        foreach (AspectKind bit in ExpandBits(kind))
        {
            var key = (aspect.Name, entityType, bit);
            if (!_registrations.TryAdd(key, aspect))
                throw new InvalidOperationException(
                    $"Aspect '{aspect.Name}' is already registered for '{entityType.Name}' / {bit}.");
        }
    }

    public IWriteAspect? TryGet(IOperationAspect aspect, Type entityType, AspectKind kind)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(entityType);

        // kind should be a single bit here (from the engine).
        _registrations.TryGetValue((aspect.Name, entityType, kind), out var result);
        return result;
    }

    // ------------------------------------------------------------------ IAspectResolver

    public IWriteAspect Resolve(IOperationAspect aspect, Type entityType, AspectKind kind)
    {
        var result = TryGet(aspect, entityType, kind);
        if (result is null)
            throw new AspectNotRegisteredException(aspect.Name, entityType, kind);
        return result;
    }

    // ------------------------------------------------------------------ Helpers

    private static IEnumerable<AspectKind> ExpandBits(AspectKind kind)
    {
        foreach (AspectKind bit in Enum.GetValues<AspectKind>())
            if ((kind & bit) != 0)
                yield return bit;
    }
}
