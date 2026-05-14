using Forge.Repository.Rdf;

namespace Forge.Repository;

/// <summary>
/// Optional capability surface for <see cref="IEntityStore"/> implementations that can
/// execute opaque SPARQL strings. Consumed by <c>Forge.Sparql</c> as the seam
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
/// Opt-in marker for backends that support SPARQL <c>GRAPH</c>-clause queries across
/// multiple named graphs simultaneously. Extends <see cref="ISparqlQueryStore"/> but
/// adds no new methods; it serves as a capability discriminator used by
/// <c>BranchDiffEngine</c> (Branch ADR-0004) to choose multi-graph SPARQL over the
/// scoped single-graph fallback.
/// <para/>
/// <c>GraphDbEntityStore</c> implements this interface because its SPARQL endpoint
/// natively supports named-graph federation.
/// <c>InMemoryEntityStore</c> does <em>not</em> implement it: its Leviathan dataset
/// is single-graph and <c>GRAPH</c> clauses return no results.
/// </summary>
public interface IMultiGraphSparqlStore : ISparqlQueryStore { }

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
