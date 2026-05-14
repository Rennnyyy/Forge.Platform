namespace Forge.Branch.Merge;

/// <summary>
/// One entry in an <see cref="EntityGraphDelta"/>: a single entity that differs between
/// the source and target named graphs. See Branch ADR-0004.
/// </summary>
/// <param name="EntityIri">The IRI of the entity.</param>
/// <param name="TypeIri">
/// The most-derived <c>rdf:type</c> IRI for this entity as found in the source graph.
/// Used by <see cref="IMergePlanner"/> to resolve the CLR type via
/// <c>IRdfMapperRegistry.ForTypeIri</c>.
/// </param>
/// <param name="Kind">Whether this entity is new (<see cref="EntityDeltaKind.Added"/>) or
/// existing in both graphs (<see cref="EntityDeltaKind.Modified"/>).</param>
public sealed record EntityDeltaEntry(
    string EntityIri,
    string TypeIri,
    EntityDeltaKind Kind);
