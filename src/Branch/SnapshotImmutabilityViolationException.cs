namespace Forge.Branch;

/// <summary>
/// Thrown when a transaction operation attempts to mutate the content of a frozen snapshot
/// named graph. The only permitted write against a snapshot is an atomic
/// <c>Delete</c> + <c>DropGraph</c> pair that removes the snapshot entirely
/// (see Branch ADR-0002).
/// </summary>
public sealed class SnapshotImmutabilityViolationException : InvalidOperationException
{
    /// <summary>The IRI of the snapshot named graph that the operation targeted.</summary>
    public string SnapshotIri { get; }

    /// <summary>Initializes a new instance with a formatted message.</summary>
    internal SnapshotImmutabilityViolationException(string message, string snapshotIri)
        : base(message)
    {
        SnapshotIri = snapshotIri;
    }

    /// <summary>Factory for a rejected Create/Update operation targeting a snapshot graph.</summary>
    internal static SnapshotImmutabilityViolationException WriteBlocked(string snapshotIri) =>
        new($"Cannot write to the snapshot graph '{snapshotIri}'. " +
            "Snapshot named graphs are immutable after creation. " +
            "To remove a snapshot, issue a paired Delete + DropGraph in the same transaction.",
            snapshotIri);

    /// <summary>Factory for a rejected DropGraph targeting a snapshot graph outside a delete pair.</summary>
    internal static SnapshotImmutabilityViolationException DropGraphBlocked(string snapshotIri) =>
        new($"Cannot drop the snapshot graph '{snapshotIri}' without also deleting the Snapshot entity " +
            "in the same transaction. Issue a Delete + DropGraph pair to remove a snapshot atomically.",
            snapshotIri);
}
