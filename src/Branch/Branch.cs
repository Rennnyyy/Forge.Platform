using Forge.Entity;

namespace Forge.Branch;

/// <summary>
/// Represents a branch — an RDF named graph whose IRI equals this entity's IRI.
/// Stored in the management graph; never stored inside its own named graph.
/// See Branch ADR-0001.
/// </summary>
/// <remarks>
/// <para>
/// The entity IRI is <c>{EntityOptions.BaseIri}/branches/{Name}</c>. Because the IRI equals
/// the named graph IRI, deleting a <see cref="Branch"/> entity and dropping its graph can be
/// done atomically in a single <see cref="Forge.Repository.Transaction.EntityTransaction"/>:
/// </para>
/// <code>
/// await using var tx = new EntityTransaction(managementStore);
/// tx.Delete&lt;Branch&gt;(branch.Iri).DropGraph(branch.Iri);
/// await tx.CommitAsync();
/// </code>
/// </remarks>
[Entity(Path = "branches", PredicatePath = "branch")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
public partial class Branch
{
    /// <summary>
    /// Human-readable slug, e.g. <c>"main"</c> or <c>"feature-X"</c>.
    /// This is the sole identity part; the entity IRI becomes
    /// <c>{BaseIri}/branches/{Name}</c>, which equals the branch's named graph IRI.
    /// </summary>
    [IdentityPart(0)]
    [Predicate("name")]
    public partial string Name { get; init; }

    /// <summary>Optional human-readable description.</summary>
    [Predicate("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Timestamp of branch creation. Set by the caller at creation time; never mutated.
    /// </summary>
    [Predicate("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
