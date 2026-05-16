using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Entity;
using Forge.Operations;

namespace Forge.Structure;

/// <summary>
/// A first-class entity representing a directed, condition-bearing edge from one
/// <see cref="IStructure"/> node (<see cref="ParentStructureIri"/>) to another
/// (<see cref="ChildStructureIri"/>).
/// <para>
/// Multiple <see cref="Usage"/> entities may exist between the same parent and child,
/// expressing OR semantics: the child is included in a configured tree if <em>any</em>
/// of its Usage edges to the parent is satisfied by the current
/// <see cref="StructureConfiguration"/>. Conditions within a single Usage are evaluated
/// with AND (see <see cref="ConditionSet"/>). See Structure ADR-0001 and ADR-0002.
/// </para>
/// <para>
/// <see cref="Conditions"/> is the HTTP-friendly structured property. It is backed by
/// <see cref="ConditionsJson"/>, a JSON string literal stored via <c>[Predicate]</c>.
/// This ensures that conditions round-trip correctly through both the InMemory and GraphDB
/// RDF backends without requiring a custom <c>IRdfMapper&lt;Usage&gt;</c>.
/// </para>
/// </summary>
[Entity(Path = "structure-usages", PredicatePath = "usage")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Usage
{
    private static readonly JsonSerializerOptions _conditionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>IRI of the parent <see cref="IStructure"/> node.</summary>
    [Predicate("parentStructureIri")]
    public string ParentStructureIri { get; set; } = string.Empty;

    /// <summary>IRI of the child <see cref="IStructure"/> node.</summary>
    [Predicate("childStructureIri")]
    public string ChildStructureIri { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized form of <see cref="Conditions"/>. Stored in the RDF graph as a
    /// string literal via <c>[Predicate]</c>. Use <see cref="Conditions"/> for structured
    /// read/write access; do not set this property directly from application code.
    /// </summary>
    [Predicate("conditionsJson")]
    [JsonIgnore]
    public string? ConditionsJson { get; set; }
    /// <summary>
    /// Optional IRI of the named graph (branch or snapshot) from which the child
    /// structure subtree and its associated context (geometry, metadata, etc.) should
    /// be resolved. When non-null, <see cref="GetConfiguredTreeHandler"/> annotates the
    /// child's <see cref="StructureNodeDto.SnapshotBranchIri"/> so callers can switch
    /// their read context accordingly. Null means "inherit the query's default branch."
    /// See Structure ADR-0006.
    /// </summary>
    [Predicate("snapshotIri")]
    public string? SnapshotIri { get; set; }
    /// <summary>
    /// The set of applicability conditions (AND semantics). Backed by
    /// <see cref="ConditionsJson"/>: the setter serializes to JSON, the getter
    /// deserializes on demand. An absent or empty JSON value yields
    /// <see cref="ConditionSet.Empty"/> (unconditionally active).
    /// </summary>
    public ConditionSet Conditions
    {
        get => ConditionsJson is null or ""
            ? ConditionSet.Empty
            : new ConditionSet(
                JsonSerializer.Deserialize<IReadOnlyList<IStructureCondition>>(ConditionsJson, _conditionJsonOptions)!);
        set => ConditionsJson = value.Conditions.Count == 0
            ? null
            : JsonSerializer.Serialize(value.Conditions);
    }
}

