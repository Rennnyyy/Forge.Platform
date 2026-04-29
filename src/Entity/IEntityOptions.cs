namespace Forge.Entity;

/// <summary>
/// Read-only view of entity configuration used by IRI-materialization code and
/// the <see cref="Iri"/> factory helpers.
/// Implement to provide options via DI, environment variables, or appsettings.
/// </summary>
/// <seealso cref="EntityOptionsInstance"/>
/// <seealso cref="EntityOptions.Use"/>
public interface IEntityOptions
{
    /// <summary>
    /// Global base IRI prepended to every entity path and identity suffix.
    /// </summary>
    string BaseIri { get; }

    /// <summary>
    /// Global base IRI used to resolve short predicate names declared on
    /// <c>[Owning]</c> / <c>[Inverse]</c>. Defaults to <c>{BaseIri}/predicates</c>.
    /// </summary>
    string PredicateBaseIri { get; }
}
