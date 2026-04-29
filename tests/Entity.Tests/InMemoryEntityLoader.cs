using Forge.Entity;

namespace Forge.Entity.Tests;

/// <summary>
/// Test loader that resolves entities from an in-memory dictionary keyed by IRI.
/// Also implements <see cref="ICollectionLoader"/> so deferred collections can be exercised
/// in tests by pre-registering expected IRI sets.
/// Real implementations will hit a triple store / repository.
/// </summary>
internal sealed class InMemoryEntityLoader : IEntityLoader, ICollectionLoader
{
    private readonly Dictionary<string, IEntity> _byIri = new(StringComparer.Ordinal);
    private readonly Dictionary<(string OwnerIri, string Predicate), List<string>> _collections = new();

    public InMemoryEntityLoader Register(IEntity entity)
    {
        _byIri[entity.Iri] = entity;
        return this;
    }

    /// <summary>Pre-register the IRI list for a deferred collection.</summary>
    public InMemoryEntityLoader RegisterCollection(string ownerIri, string predicate, params string[] memberIris)
    {
        _collections[(ownerIri, predicate)] = new List<string>(memberIris);
        return this;
    }

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        _byIri.TryGetValue(iri, out var found);
        return ValueTask.FromResult(found as T);
    }

#pragma warning disable CS1998 // async iterator with no await is intentional
    public async IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        if (!_collections.TryGetValue((ownerIri, predicate), out var iris)) yield break;
        foreach (var iri in iris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return iri;
        }
    }
#pragma warning restore CS1998
}
