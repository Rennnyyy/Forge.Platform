namespace Forge.Entity.Messaging;

/// <summary>
/// Provides <see cref="IEntityEventEmitter"/> lookup by CLR entity type.
/// See root ADR-0021.
/// </summary>
internal interface IEntityEventEmitterRegistry
{
    /// <summary>
    /// Returns the emitter registered for <paramref name="entityType"/>,
    /// or <c>null</c> when no event emission is configured for that type.
    /// </summary>
    IEntityEventEmitter? TryGet(Type entityType);
}
