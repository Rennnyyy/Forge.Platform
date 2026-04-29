namespace Forge.Entity;

/// <summary>
/// Marks the owning side of an entity reference. The owning side persists the link;
/// the inverse side is read-only and reflects the owning side's value.
/// <para/>
/// <see cref="Predicate"/> is required and identifies the relation in the graph.
/// If it contains a colon it is treated as an absolute IRI; otherwise it is resolved
/// against <see cref="EntityOptions.PredicateBaseIri"/> and the enclosing
/// <see cref="EntityAttribute.PredicatePrefix"/> (if any).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class OwningAttribute : Attribute
{
    public string Predicate { get; }

    /// <summary>
    /// When <see langword="true"/>, the generated collection defers its IRI list load until
    /// first access and resolves through the ambient <see cref="ICollectionLoader"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool Lazy { get; set; }

    public OwningAttribute(string predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
            throw new ArgumentException("Predicate is required.", nameof(predicate));
        Predicate = predicate;
    }
}

/// <summary>
/// Marks the inverse side of an entity reference and names the owning property on the other entity.
/// The generator wires both sides so assigning to the owning property updates the inverse automatically.
/// An inverse property is read-only at the public API level; mutate via the owning side.
/// <para/>
/// The <see cref="Predicate"/> is required even though it is logically the inverse direction
/// of the owning side's predicate; declaring it explicitly avoids cross-class lookups during emission
/// and survives property renames.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class InverseAttribute : Attribute
{
    /// <summary>Name of the owning property on the other entity (use <c>nameof</c>).</summary>
    public string PropertyName { get; }

    public string Predicate { get; }

    /// <summary>
    /// When <see langword="true"/>, the generated inverse collection defers its IRI list load
    /// until first access and resolves through the ambient <see cref="ICollectionLoader"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool Lazy { get; set; }

    public InverseAttribute(string propertyName, string predicate)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("PropertyName is required.", nameof(propertyName));
        if (string.IsNullOrWhiteSpace(predicate))
            throw new ArgumentException("Predicate is required.", nameof(predicate));
        PropertyName = propertyName;
        Predicate = predicate;
    }
}

/// <summary>
/// Semantic marker that a property must have a value at validation time.
/// Currently informational only; no runtime check is wired up yet.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class RequiredAttribute : Attribute { }

/// <summary>
/// Marks an entity class as an enumeration: a sealed entity with a fixed set of
/// public static readonly instances declared on the class itself (named individuals, SKOS-like).
/// The generator validates that all instances share a single namespace and have stable IRIs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EnumerationAttribute : Attribute { }
