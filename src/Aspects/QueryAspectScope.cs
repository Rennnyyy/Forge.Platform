using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// Ambient scope that routes read / query validation to an <see cref="IQueryAspect"/>
/// for the current async control flow. Independent from the write-side
/// <see cref="Forge.Repository.TransactionOperation.Aspect"/> (see ADR-0007 §"Scope").
/// </summary>
/// <example>
/// <code>
/// using var _ = QueryAspectScope.Use(ownerGate);
/// var artist = await Artist.ReadAsync(iri);           // gate applied
/// await foreach (var a in Artist.ListAsync()) { }     // gate applied
/// </code>
/// </example>
public static class QueryAspectScope
{
    private static readonly AsyncLocal<IQueryAspect?> _current = new();

    /// <summary>
    /// The <see cref="IQueryAspect"/> bound to the current async control flow, or
    /// <c>null</c> if none has been set.
    /// </summary>
    public static IQueryAspect? Current => _current.Value;

    /// <summary>
    /// Opens an ambient scope that applies <paramref name="aspect"/> to all read
    /// and query operations in the current async control flow. Dispose to restore
    /// the previous scope.
    /// </summary>
    public static IDisposable Use(IQueryAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        var previous = _current.Value;
        _current.Value = aspect;
        return new AspectScope(previous);
    }

    private sealed class AspectScope(IQueryAspect? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
