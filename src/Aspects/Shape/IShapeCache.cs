using Forge.Entity;
using VDS.RDF.Shacl;

namespace Forge.Aspects.Shape;

/// <summary>
/// Cache of parsed <see cref="ShapesGraph"/> instances keyed by the SHA-256 hex digest
/// of the source Turtle text. Avoids re-parsing identical shapes registered for multiple entity types.
/// </summary>
public interface IShapeCache
{
    /// <summary>
    /// Return the cached <see cref="ShapesGraph"/> for <paramref name="ttl"/>, parsing and
    /// caching it if it has not been seen before.
    /// Throws <see cref="AspectTtlParseException"/> if parsing fails.
    /// </summary>
    ShapesGraph GetOrParse(string ttl);
}
