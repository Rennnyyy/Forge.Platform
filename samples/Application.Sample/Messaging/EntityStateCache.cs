namespace Forge.Application.Sample;

/// <summary>
/// In-process compacted view of the latest entity state per IRI.
/// Mirrors Kafka log compaction: each IRI key retains exactly one entry — the
/// most recently received event from <c>forge.entities.book.state</c>.
/// <para>
/// Unlike <see cref="EntityEventLog"/> (which is append-only history), this cache
/// is upserted on every inbound state event.  Exposed via
/// <c>GET /api/diagnostics/entity-events/latest?iri=…</c>.
/// </para>
/// Populated by <see cref="BookStateConsumerService"/>.
/// See sample ADR-0010 and root ADR-0021.
/// </summary>
public sealed class EntityStateCache
{
    private readonly Dictionary<string, EntityEventLogEntry> _latest = [];
    private readonly object _lock = new();

    /// <summary>Upserts <paramref name="entry"/>, replacing any previous value for the same IRI.</summary>
    public void Upsert(EntityEventLogEntry entry)
    {
        lock (_lock)
            _latest[entry.EntityIri] = entry;
    }

    /// <summary>
    /// Returns the latest known state for <paramref name="iri"/>, or
    /// <see langword="null"/> when no events have been received for that IRI.
    /// </summary>
    public EntityEventLogEntry? GetLatest(string iri)
    {
        lock (_lock)
            return _latest.GetValueOrDefault(iri);
    }

    /// <summary>Returns a snapshot of all latest-state entries, keyed by IRI.</summary>
    public IReadOnlyDictionary<string, EntityEventLogEntry> GetAll()
    {
        lock (_lock)
            return new Dictionary<string, EntityEventLogEntry>(_latest);
    }
}
