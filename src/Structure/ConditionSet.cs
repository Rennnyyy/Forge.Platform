using System.Text.Json.Serialization;

namespace Forge.Structure;

/// <summary>
/// A value object that holds an ordered list of <see cref="IStructureCondition"/> entries
/// and evaluates them as a logical AND: the set is satisfied only when every individual
/// condition is satisfied. An empty set is always satisfied (vacuous truth).
/// <para>
/// OR semantics across <see cref="Usage"/> edges are expressed by creating multiple
/// parallel <see cref="Usage"/> entities between the same parent and child, each with
/// its own <see cref="ConditionSet"/>. See Variant ADR-0001 and ADR-0002.
/// </para>
/// </summary>
public sealed class ConditionSet
{
    /// <summary>A <see cref="ConditionSet"/> with no conditions; always satisfied.</summary>
    public static readonly ConditionSet Empty = new([]);

    private readonly IReadOnlyList<IStructureCondition> _conditions;

    /// <param name="conditions">The conditions to include. Must not be null.</param>
    [JsonConstructor]
    public ConditionSet(IReadOnlyList<IStructureCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        _conditions = conditions;
    }

    /// <summary>The conditions in this set, in declaration order.</summary>
    public IReadOnlyList<IStructureCondition> Conditions => _conditions;

    /// <summary>
    /// Returns <see langword="true"/> when every condition in this set is satisfied by
    /// <paramref name="config"/>, or when the set is empty.
    /// </summary>
    public bool IsSatisfiedBy(StructureConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        foreach (var condition in _conditions)
        {
            if (!condition.IsSatisfiedBy(config))
                return false;
        }
        return true;
    }
}
