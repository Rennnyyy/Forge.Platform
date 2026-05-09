using Forge.Aspects.Abstractions;
using Forge.Entity;
using VDS.RDF;
using Forge.Aspects.Query;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Rdf;
using Microsoft.Extensions.Options;

namespace Forge.Aspects;

/// <summary>
/// Decorates <see cref="IEntityStore"/> with read-aspect validation.
/// Intercepts <see cref="LoadAsync{T}"/>, <see cref="QueryByTypeAsync{T}"/>, and
/// <see cref="ISparqlQueryStore.ExecuteSelectAsync"/>; applies the ambient
/// <see cref="IQueryAspect"/> from <see cref="QueryAspectScope.Current"/>.
/// See Aspects ADR-0007.
/// </summary>
internal sealed class AspectEnforcingEntityStore : IEntityStore, ISparqlQueryStore, IInverseRefLoader
{
    private readonly IEntityStore _inner;
    private readonly IQueryAspectEngine _engine;
    private readonly IAspectStore _store;
    private readonly IRdfMapperRegistry _mappers;
    private readonly EntityRepositoryOptions _options;

    public AspectEnforcingEntityStore(
        IEntityStore inner,
        IQueryAspectEngine engine,
        IAspectStore store,
        IRdfMapperRegistry mappers,
        IOptions<EntityRepositoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mappers);
        _inner = inner;
        _engine = engine;
        _store = store;
        _mappers = mappers;
        _options = options.Value;
    }

    // ------------------------------------------------------------------ IEntityStore

    public string? NamedGraph => _inner.NamedGraph;

    public async ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var aspect = ResolveCurrentQueryAspect();
        if (aspect is not null && _inner is ISparqlQueryStore sparql)
            await _engine.ValidateAccessAsync(iri, aspect, sparql, cancellationToken)
                         .ConfigureAwait(false);

        var entity = await _inner.LoadAsync<T>(iri, cancellationToken).ConfigureAwait(false);

        if (entity is not null && aspect?.ResultShapeTtl is not null)
            ValidateEntityResultGraph(entity, typeof(T), aspect, iri);

        return entity;
    }

    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var aspect = ResolveCurrentQueryAspect();
        if (aspect is null)
            return _inner.QueryByTypeAsync<T>(cancellationToken);

        return QueryByTypeWithAspectAsync<T>(aspect, cancellationToken);
    }

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.SaveAsync(entity, mode, cancellationToken);

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(iri, cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ------------------------------------------------------------------ ISparqlQueryStore

    /// <summary>
    /// Delegates to the inner store's SPARQL execution, injecting the ambient aspect's
    /// <see cref="IQueryAspect.FilterWhere"/> into the query before dispatch. Throws
    /// <see cref="NotSupportedException"/> when the inner store is not SPARQL-capable.
    /// </summary>
    IAsyncEnumerable<SparqlResultRow> ISparqlQueryStore.ExecuteSelectAsync(
        string sparqlQuery, CancellationToken cancellationToken)
    {
        if (_inner is not ISparqlQueryStore sparql)
            throw new NotSupportedException(
                $"Inner entity store '{_inner.GetType().FullName}' does not implement " +
                $"ISparqlQueryStore. LINQ queries require a SPARQL-capable backend.");

        var aspect = ResolveCurrentQueryAspect();
        if (aspect is null)
            return sparql.ExecuteSelectAsync(sparqlQuery, cancellationToken);

        // The LINQ emitter uses ?s for the entity subject. Substitute ?entityIri → ?s
        // in the FilterWhere fragment so authors can write ?entityIri consistently
        // across both the per-entity access-gate path and the LINQ path.
        var linqAspect = AdaptFilterForLinq(aspect);
        var query = _engine.InjectFilter(sparqlQuery, linqAspect);
        return sparql.ExecuteSelectAsync(query, cancellationToken);
    }

    // ------------------------------------------------------------------ IEntityLoader / ICollectionLoader / IInverseRefLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => LoadAsync<T>(iri, cancellationToken);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => _inner is ICollectionLoader cl
            ? cl.LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken)
            : AsyncEnumerable.Empty<string>();

    ValueTask<string?> IInverseRefLoader.LoadInverseRefIriAsync(
        string targetIri, string predicate, CancellationToken cancellationToken)
        => _inner is IInverseRefLoader il
            ? il.LoadInverseRefIriAsync(targetIri, predicate, cancellationToken)
            : ValueTask.FromResult<string?>(null);

    IAsyncEnumerable<string> IInverseRefLoader.LoadInverseCollectionIrisAsync<T>(
        string targetIri, string predicate, CancellationToken cancellationToken)
        => _inner is IInverseRefLoader il
            ? il.LoadInverseCollectionIrisAsync<T>(targetIri, predicate, cancellationToken)
            : AsyncEnumerable.Empty<string>();

    // ------------------------------------------------------------------ Private helpers

    private async IAsyncEnumerable<T> QueryByTypeWithAspectAsync<T>(
        IQueryAspect aspect,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, IEntity
    {
        // If there is a result-shape check, accumulate all entity triples into one aggregate
        // graph and validate once after the scan (ADR-0007: aggregate granularity).
        Graph? aggregate = aspect.ResultShapeTtl is not null ? new Graph() : null;

        await foreach (var entity in _inner.QueryByTypeAsync<T>(cancellationToken).ConfigureAwait(false))
        {
            if (aggregate is not null)
                AppendEntityTriples(entity, typeof(T), aggregate);

            yield return entity;
        }

        if (aggregate is not null)
            _engine.ValidateResultGraph(aggregate, aspect, entityIri: null);
    }

    private void ValidateEntityResultGraph(IEntity entity, Type entityType, IQueryAspect aspect, string iri)
    {
        var aggregate = new Graph();
        AppendEntityTriples(entity, entityType, aggregate);
        _engine.ValidateResultGraph(aggregate, aspect, iri);
    }

    private IQueryAspect? ResolveCurrentQueryAspect()
        => QueryAspectScope.CurrentIri is { } iri ? _store.TryResolveQuery(iri) : null;

    private static IQueryAspect AdaptFilterForLinq(IQueryAspect aspect)
    {
        if (aspect.FilterWhere is not { } filter) return aspect;
        var adapted = filter.Replace("?entityIri", "?s", StringComparison.Ordinal);
        return ReferenceEquals(adapted, filter)
            ? aspect
            : new InlineTtlQueryAspect(aspect.Iri, adapted, aspect.ResultShapeTtl);
    }

    private void AppendEntityTriples(IEntity entity, Type entityType, Graph target)
    {
        var mapper = _mappers.ForEntityType(entityType);
        var typeIri = mapper.ResolveTypeIri(_options);
        var sink = new CollectingTripleSink();
        mapper.ProjectEntity(entity, sink, typeIri);

        var blankCache = new Dictionary<string, IBlankNode>(StringComparer.Ordinal);
        foreach (var triple in sink.Triples)
            target.Assert(
                ToRdfNode(triple.Subject, target, blankCache),
                ToRdfNode(triple.Predicate, target, blankCache),
                ToRdfNode(triple.Object, target, blankCache));
    }

    private static INode ToRdfNode(RdfTerm term, IGraph graph, Dictionary<string, IBlankNode> blankCache)
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
