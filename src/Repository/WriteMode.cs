namespace Forge.Repository;

/// <summary>How <see cref="IEntityStore.SaveAsync{T}"/> writes an entity to the store.</summary>
public enum WriteMode
{
    /// <summary>
    /// Assert the new triples without first deleting existing ones for this subject.
    /// Throws if a triple set already exists for the entity's IRI.
    /// </summary>
    Create = 0,

    /// <summary>
    /// Full PUT semantics: delete every triple where the entity's IRI is the subject in the
    /// configured named graph, then assert the projected triples. Idempotent.
    /// </summary>
    Replace = 1,
}
