using Forge.Aspects;
using Forge.Entity;
using Forge.Aspects.Operation;
using Forge.Repository;
using VDS.RDF;
using VDS.RDF.Shacl;

namespace Forge.Aspects.Query;

/// <summary>
/// Default <see cref="IQueryAspectEngine"/>. Implements the two-pass read pipeline defined
/// in Aspects ADR-0007: access gate (SPARQL filter) then result-shape (SHACL validate).
/// </summary>
internal sealed class QueryAspectEngine : IQueryAspectEngine
{
    private const string FilterPlaceholder = "##aspect:filter##";
    private static readonly string ShaclViolationIri = "http://www.w3.org/ns/shacl#Violation";

    private readonly IShapeCache _cache;

    public QueryAspectEngine(IShapeCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    // ------------------------------------------------------------------ Access gate

    public async ValueTask ValidateAccessAsync(
        string entityIri,
        IQueryAspect aspect,
        Forge.Repository.ISparqlQueryStore queryStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityIri);
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(queryStore);

        if (aspect.FilterWhere is not { } filter) return;

        // Pre-load gate: returns rows only when access is granted.
        var gateQuery =
            $"SELECT ?granted WHERE {{ " +
            $"VALUES ?entityIri {{ <{entityIri}> }} " +
            $"BIND(true AS ?granted) " +
            $"{filter} }}";

        var granted = false;
        await foreach (var _ in queryStore.ExecuteSelectAsync(gateQuery, cancellationToken)
                           .ConfigureAwait(false))
        {
            granted = true;
            break;
        }

        if (!granted)
            throw new QueryAspectViolationException(entityIri, aspect.Iri);
    }

    // ------------------------------------------------------------------ Filter injection

    public string InjectFilter(string sparqlQuery, IQueryAspect aspect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sparqlQuery);
        ArgumentNullException.ThrowIfNull(aspect);

        if (aspect.FilterWhere is not { } filter) return sparqlQuery;

        // Append the fragment just before the closing } of the outermost WHERE block.
        // Generated queries always end with a single closing brace at the end.
        var lastBrace = sparqlQuery.LastIndexOf('}');
        if (lastBrace < 0) return sparqlQuery;

        return sparqlQuery[..lastBrace] + " " + filter + " " + sparqlQuery[lastBrace..];
    }

    public string InjectFilterDynamic(string sparqlQuery, IQueryAspect aspect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sparqlQuery);
        ArgumentNullException.ThrowIfNull(aspect);

        if (aspect.FilterWhere is not { } filter)
        {
            // No filter — replace placeholder with empty string if present, otherwise pass through.
            return sparqlQuery.Replace(FilterPlaceholder, string.Empty, StringComparison.Ordinal);
        }

        if (!sparqlQuery.Contains(FilterPlaceholder, StringComparison.Ordinal))
            throw new QueryAspectViolationException(
                message: $"Aspect '{aspect.Iri}' declares a FilterWhere but the dynamic SPARQL query " +
                         $"does not contain the required placeholder '{FilterPlaceholder}'. " +
                         $"Add the placeholder to the WHERE block of the query.",
                sourceAspectIri: aspect.Iri,
                _: false);

        return sparqlQuery.Replace(FilterPlaceholder, filter, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Result-shape validation

    public void ValidateResultGraph(IGraph resultGraph, IQueryAspect aspect, string? entityIri = null)
    {
        ArgumentNullException.ThrowIfNull(resultGraph);
        ArgumentNullException.ThrowIfNull(aspect);

        if (aspect.ResultShapeTtl is not { } shapeTtl) return;

        var shapesGraph = _cache.GetOrParse(shapeTtl);
        var report = shapesGraph.Validate(resultGraph);
        if (report.Conforms) return;

        var violations = report.Results
            .Where(r => r.Severity?.ToString() == ShaclViolationIri ||
                        r.Severity?.ToString().EndsWith("#Violation", StringComparison.Ordinal) == true)
            .Select(r => new AspectViolation(
                FocusNodeIri: r.FocusNode?.ToString() ?? entityIri ?? "(unknown)",
                PathPredicate: r.ResultPath?.ToString(),
                Severity: r.Severity?.ToString() ?? ShaclViolationIri,
                Message: r.Message?.ToString() ?? "Result-shape constraint violated.",
                SourceShapeIri: r.SourceShape?.ToString()))
            .ToList();

        if (violations.Count > 0)
            throw new QueryAspectViolationException(violations, entityIri, aspect.Iri);
    }
}
