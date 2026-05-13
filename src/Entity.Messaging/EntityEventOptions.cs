namespace Forge.Entity.Messaging;

/// <summary>
/// Per-entity type configuration for entity event emission.
/// Passed to the <c>AddForgeEntityEvent&lt;TEntity&gt;</c> DI registration callback.
/// See root ADR-0021.
/// </summary>
public sealed class EntityEventOptions
{
    /// <summary>
    /// RDF type IRI for the entity (e.g. <c>"https://forge-it.net/types/artists"</c>).
    /// Required — used on every emitted <see cref="EntityChangedEnvelope{TDto}"/>.
    /// </summary>
    public string TypeIri { get; set; } = string.Empty;

    /// <summary>
    /// Kafka topic for the full-history append log.
    /// Defaults to <c>forge.entities.{type-name}.history</c> where <c>type-name</c> is the
    /// lower-kebab-cased CLR simple name of <c>TEntity</c>.
    /// </summary>
    public string HistoryTopic { get; set; } = string.Empty;

    /// <summary>
    /// Kafka topic for the compacted latest-state log.
    /// Defaults to <c>forge.entities.{type-name}.state</c>.
    /// </summary>
    public string StateTopic { get; set; } = string.Empty;
}
