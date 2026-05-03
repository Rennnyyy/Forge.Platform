using System.Text;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Rdf;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Options;
using VDS.RDF;
using VDS.RDF.Shacl.Validation;

namespace Forge.Aspects.Operation;

/// <summary>
/// Default <see cref="IOperationAspectEngine"/>. Implements the two-pass pipeline defined in
/// Aspects ADR-0001: Local pass (SHACL graph validate) then Context pass (SPARQL SELECT).
/// Resolves the concrete <see cref="IOperationAspect"/> from <see cref="IAspectStore"/> using
/// the operation's <see cref="TransactionOperation.AspectIri"/>.
/// </summary>
internal sealed class OperationAspectEngine : IOperationAspectEngine
{
    private static readonly string ShaclViolationIri = "http://www.w3.org/ns/shacl#Violation";

    private readonly IAspectStore _store;
    private readonly IShapeCache _cache;
    private readonly IRdfMapperRegistry _mappers;
    private readonly EntityRepositoryOptions _options;

    public OperationAspectEngine(
        IAspectStore store,
        IShapeCache cache,
        IRdfMapperRegistry mappers,
        IOptions<EntityRepositoryOptions> options)
    {
        _store = store;
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
        if (operation.AspectIri == Aspect.NoOpIri)
            return;

        var operationAspect = _store.ResolveOperation(operation.AspectIri);

        // 1. Local pass
        if (operationAspect.LocalShapeTtl is { } localTtl)
        {
            var localGraph = BuildLocalGraph(operation);
            var shapesGraph = _cache.GetOrParse(localTtl);
            var report = shapesGraph.Validate(localGraph);
            ThrowIfViolations(report, operation, operationAspect.Iri);
        }

        // 2. Context pass
        if (operationAspect.ContextWhere is { } whereBody)
        {
            var fullQuery =
                $"SELECT ?focusNode ?message ?path WHERE {{ " +
                $"VALUES ?entityIri {{ <{operation.EntityIri}> }} {whereBody} }}";
            await RunContextPassAsync(fullQuery, queryStore, operation, operationAspect.Iri, cancellationToken)
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

    private static void ThrowIfViolations(Report report, TransactionOperation op, string aspectIri)
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
            throw new AspectViolationException(violations, op, aspectIri);
    }

    // ------------------------------------------------------------------ Context pass helpers

    private static async ValueTask RunContextPassAsync(
        string sparql,
        ISparqlQueryStore queryStore,
        TransactionOperation op,
        string aspectIri,
        CancellationToken ct)
    {
        var violations = new List<AspectViolation>();

        await foreach (var row in queryStore.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
        {
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
            throw new AspectViolationException(violations, op, aspectIri);
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
}
