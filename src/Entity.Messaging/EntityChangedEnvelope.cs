using Forge.Execution;

namespace Forge.Entity.Messaging;

/// <summary>
/// Wire contract carried in every entity change event envelope.
/// <para>
/// Published to two Kafka topics per entity type (see ADR-0021):
/// <list type="bullet">
///   <item><c>forge.entities.{typeName}.history</c> — infinite-retention append log.</item>
///   <item><c>forge.entities.{typeName}.state</c> — compacted log keyed by <see cref="Iri"/>.</item>
/// </list>
/// </para>
/// <typeparam name="TDto">
/// The DTO (or entity) type representing the entity's state.
/// <c>null</c> when <see cref="Operation"/> is <see cref="EntityChangeOperation.Deleted"/>.
/// </typeparam>
/// </summary>
/// <param name="Iri">The IRI (identity) of the entity that changed.</param>
/// <param name="TypeName">CLR simple name of the entity type (e.g. <c>"Artist"</c>).</param>
/// <param name="TypeIri">RDF type IRI of the entity (e.g. <c>"https://forge-it.net/types/artists"</c>).</param>
/// <param name="Operation">The type of mutation.</param>
/// <param name="BranchIri">
/// The named-graph IRI of the branch in which the change occurred.
/// Empty string when the store has no branch scope.
/// </param>
/// <param name="Dto">
/// The entity state at the time of the event.
/// <c>null</c> when <paramref name="Operation"/> is <see cref="EntityChangeOperation.Deleted"/>.
/// </param>
/// <param name="Correlation">Execution correlation propagated from the originating dispatch.</param>
/// <param name="TimestampUtc">UTC instant at which the event was emitted.</param>
public sealed record EntityChangedEnvelope<TDto>(
    string Iri,
    string TypeName,
    string TypeIri,
    EntityChangeOperation Operation,
    string BranchIri,
    TDto? Dto,
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc);
