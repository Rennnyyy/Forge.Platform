namespace Forge.Repository;

/// <summary>
/// Thrown when an attempt is made to create an entity whose IRI already exists in the
/// target named graph and the write mode does not permit replacing existing data.
/// Maps to HTTP 409 Conflict at the endpoint layer.
/// </summary>
public sealed class EntityAlreadyExistsException : Exception
{
    /// <summary>The IRI of the entity that already exists.</summary>
    public string EntityIri { get; }

    public EntityAlreadyExistsException(string entityIri)
        : base($"Entity '{entityIri}' already exists in the target named graph.")
    {
        EntityIri = entityIri;
    }
}
