using Forge.Entity;
using System.Text;
using Forge.Repository;
using Forge.Repository.Rdf;
using Microsoft.Extensions.Options;
using VDS.RDF;
using VDS.RDF.Shacl;
using VDS.RDF.Shacl.Validation;

namespace Forge.Aspects;

/// <summary>
/// Default <see cref="IAspectEngine"/>. Implements the two-pass pipeline defined in
/// Aspects ADR-0001: Local pass (SHACL graph validate) then Context pass (SPARQL SELECT).
/// </summary>
internal sealed class AspectEngine : IAspectEngine
{
    private static readonly string ShaclViolationIri = "http://www.w3.org/ns/shacl#Violation";

    private readonly IAspectResolver _resolver;
    private readonly IShapeCache _cache;
    private readonly IRdfMapperRegistry _mappers;
    private readonly EntityRepositoryOptions _options;

    public AspectEngine(
        IAspectResolver resolver,
        IShapeCache cache,
        IRdfMapperRegistry mappers,
        IOptions<EntityRepositoryOptions> options)
    {
        _resolver = resolver;
        _cache = cache;
        _mappers = mappers;
        _options = options.Value;
    }

    public async ValueTask ValidateAsync(
        TransactionOperation operation,
        ISparqlQueryStore queryStore,
        CancellationToken cancellationToken = default)
    {
        // Fast-path: NoOp aspect — no validation.
        if (ReferenceEquals(operation.Aspect, Aspect.NoOp))
            return;

        var kind = OperationKind(operation);
        var entityType = ResolveEntityType(operation);
        var shapeAspect = _resolver.Resolve(operation.Aspect, entityType, kind);

        // 1. Local pass
        if (shapeAspect.LocalShapeTtl is { } localTtl)
        {
            var localGraph = BuildLocalGraph(operation);
            var shapesGraph = _cache.GetOrParse(localTtl);
            var report = shapesGraph.Validate(localGraph);
            ThrowIfViolations(report, operation, shapeAspect.Name);
        }

        // 2. Context pass
        if (shapeAspect.ContextWhere is { } whereBody)
        {
            // Inject the operation entity IRI as ?entityIri so WHERE bodies can reference it.
            // ?focusNode is unset by default; the engine falls back to op.EntityIri per row.
            var fullQuery = $"SELECT ?focusNode ?message ?path WHERE {{ VALUES ?entityIri {{ <{operation.EntityIri}> }} {whereBody} }}";
            await RunContextPassAsync(fullQuery, queryStore, operation, shapeAspect.Name, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ Local pass helpers

    private Graph BuildLocalGraph(TransactionOperation operation)
    {
        var dotNetGraph = new Graph();

        if (operation is not EntityWriteOperation write)
            return dotNetGraph; // Delete → empty graph

        var mapper = _mappers.ForEntityType(write.Entity.GetType());
        var typeIri = mapper.ResolveTypeIri(_options);
        var sink = new CollectingTripleSink();
        mapper.ProjectEntity(write.Entity, sink, typeIri);

        var blankCache = new Dictionary<string, IBlankNode>(StringComparer.Ordinal);
        foreach (var triple in sink.Triples)
            dotNetGraph.Assert(ToRdfNode(triple.Subject, dotNetGraph, blankCache),
                               ToRdfNode(triple.Predicate, dotNetGraph, blankCache),
                               ToRdfNode(triple.Object, dotNetGraph, blankCache));

        return dotNetGraph;
    }

    private static void ThrowIfViolations(Report report, TransactionOperation op, string aspectName)
    {
        if (report.Conforms) return;

        var violations = report.Results
            .Where(r => r.Severity?.ToString() == ShaclViolationIri ||
                        r.Severity?.ToString().EndsWith("#Violation", StringComparison.Ordinal) == true)
            .Select(r => new AspectViolation(
                FocusNodeIri: r.FocusNode?.ToString() ?? op.EntityIri,
                PathPredicate: r.ResultPath?.ToString(),
                Severity: r.Severity?.ToString() ?? ShaclViolationIri,
                Message: r.Message?.ToString() ?? "Constraint violated.",
                SourceShapeIri: r.SourceShape?.ToString()))
            .ToList();

        if (violations.Count > 0)
            throw new AspectViolationException(violations, op, aspectName);
    }

    // ------------------------------------------------------------------ Context pass helpers

    private static async ValueTask RunContextPassAsync(
        string sparql,
        ISparqlQueryStore queryStore,
        TransactionOperation op,
        string aspectName,
        CancellationToken ct)
    {
        var violations = new List<AspectViolation>();

        await foreach (var row in queryStore.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
        {
            // Any row returned by the constraint query is a violation.
            var focusNode = row.GetIri("focusNode") ?? op.EntityIri;
            var path = row.GetIri("path") ?? row.GetLiteral("path");
            var message = row.GetLiteral("message") ?? "Context constraint violated.";

            violations.Add(new AspectViolation(
                FocusNodeIri: focusNode,
                PathPredicate: path,
                Severity: ShaclViolationIri,
                Message: message,
                SourceShapeIri: null));
        }

        if (violations.Count > 0)
            throw new AspectViolationException(violations, op, aspectName);
    }

    // ------------------------------------------------------------------ Conversion helpers

    private static INode ToRdfNode(
        RdfTerm term,
        IGraph graph,
        Dictionary<string, IBlankNode> blankCache)
    {
        return term.Kind switch
        {
            RdfTermKind.Iri => graph.CreateUriNode(UriFactory.Create(term.Value)),
            RdfTermKind.BlankNode => blankCache.TryGetValue(term.Value, out var bn)
                ? bn
                : blankCache[term.Value] = graph.CreateBlankNode(term.Value),
            RdfTermKind.Literal when term.Language is not null =>
                graph.CreateLiteralNode(term.Value, term.Language),
            RdfTermKind.Literal when term.DatatypeIri is not null =>
                graph.CreateLiteralNode(term.Value, UriFactory.Create(term.DatatypeIri)),
            RdfTermKind.Literal =>
                graph.CreateLiteralNode(term.Value),
            _ => throw new NotSupportedException($"Unsupported RdfTermKind: {term.Kind}"),
        };
    }

    // ------------------------------------------------------------------ Kind helpers

    private static AspectKind OperationKind(TransactionOperation op) => op switch
    {
        CreateOperation<IEntity> => AspectKind.Create,
        DeleteOperation => AspectKind.Delete,
        EntityWriteOperation w when w.Mode == WriteMode.Replace => AspectKind.Update,
        EntityWriteOperation w when w.Mode == WriteMode.Create => AspectKind.Create,
        _ => throw new NotSupportedException($"Cannot determine AspectKind for {op.GetType().Name}"),
    };

    private static Type ResolveEntityType(TransactionOperation op) => op switch
    {
        EntityWriteOperation w => w.Entity.GetType(),
        DeleteOperation { EntityType: { } t } => t,
        _ => throw new NotSupportedException(
            $"Cannot resolve entity type for non-write operation {op.GetType().Name}. " +
            $"Delete operations with a non-NoOp aspect must use EntityTransaction.Delete<T>(iri, aspect)."),
    };
}
