using Forge.Aspects.Abstractions;
using Forge.Entity;
using Forge.Repository;

namespace Forge.Aspects.Query;

/// <summary>
/// Orchestrates the access-gate and result-shape validation passes for a read or query
/// operation. See Aspects ADR-0007 for pipeline details.
/// </summary>
public interface IQueryAspectEngine
{
    /// <summary>
    /// The placeholder string emitted by <c>Forge.Sparql.SparqlEmitter</c> into every
    /// generated WHERE clause. <see cref="InjectFilterDynamic"/> replaces it with the
    /// ambient aspect's <see cref="IQueryAspect.FilterWhere"/> (or removes it when no
    /// filter is active).
    /// </summary>
    public const string FilterPlaceholder = "##aspect:filter##";

    /// <summary>
    /// Runs the access gate (Layer 1) for a point read against
    /// <paramref name="entityIri"/>. Throws <see cref="QueryAspectViolationException"/>
    /// if the filter query returns zero rows (access denied) or if the result-shape
    /// SHACL check fails.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for loading the entity from the store after this
    /// gate passes. The result-shape pass is performed separately via
    /// <see cref="ValidateResultGraphAsync"/>.
    /// </remarks>
    ValueTask ValidateAccessAsync(
        string entityIri,
        IQueryAspect aspect,
        ISparqlQueryStore queryStore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects <see cref="IQueryAspect.FilterWhere"/> into a generated SPARQL WHERE
    /// block and returns the modified query string. No-ops when
    /// <see cref="IQueryAspect.FilterWhere"/> is <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deprecated.</strong> Use <see cref="InjectFilterDynamic"/> with the
    /// <c>##aspect:filter##</c> placeholder instead. <see cref="InjectFilter"/> relies on
    /// <c>LastIndexOf('}')</c> to locate the injection point, which is fragile when the
    /// SPARQL contains nested braces (sub-queries, GRAPH blocks, OPTIONAL blocks).
    /// </para>
    /// <para>
    /// <see cref="InjectFilter"/> will be removed in a future release. The
    /// <see cref="Forge.Sparql.SparqlEmitter"/> now emits the <c>##aspect:filter##</c>
    /// placeholder in every generated query, so all framework-internal call sites have
    /// been migrated to <see cref="InjectFilterDynamic"/>.
    /// </para>
    /// </remarks>
    [Obsolete(
        "InjectFilter relies on LastIndexOf('}') to find the injection point, which is fragile " +
        "for SPARQL with nested braces. Use InjectFilterDynamic with the ##aspect:filter## " +
        "placeholder instead. InjectFilter will be removed in a future release.")]
    string InjectFilter(string sparqlQuery, IQueryAspect aspect);

    /// <summary>
    /// Injects <see cref="IQueryAspect.FilterWhere"/> into a dynamic SPARQL query via
    /// placeholder substitution (<c>##aspect:filter##</c>). Throws
    /// <see cref="QueryAspectViolationException"/> when <see cref="IQueryAspect.FilterWhere"/>
    /// is non-null and the placeholder is absent from the query string.
    /// </summary>
    string InjectFilterDynamic(string sparqlQuery, IQueryAspect aspect);

    /// <summary>
    /// Validates an aggregate result graph against <see cref="IQueryAspect.ResultShapeTtl"/>.
    /// The graph should contain the projected triples for all entities in the result set.
    /// No-ops when <see cref="IQueryAspect.ResultShapeTtl"/> is <c>null</c>.
    /// Throws <see cref="QueryAspectViolationException"/> on any sh:Violation severity result.
    /// </summary>
    void ValidateResultGraph(
        VDS.RDF.IGraph resultGraph,
        IQueryAspect aspect,
        string? entityIri = null);
}
