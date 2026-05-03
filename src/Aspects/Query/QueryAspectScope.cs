namespace Forge.Aspects.Query;

/// <summary>
/// Ambient scope that routes read / query validation to an <see cref="IQueryAspect"/>
/// for the current async control flow. Independent from the write-side
/// <see cref="Forge.Repository.TransactionOperation.AspectIri"/> (see ADR-0007 §"Scope").
/// </summary>
/// <example>
/// <code>
/// using var _ = QueryAspectScope.Use(ownerGateIri);
/// var artist = await Artist.ReadAsync(iri);           // gate applied
/// await foreach (var a in Artist.ListAsync()) { }     // gate applied
/// </code>
/// </example>
public static class QueryAspectScope
{
    private static readonly AsyncLocal<string?> _currentIri = new();

    /// <summary>
    /// The IRI of the <see cref="IQueryAspect"/> bound to the current async control flow, or
    /// <c>null</c> if none has been set.
    /// </summary>
    public static string? CurrentIri => _currentIri.Value;

    /// <summary>
    /// Opens an ambient scope that applies the aspect identified by <paramref name="aspectIri"/>
    /// to all read and query operations in the current async control flow. Dispose to restore
    /// the previous scope.
    /// </summary>
    public static IDisposable Use(string aspectIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectIri);
        var previous = _currentIri.Value;
        _currentIri.Value = aspectIri;
        return new AspectScope(previous);
    }

    private sealed class AspectScope(string? previous) : IDisposable
    {
        public void Dispose() => _currentIri.Value = previous;
    }
}
