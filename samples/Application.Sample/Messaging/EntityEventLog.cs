namespace Forge.Application.Sample;

/// <summary>
/// Entry in the in-process entity event log used by the messaging demo.
/// </summary>
/// <param name="EntityIri">IRI of the entity that changed.</param>
/// <param name="TypeName">CLR type name of the entity.</param>
/// <param name="Operation">One of <c>Created</c>, <c>Updated</c>, <c>Deleted</c>.</param>
/// <param name="BranchIri">Named-graph IRI of the branch; empty string for the default branch.</param>
/// <param name="Topic">Message topic the event arrived on.</param>
/// <param name="TimestampUtc">UTC instant captured by the emitter.</param>
public sealed record EntityEventLogEntry(
    string EntityIri,
    string TypeName,
    string Operation,
    string BranchIri,
    string Topic,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Thread-safe in-process log of entity change events.
/// Populated by <see cref="BookEventConsumerService"/> and exposed via
/// <c>GET /api/diagnostics/entity-events</c>.
/// </summary>
public sealed class EntityEventLog
{
    private readonly List<EntityEventLogEntry> _entries = [];
    private readonly object _lock = new();

    /// <summary>Appends <paramref name="entry"/> to the log.</summary>
    public void Add(EntityEventLogEntry entry)
    {
        lock (_lock)
            _entries.Add(entry);
    }

    /// <summary>Returns a snapshot of all entries.</summary>
    public IReadOnlyList<EntityEventLogEntry> GetAll()
    {
        lock (_lock)
            return [.. _entries];
    }

    /// <summary>
    /// Returns all entries whose <see cref="EntityEventLogEntry.EntityIri"/> matches
    /// <paramref name="iri"/>, in arrival order.
    /// </summary>
    public IReadOnlyList<EntityEventLogEntry> GetByIri(string iri)
    {
        lock (_lock)
            return _entries.Where(e => e.EntityIri == iri).ToList();
    }
}
