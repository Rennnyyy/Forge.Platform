using Forge.Entity;
using Forge.Repository.Rdf;

namespace Forge.Repository;

/// <summary>
/// Backend boundary for the Repository slice. One implementation per RDF store
/// (in-memory dotNetRDF, Ontotext GraphDB HTTP, …). The store is type-agnostic;
/// it consults the <see cref="IRdfMapperRegistry"/> to materialize / project entities.
/// </summary>
/// <remarks>
/// Implements <see cref="IEntityLoader"/> and <see cref="ICollectionLoader"/> so that
/// an open <see cref="EntitySession"/> bound to the store automatically participates in
/// lazy-ref resolution and deferred-collection loading.
/// </remarks>
public interface IEntityStore : IEntityLoader, ICollectionLoader, IAsyncDisposable
{
    /// <summary>Load a single entity by IRI, or null if absent.</summary>
    new ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity;

    /// <summary>Persist an entity. See <see cref="WriteMode"/>.</summary>
    ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity;

    /// <summary>Delete every triple whose subject is the given IRI.</summary>
    ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default);

    /// <summary>Stream all entities of type <typeparamref name="T"/> by their <c>rdf:type</c>.</summary>
    IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity;

    /// <summary>Resolve the named graph the store reads/writes in, if any.</summary>
    string? NamedGraph { get; }
}
