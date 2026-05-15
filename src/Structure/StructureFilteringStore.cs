using System.Runtime.CompilerServices;
using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Rdf;

namespace Forge.Structure;

/// <summary>
/// Decorates <see cref="IEntityStore"/> with transparent <see cref="StructureScope"/>-based
/// filtering of <see cref="Usage"/> query results.
/// <para>
/// When <see cref="StructureScope.Current"/> is not null and <typeparamref name="T"/> is
/// <see cref="Usage"/>, <see cref="QueryByTypeAsync{T}"/> yields only the
/// <see cref="Usage"/> entities whose <see cref="Usage.Conditions"/> are satisfied by the
/// active <see cref="StructureConfiguration"/>. All other methods and all calls where no
/// scope is active are delegated to the inner store unchanged.
/// </para>
/// See Variant ADR-0004.
/// </summary>
internal sealed class StructureFilteringStore : IEntityStore, ISparqlQueryStore, IInverseRefLoader
{
    private readonly IEntityStore _inner;

    public StructureFilteringStore(IEntityStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    // ------------------------------------------------------------------ IEntityStore

    public string? NamedGraph => _inner.NamedGraph;

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.LoadAsync<T>(iri, cancellationToken);

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.SaveAsync(entity, mode, cancellationToken);

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(iri, cancellationToken);

    /// <summary>
    /// When <typeparamref name="T"/> is <see cref="Usage"/> and
    /// <see cref="StructureScope.Current"/> is not <c>null</c>, filters the inner results
    /// to only those <see cref="Usage"/> entities whose
    /// <see cref="Usage.Conditions"/> are satisfied by the active configuration.
    /// All other cases delegate unfiltered.
    /// </summary>
    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var config = StructureScope.Current;
        if (config is null || typeof(T) != typeof(Usage))
            return _inner.QueryByTypeAsync<T>(cancellationToken);

        return FilteredUsagesAsync<T>(config, cancellationToken);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ------------------------------------------------------------------ IEntityLoader (explicit)

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => ((IEntityLoader)_inner).LoadAsync<T>(iri, cancellationToken);

    // ------------------------------------------------------------------ ICollectionLoader (explicit)

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken);

    // ------------------------------------------------------------------ ISparqlQueryStore

    IAsyncEnumerable<SparqlResultRow> ISparqlQueryStore.ExecuteSelectAsync(
        string sparql, CancellationToken cancellationToken)
        => _inner is ISparqlQueryStore sq
            ? sq.ExecuteSelectAsync(sparql, cancellationToken)
            : throw new NotSupportedException(
                $"Inner entity store '{_inner.GetType().FullName}' does not implement " +
                "ISparqlQueryStore. SPARQL queries require a SPARQL-capable backend.");

    // ------------------------------------------------------------------ IInverseRefLoader

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

    // ------------------------------------------------------------------ private helpers

    private async IAsyncEnumerable<T> FilteredUsagesAsync<T>(
        StructureConfiguration config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, IEntity
    {
        // Safety: this method is only called when typeof(T) == typeof(Usage).
        // Every entity returned from the inner store is a Usage, so the pattern match
        // always succeeds. The cast of the entire enumerable is not possible at compile
        // time due to generic constraints, so we use the runtime-safe 'is Usage' check.
        await foreach (var entity in _inner.QueryByTypeAsync<T>(cancellationToken)
                                           .WithCancellation(cancellationToken)
                                           .ConfigureAwait(false))
        {
            if (entity is Usage usage && usage.Conditions.IsSatisfiedBy(config))
                yield return entity;
        }
    }
}
