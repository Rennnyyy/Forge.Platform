using System.Reflection;

namespace Forge.Entity;

/// <summary>
/// Factory helpers for constructing entity IRIs from plain text segments.
/// All methods read the base IRI from <see cref="EntityOptions.Current"/>,
/// so they participate in any ambient override opened with <see cref="EntityOptions.Use"/>.
/// </summary>
public static class Iri
{
    /// <summary>
    /// Combines <see cref="IEntityOptions.BaseIri"/> with the given relative path.
    /// </summary>
    /// <param name="path">
    /// Relative path, e.g. <c>"/entity/myentity"</c>. Leading slashes are normalized.
    /// </param>
    /// <returns>Fully qualified IRI string.</returns>
    /// <example>
    /// <code>
    /// Iri.FromBaseUrl("/entity/myentity")
    /// // → "https://forge-it.net/entity/myentity"
    /// </code>
    /// </example>
    public static string FromBaseUrl(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return $"{EntityOptions.Current.BaseIri}/{path.TrimStart('/')}";
    }

    /// <summary>
    /// Constructs an IRI for entity type <typeparamref name="T"/> using its
    /// <c>[Entity(Path = "…")]</c> declaration and the given identity segment.
    /// </summary>
    /// <typeparam name="T">Entity type decorated with <see cref="EntityAttribute"/>.</typeparam>
    /// <param name="identity">
    /// The identity suffix, e.g. <c>"myentity"</c>. Leading slashes are normalized.
    /// </param>
    /// <returns>
    /// Fully qualified IRI string, e.g. <c>https://forge-it.net/bars/myentity</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="T"/> is not decorated with <see cref="EntityAttribute"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// Iri.FromEntity&lt;Bar&gt;("myentity")
    /// // → "https://forge-it.net/bars/myentity"
    /// </code>
    /// </example>
    public static string FromEntity<T>(string identity) where T : IEntity
    {
        ArgumentNullException.ThrowIfNull(identity);

        var attr = typeof(T).GetCustomAttribute<EntityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{typeof(T).FullName}' is not decorated with [Entity].");

        var path = string.IsNullOrEmpty(attr.Path)
            ? typeof(T).Name.ToLowerInvariant()
            : attr.Path.Trim('/');

        return $"{EntityOptions.Current.BaseIri}/{path}/{identity.TrimStart('/')}";
    }
}
