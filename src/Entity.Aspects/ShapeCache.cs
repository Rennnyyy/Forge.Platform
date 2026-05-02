using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;

namespace Forge.Entity.Aspects;

/// <summary>
/// Default <see cref="IShapeCache"/> implementation.
/// Parses TTL on first use and caches the resulting <see cref="ShapesGraph"/> by the
/// SHA-256 hex digest of the source text, so identical shapes shared across multiple
/// entity types are only parsed once.
/// </summary>
internal sealed class ShapeCache : IShapeCache
{
    private readonly ConcurrentDictionary<string, ShapesGraph> _cache = new(StringComparer.Ordinal);

    public ShapesGraph GetOrParse(string ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ttl);

        var key = ComputeSha256(ttl);
        return _cache.GetOrAdd(key, _ => Parse(ttl));
    }

    private static ShapesGraph Parse(string ttl)
    {
        var graph = new Graph();
        try
        {
            var parser = new TurtleParser();
            using var reader = new StringReader(ttl);
            parser.Load(graph, reader);
            return new ShapesGraph(graph);
        }
        catch (Exception ex)
        {
            throw new AspectTtlParseException(ttl, ex);
        }
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}
