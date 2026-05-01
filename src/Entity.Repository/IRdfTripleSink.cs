using Forge.Entity.Repository.Rdf;

namespace Forge.Entity.Repository;

/// <summary>Receives triples emitted by <see cref="IRdfMapper{T}.Project"/>.</summary>
public interface IRdfTripleSink
{
    void Add(RdfTriple triple);

    /// <summary>
    /// Allocate a fresh blank-node label unique within this sink (for <c>rdf:List</c>
    /// chains and similar nested structures).
    /// </summary>
    string NewBlankNodeLabel();
}

/// <summary>List-backed default sink. Useful for tests and for backends that prefer
/// to materialize the projected triples before serializing.</summary>
public sealed class CollectingTripleSink : IRdfTripleSink
{
    // Global counter so blank-node labels are unique across ALL sink instances and
    // therefore across all SaveAsync calls into the same underlying graph.
    private static int _globalBn;
    public List<RdfTriple> Triples { get; } = new();

    public void Add(RdfTriple triple) => Triples.Add(triple);
    public string NewBlankNodeLabel() => $"b{System.Threading.Interlocked.Increment(ref _globalBn)}";
}
