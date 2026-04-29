namespace Forge.Entity;

/// <summary>
/// Ambient scope that makes an <see cref="IEntityLoader"/> available to lazy-loaded
/// entity references inside the scope, via an <see cref="AsyncLocal{T}"/> stack.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// using var scope = EntitySession.Begin(loader);
/// var bar = await foo.Bar; // loader resolved ambiently
/// </code>
/// Outside any scope, accessing an unloaded reference throws.
/// </remarks>
public sealed class EntitySession : IDisposable
{
    private static readonly AsyncLocal<EntitySession?> _current = new();

    private readonly EntitySession? _previous;
    private bool _disposed;

    public IEntityLoader Loader { get; }

    private EntitySession(IEntityLoader loader, EntitySession? previous)
    {
        Loader = loader;
        _previous = previous;
    }

    /// <summary>The active session on the current async control flow, or null if none.</summary>
    public static EntitySession? Current => _current.Value;

    /// <summary>Open a new ambient session that delegates to <paramref name="loader"/>. Dispose to restore the previous scope.</summary>
    public static EntitySession Begin(IEntityLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var session = new EntitySession(loader, _current.Value);
        _current.Value = session;
        return session;
    }

    /// <summary>Returns the active loader, or throws if no session is active.</summary>
    public static IEntityLoader RequireLoader() =>
        _current.Value?.Loader
            ?? throw new InvalidOperationException(
                "No EntitySession is active. Open one via 'using var s = EntitySession.Begin(loader);'.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _current.Value = _previous;
    }
}
