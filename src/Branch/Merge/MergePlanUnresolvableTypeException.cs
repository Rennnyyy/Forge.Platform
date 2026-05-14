namespace Forge.Branch.Merge;

/// <summary>
/// Thrown when the <see cref="IMergePlanner"/> cannot resolve a CLR type for an
/// <c>rdf:type</c> IRI present in the <see cref="EntityGraphDelta"/>. The entity type
/// must be registered via the mapper registry at DI time.
/// See Branch ADR-0006.
/// </summary>
public sealed class MergePlanUnresolvableTypeException : InvalidOperationException
{
    /// <summary>The <c>rdf:type</c> IRI for which no mapper was found.</summary>
    public string TypeIri { get; }

    /// <inheritdoc/>
    public MergePlanUnresolvableTypeException(string typeIri)
        : base($"No mapper registered for rdf:type IRI '{typeIri}'. Ensure the entity type is registered via AddForgeEntityRepository() before calling MergeAsync.")
    {
        TypeIri = typeIri;
    }
}
