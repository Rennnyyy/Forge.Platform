namespace Forge.Entity;

/// <summary>
/// Marks a partial class as an Entity. The source generator emits the second
/// partial half implementing <see cref="IEntity"/> and inheriting <see cref="EntityBase"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityAttribute : Attribute
{
    /// <summary>
    /// Optional path segment inserted between the global <see cref="EntityOptions.BaseIri"/>
    /// and the identity-derived suffix, e.g. <c>"orders"</c> in
    /// <c>https://forge.example/orders/{identity}</c>.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Optional sub-path appended to <see cref="EntityOptions.PredicateBaseIri"/> when resolving
    /// short predicate names on <c>[Owning]</c> / <c>[Inverse]</c>.
    /// Defaults to <see cref="Path"/> when not set.
    /// e.g. <c>"order"</c> in <c>https://forge.example/predicates/order/{predicate}</c>.
    /// </summary>
    public string? PredicatePath { get; init; }
}
