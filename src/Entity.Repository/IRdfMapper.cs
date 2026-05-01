using Forge.Entity.Repository.Rdf;

namespace Forge.Entity.Repository;

/// <summary>
/// Non-generic mapper metadata facet, useful for decorators (validation, change-tracking)
/// and for mapper-registry plumbing.
/// </summary>
public interface IRdfMapper
{
    Type EntityType { get; }
    string? EntityPath { get; }
    string? PredicatePath { get; }

    /// <summary>The <c>rdf:type</c> IRI used for instances of <see cref="EntityType"/>.</summary>
    string ResolveTypeIri(EntityRepositoryOptions options);
}

/// <summary>
/// Per-type contract that maps an entity to and from an <see cref="RdfGraph"/>.
/// Implementations may be reflection-based (v1) or source-generated (v2).
/// </summary>
public interface IRdfMapper<T> : IRdfMapper where T : class, IEntity
{
    /// <summary>
    /// Build a <typeparamref name="T"/> instance from the triple closure of
    /// <paramref name="iri"/>. Returns null if the closure does not contain a recognizable
    /// instance of <typeparamref name="T"/> (e.g. wrong rdf:type, missing data).
    /// </summary>
    T? Hydrate(string iri, RdfGraph subjectGraph);

    /// <summary>
    /// Emit triples representing <paramref name="entity"/> into <paramref name="sink"/>.
    /// Implementations must include the <c>rdf:type</c> triple plus all opted-in data,
    /// owning refs and owning collections; inverse refs/collections must be skipped.
    /// </summary>
    void Project(T entity, IRdfTripleSink sink, string typeIri);
}
