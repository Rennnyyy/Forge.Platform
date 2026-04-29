namespace Forge.Entity;

/// <summary>
/// A managed collection of entity references. Supports async enumeration and async mutation;
/// the source generator wires inverse-side updates so adding to one side updates the other.
/// </summary>
public interface EntityRefCollection<T> : IAsyncEnumerable<T>
    where T : class, IEntity
{
    /// <summary>Number of items currently materialized in this collection.</summary>
    int LoadedCount { get; }

    /// <summary>Returns the IRIs of all members regardless of load state.</summary>
    IReadOnlyCollection<string> Iris { get; }

    ValueTask AddAsync(T entity, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(T entity, CancellationToken cancellationToken = default);

    ValueTask<bool> ContainsAsync(string iri, CancellationToken cancellationToken = default);

    /// <summary>True once the IRI list has been resolved from the backing store; always true for non-deferred collections.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Ensures the member IRI list is populated from the backing store.
    /// For non-deferred collections this is a no-op; for deferred ones it triggers a single
    /// call to the <see cref="ICollectionLoader"/> in the ambient <see cref="EntitySession"/>.
    /// </summary>
    ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default);
}
