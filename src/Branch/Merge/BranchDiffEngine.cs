using Forge.Repository;
using Forge.Repository.Mapping;
using Microsoft.Extensions.Options;

namespace Forge.Branch.Merge;

/// <summary>
/// Default implementation of <see cref="IBranchDiffEngine"/>. See Branch ADR-0004.
/// </summary>
/// <remarks>
/// <para>
/// Uses two SPARQL paths:
/// </para>
/// <list type="bullet">
///   <item><b>Multi-graph path</b> — when the raw backend implements
///     <see cref="IMultiGraphSparqlStore"/> (GraphDB), two SPARQL queries with
///     <c>GRAPH</c> clauses compare both named graphs in a single server round-trip
///     per query.</item>
///   <item><b>Scoped single-graph path</b> — when the backend implements
///     <see cref="ISparqlQueryStore"/> only (InMemory), one query per registered mapper
///     type is issued against each graph using <see cref="BranchScope.Use"/>. This works
///     because InMemory's Leviathan dataset is branch-scope-aware but does not support
///     <c>GRAPH</c> clauses.</item>
/// </list>
/// <para>
/// Deduplication by entity IRI: when entity-type inheritance produces the same IRI
/// from both a parent-type query and a child-type query, the entry with the longest
/// type IRI (most-derived) is kept.
/// </para>
/// </remarks>
internal sealed class BranchDiffEngine : IBranchDiffEngine
{
    private readonly IRdfMapperRegistry _registry;
    private readonly IEntityStore _rawStore;
    private readonly EntityRepositoryOptions _repoOptions;

    public BranchDiffEngine(
        IRdfMapperRegistry registry,
        IEntityStore rawStore,
        IOptions<EntityRepositoryOptions> repoOptions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(rawStore);
        ArgumentNullException.ThrowIfNull(repoOptions);
        _registry = registry;
        _rawStore = rawStore;
        _repoOptions = repoOptions.Value;
    }

    /// <inheritdoc/>
    public async Task<EntityGraphDelta> ComputeDiffAsync(
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGraphIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetGraphIri);

        Dictionary<string, EntityDeltaEntry> entries;

        if (_rawStore is IMultiGraphSparqlStore multiGraph)
            entries = await ComputeMultiGraphAsync(multiGraph, sourceGraphIri, targetGraphIri, cancellationToken)
                .ConfigureAwait(false);
        else if (_rawStore is ISparqlQueryStore singleGraph)
            entries = await ComputeScopedSparqlAsync(singleGraph, sourceGraphIri, targetGraphIri, cancellationToken)
                .ConfigureAwait(false);
        else
            entries = await ComputeQueryByTypeAsync(sourceGraphIri, targetGraphIri, cancellationToken)
                .ConfigureAwait(false);

        return new EntityGraphDelta(sourceGraphIri, targetGraphIri, [.. entries.Values]);
    }

    // ── Multi-graph SPARQL path (GraphDB) ─────────────────────────────────────

    private static async Task<Dictionary<string, EntityDeltaEntry>> ComputeMultiGraphAsync(
        IMultiGraphSparqlStore store,
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken ct)
    {
        var result = new Dictionary<string, EntityDeltaEntry>(StringComparer.Ordinal);

        // Query 1 — entities in source, absent from target (Added).
        var addedQuery = $$"""
            SELECT DISTINCT ?entity ?type WHERE {
              GRAPH <{{EscapeIri(sourceGraphIri)}}> { ?entity a ?type }
              FILTER NOT EXISTS { GRAPH <{{EscapeIri(targetGraphIri)}}> { ?entity ?p ?o } }
            }
            """;

        await foreach (var row in store.ExecuteSelectAsync(addedQuery, ct).ConfigureAwait(false))
        {
            var iri = row.GetIri("entity");
            var type = row.GetIri("type");
            if (iri is null || type is null) continue;
            AddOrKeepMostDerived(result, iri, type, EntityDeltaKind.Added);
        }

        // Query 2 — entities in source that also exist in target (Modified).
        var modifiedQuery = $$"""
            SELECT DISTINCT ?entity ?type WHERE {
              GRAPH <{{EscapeIri(sourceGraphIri)}}> { ?entity a ?type }
              FILTER EXISTS { GRAPH <{{EscapeIri(targetGraphIri)}}> { ?entity ?p ?o } }
            }
            """;

        await foreach (var row in store.ExecuteSelectAsync(modifiedQuery, ct).ConfigureAwait(false))
        {
            var iri = row.GetIri("entity");
            var type = row.GetIri("type");
            if (iri is null || type is null) continue;
            AddOrKeepMostDerived(result, iri, type, EntityDeltaKind.Modified);
        }

        return result;
    }

    // ── Scoped single-graph SPARQL path (InMemory) ───────────────────────────

    private async Task<Dictionary<string, EntityDeltaEntry>> ComputeScopedSparqlAsync(
        ISparqlQueryStore store,
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken ct)
    {
        var result = new Dictionary<string, EntityDeltaEntry>(StringComparer.Ordinal);

        foreach (var mapper in _registry.All)
        {
            var typeIri = mapper.ResolveTypeIri(_repoOptions);

            // Entity IRIs of this CLR type in the source graph.
            var sourceIris = await SelectIrisScopedAsync(store, typeIri, sourceGraphIri, ct)
                .ConfigureAwait(false);
            if (sourceIris.Count == 0) continue;

            // Entity IRI set of this CLR type in the target graph (for Added/Modified split).
            var targetIris = await SelectIrisScopedAsync(store, typeIri, targetGraphIri, ct)
                .ConfigureAwait(false);

            foreach (var iri in sourceIris)
            {
                var kind = targetIris.Contains(iri) ? EntityDeltaKind.Modified : EntityDeltaKind.Added;
                AddOrKeepMostDerived(result, iri, typeIri, kind);
            }
        }

        return result;
    }

    private static async Task<HashSet<string>> SelectIrisScopedAsync(
        ISparqlQueryStore store,
        string typeIri,
        string graphIri,
        CancellationToken ct)
    {
        using var _ = BranchScope.Use(graphIri);
        var query = $"SELECT DISTINCT ?s WHERE {{ ?s a <{EscapeIri(typeIri)}> }}";
        var iris = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var row in store.ExecuteSelectAsync(query, ct).ConfigureAwait(false))
        {
            var iri = row.GetIri("s");
            if (iri is not null) iris.Add(iri);
        }
        return iris;
    }

    // ── QueryByTypeAsync fallback (stores without SPARQL) ────────────────────

    private async Task<Dictionary<string, EntityDeltaEntry>> ComputeQueryByTypeAsync(
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken ct)
    {
        var result = new Dictionary<string, EntityDeltaEntry>(StringComparer.Ordinal);

        foreach (var mapper in _registry.All)
        {
            var typeIri = mapper.ResolveTypeIri(_repoOptions);

            var sourceIris = await CollectIrisByTypeAsync(mapper.EntityType, sourceGraphIri, ct)
                .ConfigureAwait(false);
            if (sourceIris.Count == 0) continue;

            var targetIris = await CollectIrisByTypeAsync(mapper.EntityType, targetGraphIri, ct)
                .ConfigureAwait(false);

            foreach (var iri in sourceIris)
            {
                var kind = targetIris.Contains(iri) ? EntityDeltaKind.Modified : EntityDeltaKind.Added;
                AddOrKeepMostDerived(result, iri, typeIri, kind);
            }
        }

        return result;
    }

    private static readonly System.Reflection.MethodInfo _queryByTypeGenericMethod =
        typeof(IEntityStore).GetMethod(nameof(IEntityStore.QueryByTypeAsync))!;

    private async Task<HashSet<string>> CollectIrisByTypeAsync(
        Type entityType, string graphIri, CancellationToken ct)
    {
        using var _ = BranchScope.Use(graphIri);
        var closed = _queryByTypeGenericMethod.MakeGenericMethod(entityType);
        var asyncEnum = closed.Invoke(_rawStore, [ct])!;

        // IAsyncEnumerable<T> → collect IRIs via non-generic iteration using GetAsyncEnumerator.
        var iris = new HashSet<string>(StringComparer.Ordinal);
        var enumeratorMethod = asyncEnum.GetType()
            .GetMethod("GetAsyncEnumerator")!;
        var enumerator = enumeratorMethod.Invoke(asyncEnum, [ct])!;
        var moveNextMethod = enumerator.GetType().GetMethod("MoveNextAsync")!;
        var currentProp = enumerator.GetType().GetProperty("Current")!;

        try
        {
            while (true)
            {
                var moveNextVt = moveNextMethod.Invoke(enumerator, null)!;
                var moveNextTask = ((dynamic)moveNextVt).AsTask() as System.Threading.Tasks.Task<bool>;
                if (moveNextTask is null || !await moveNextTask.ConfigureAwait(false)) break;
                var entity = currentProp.GetValue(enumerator) as Forge.Entity.IEntity;
                if (entity?.Iri is not null) iris.Add(entity.Iri);
            }
        }
        finally
        {
            if (enumerator is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
        }

        return iris;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="iri"/> → <paramref name="entry"/> to <paramref name="result"/>.
    /// When the IRI already exists, the entry with the longer type IRI (more-derived type)
    /// is kept to handle entity-type inheritance where the same IRI appears once per ancestor.
    /// </summary>
    private static void AddOrKeepMostDerived(
        Dictionary<string, EntityDeltaEntry> result,
        string iri,
        string typeIri,
        EntityDeltaKind kind)
    {
        if (result.TryGetValue(iri, out var existing))
        {
            if (typeIri.Length > existing.TypeIri.Length)
                result[iri] = new EntityDeltaEntry(iri, typeIri, kind);
        }
        else
        {
            result[iri] = new EntityDeltaEntry(iri, typeIri, kind);
        }
    }

    /// <summary>
    /// Escapes characters that could break out of a SPARQL angle-bracket IRI literal
    /// (<c>&lt;</c> and <c>&gt;</c>). Delegates to percent-encoding per RFC 3987.
    /// </summary>
    private static string EscapeIri(string iri) =>
        iri.Replace("<", "%3C", StringComparison.Ordinal)
           .Replace(">", "%3E", StringComparison.Ordinal);
}
