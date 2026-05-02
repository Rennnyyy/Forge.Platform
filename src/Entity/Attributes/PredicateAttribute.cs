namespace Forge.Entity;

/// <summary>
/// Declares the RDF predicate IRI for a scalar data property. Consumed by the
/// persistence layer (<c>Forge.Repository</c>) to map the property value
/// to a triple <c>&lt;subject&gt; &lt;predicate&gt; "value"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Resolution mirrors <see cref="OwningAttribute"/> / <see cref="InverseAttribute"/>:
/// if <see cref="Predicate"/> contains a colon it is treated as an absolute IRI;
/// otherwise it is resolved against
/// <see cref="EntityOptions.PredicateBaseIri"/> and the enclosing
/// <see cref="EntityAttribute.PredicatePath"/>.
/// </para>
/// <para>
/// Properties without this attribute are <em>not</em> persisted by the default mapper.
/// This makes RDF emission opt-in and prevents accidental coupling of C# property names
/// to graph semantics.
/// </para>
/// <para>
/// May be combined with <see cref="IdentityPartAttribute"/>: identity participation and
/// triple emission are independent concerns.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class PredicateAttribute : Attribute
{
    public string Predicate { get; }

    public PredicateAttribute(string predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
            throw new ArgumentException("Predicate is required.", nameof(predicate));
        Predicate = predicate;
    }
}
