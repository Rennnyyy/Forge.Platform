using Forge.Entity;
using Forge.Operations;

namespace Forge.Structure;

/// <summary>
/// Classifies a single axis of variability that can be used in <see cref="ConditionSet"/>
/// entries on a <see cref="Usage"/> edge.
/// <para>
/// Each <see cref="Dimension"/> has a <see cref="DimensionType"/> that constrains which
/// condition class applies:
/// <list type="table">
///   <item><term><see cref="DimensionType.Flag"/></term>
///         <description>Boolean toggle — use <see cref="FlagOptionCondition"/>.</description></item>
///   <item><term><see cref="DimensionType.Enumeration"/></term>
///         <description>One-of-N named value — use <see cref="EnumerationOptionCondition"/>.</description></item>
///   <item><term><see cref="DimensionType.Time"/></term>
///         <description>Date/time window — use <see cref="TimeCondition"/>.</description></item>
/// </list>
/// The dimension's <see cref="IEntity.Iri"/> is what callers place in
/// <see cref="FlagOptionCondition.DimensionIri"/>,
/// <see cref="EnumerationOptionCondition.DimensionIri"/>, and in the
/// <see cref="StructureConfiguration.Options"/> dictionary.
/// </para>
/// <remarks>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/structure-dimensions</term><description>Create</description></item>
///   <item><term>GET    api/entities/structure-dimensions</term><description>List</description></item>
///   <item><term>GET    api/entities/structure-dimensions?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/structure-dimensions?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/structure-dimensions?iri=…</term><description>Delete</description></item>
/// </list>
/// See Structure ADR-0005.
/// </remarks>
[Entity(Path = "structure-dimensions")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Dimension
{
    /// <summary>Human-readable name of this dimension.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The kind of value this dimension carries. Constrains which
    /// <see cref="IStructureCondition"/> subtype may reference it.
    /// </summary>
    [Predicate("dimensionType")]
    public DimensionType Type { get; set; }

    /// <summary>Optional free-text description.</summary>
    [Predicate("description")]
    public string? Description { get; set; }
}

/// <summary>The possible value kinds for a <see cref="Dimension"/>.</summary>
public enum DimensionType
{
    /// <summary>Boolean toggle; use <see cref="FlagOptionCondition"/>.</summary>
    Flag,

    /// <summary>One-of-N named IRI value; use <see cref="EnumerationOptionCondition"/>.</summary>
    Enumeration,

    /// <summary>Date/time window; use <see cref="TimeCondition"/>.</summary>
    Time
}
