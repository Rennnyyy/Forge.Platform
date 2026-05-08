namespace Forge.Entity;

/// <summary>
/// Optional extension to <see cref="IEntityLoader"/> that resolves the owning entity's IRI
/// for an inverse single-reference (<c>[Inverse]</c> on <c>EntityRef&lt;T&gt;?</c>).
/// Implement alongside <see cref="IEntityLoader"/> and <see cref="ICollectionLoader"/> to
/// enable inverse single-ref hydration in <c>ReflectionRdfMapper&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// See ADR-0017. Only single inverse refs are handled here; inverse collections are handled
/// by <see cref="DeferredEntityRefCollectionImpl{T}"/> via <see cref="ICollectionLoader"/>.
/// </remarks>
public interface IInverseRefLoader
{
    /// <summary>
    /// Returns the IRI of the single entity that points to <paramref name="targetIri"/>
    /// via <paramref name="predicate"/> (absolute IRI). Returns <see langword="null"/> if
    /// no such owning entity exists in the store.
    /// </summary>
    ValueTask<string?> LoadInverseRefIriAsync(
        string targetIri,
        string predicate,
        CancellationToken cancellationToken = default);
}
