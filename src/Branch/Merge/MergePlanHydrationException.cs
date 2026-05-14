namespace Forge.Branch.Merge;

/// <summary>
/// Thrown when the <see cref="IMergePlanner"/> cannot load an entity from the source
/// graph during the hydration step. This typically indicates a race condition where the
/// source graph was modified between the diff and the plan steps.
/// See Branch ADR-0006.
/// </summary>
public sealed class MergePlanHydrationException : InvalidOperationException
{
    /// <summary>The IRI of the entity that could not be loaded.</summary>
    public string EntityIri { get; }

    /// <inheritdoc/>
    public MergePlanHydrationException(string entityIri)
        : base($"Entity IRI '{entityIri}' could not be loaded from the source graph during merge plan hydration.")
    {
        EntityIri = entityIri;
    }
}
