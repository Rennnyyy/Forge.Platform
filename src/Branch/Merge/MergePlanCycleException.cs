namespace Forge.Branch.Merge;

/// <summary>
/// Thrown when the <see cref="IMergePlanner"/> detects a circular owning-dependency
/// among the entities in the merge batch. Circular owning is forbidden by the mapper;
/// this exception indicates a corrupted or externally mutated entity graph.
/// See Branch ADR-0006.
/// </summary>
public sealed class MergePlanCycleException : InvalidOperationException
{
    /// <summary>The IRIs of all entities that participate in the cycle.</summary>
    public IReadOnlyList<string> CycleIris { get; }

    /// <inheritdoc/>
    public MergePlanCycleException(IReadOnlyList<string> cycleIris)
        : base($"Circular owning dependency detected in merge batch among: {string.Join(", ", cycleIris)}")
    {
        CycleIris = cycleIris;
    }
}
