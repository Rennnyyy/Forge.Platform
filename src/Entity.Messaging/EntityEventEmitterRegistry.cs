namespace Forge.Entity.Messaging;

/// <summary>
/// Indexes all registered <see cref="IEntityEventEmitter"/> instances by CLR entity type.
/// Injected into <see cref="EventEmittingTransactionalStore"/>.
/// See root ADR-0021.
/// </summary>
internal sealed class EntityEventEmitterRegistry : IEntityEventEmitterRegistry
{
    private readonly IReadOnlyDictionary<Type, IEntityEventEmitter> _emitters;

    /// <summary>
    /// Builds the index from all <see cref="IEntityEventEmitter"/> instances registered
    /// in the DI container. Called once at provider-build time.
    /// </summary>
    public EntityEventEmitterRegistry(IEnumerable<IEntityEventEmitter> emitters)
    {
        ArgumentNullException.ThrowIfNull(emitters);

        var dict = new Dictionary<Type, IEntityEventEmitter>();
        foreach (var emitter in emitters)
        {
            // Last-registration wins for the same entity type — consistent with TryAdd semantics
            // used elsewhere (applications can override platform defaults).
            dict[emitter.EntityType] = emitter;
        }

        _emitters = dict;
    }

    /// <inheritdoc/>
    public IEntityEventEmitter? TryGet(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _emitters.TryGetValue(entityType, out var emitter) ? emitter : null;
    }
}
