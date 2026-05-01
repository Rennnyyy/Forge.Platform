using Forge.Entity.Repository.Rdf;

namespace Forge.Entity.Repository;

/// <summary>
/// Optional capability surface for <see cref="IEntityStore"/> implementations that can
/// execute opaque SPARQL strings. Consumed by <c>Forge.Entity.Sparql</c> as the seam
/// between the LINQ-to-SPARQL provider and the back-end. See Sparql ADR-0002.
/// </summary>
/// <remarks>
/// Back-ends opt in by implementing this interface on the same concrete class as their
/// <see cref="IEntityStore"/> implementation. Back-ends that do not support SPARQL simply
/// omit it; <c>EntityOperations.Query&lt;T&gt;()</c> will throw
/// <see cref="NotSupportedException"/> at the entry-point with a clear message.
/// </remarks>
public interface ISparqlQueryStore
{
    /// <summary>Execute a SPARQL <c>SELECT</c> query and stream the result rows.</summary>
    IAsyncEnumerable<SparqlResultRow> ExecuteSelectAsync(
        string sparql, CancellationToken cancellationToken = default);
}

/// <summary>
/// One row of a SPARQL <c>SELECT</c> result-set: a map from projected variable name (no
/// leading <c>?</c>) to the bound <see cref="RdfTerm"/>. Unbound variables are absent.
/// </summary>
public sealed record SparqlResultRow(IReadOnlyDictionary<string, RdfTerm> Bindings)
{
    /// <summary>Get the IRI bound to <paramref name="variable"/>, or null if unbound / non-IRI.</summary>
    public string? GetIri(string variable) =>
        Bindings.TryGetValue(variable, out var term) && term.IsIri ? term.Value : null;

    /// <summary>Get the literal lexical form bound to <paramref name="variable"/>, or null if unbound / non-literal.</summary>
    public string? GetLiteral(string variable) =>
        Bindings.TryGetValue(variable, out var term) && term.IsLiteral ? term.Value : null;
}
