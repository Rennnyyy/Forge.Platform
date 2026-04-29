namespace Forge.Entity;

/// <summary>
/// Configures how IRIs are constructed for entities in the current process.
/// Set once at startup, before any entity is materialized.
/// </summary>
public static class EntityOptions
{
    private static string _baseIri = "https://forge.local";
    private static string? _predicateBaseIri;

    /// <summary>
    /// Global base IRI prepended to every entity's path-prefix and identity suffix.
    /// Trailing slashes are normalized away.
    /// </summary>
    public static string BaseIri
    {
        get => _baseIri;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("BaseIri must not be empty.", nameof(value));
            _baseIri = value.TrimEnd('/');
        }
    }

    /// <summary>
    /// Global base IRI used to resolve short predicate names declared on
    /// <c>[Owning]</c> / <c>[Inverse]</c>. Defaults to <c>{BaseIri}/predicates</c>.
    /// Setting an absolute IRI on a relation bypasses this.
    /// </summary>
    public static string PredicateBaseIri
    {
        get => _predicateBaseIri ?? $"{_baseIri}/predicates";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("PredicateBaseIri must not be empty.", nameof(value));
            _predicateBaseIri = value.TrimEnd('/');
        }
    }
}
