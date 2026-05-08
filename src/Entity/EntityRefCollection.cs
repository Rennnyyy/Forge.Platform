namespace Forge.Entity;

/// <summary>
/// Non-generic interface exposing the resolution state of an <see cref="EntityRefCollection{T}"/>.
/// Used by the JSON serializer to suppress unresolved lazy inverse collections from HTTP responses,
/// and by the mapper to mark a collection resolved after population (ADR-0018).
/// </summary>
public interface IEntityRefCollectionState
{
    /// <summary>True once the IRI list has been resolved; always true for non-deferred eager collections.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Marks the collection as resolved. For non-deferred collections this is a no-op.
    /// For <see cref="LazyInverseEntityRefCollectionImpl{T}"/> this flips <see cref="IsResolved"/>
    /// to <see langword="true"/> so the key appears in HTTP responses after mapper population.
    /// </summary>
    ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A managed collection of entity references. Supports async enumeration and async mutation;
/// the source generator wires inverse-side updates so adding to one side updates the other.
/// </summary>
public interface EntityRefCollection<T> : IEntityRefCollectionState, IAsyncEnumerable<T>
    where T : class, IEntity
{
    /// <summary>Number of items currently materialized in this collection.</summary>
    int LoadedCount { get; }

    /// <summary>Returns the IRIs of all members regardless of load state.</summary>
    IReadOnlyCollection<string> Iris { get; }

    ValueTask AddAsync(T entity, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(T entity, CancellationToken cancellationToken = default);

    ValueTask<bool> ContainsAsync(string iri, CancellationToken cancellationToken = default);
}
