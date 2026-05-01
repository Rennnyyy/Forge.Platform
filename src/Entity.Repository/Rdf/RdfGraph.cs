using System.Collections;
using System.Collections.Generic;

namespace Forge.Entity.Repository.Rdf;

/// <summary>
/// A small, in-memory triple set anchored at one subject IRI. Holds the closure of
/// triples reachable from that subject (including blank-node-rooted sub-graphs such as
/// <c>rdf:List</c> chains for ordered collections). Indexed by (subject, predicate)
/// for fast lookup during materialization.
/// </summary>
public sealed class RdfGraph : IEnumerable<RdfTriple>
{
    private readonly Dictionary<(string Subject, string Predicate), List<RdfTriple>> _index = new();
    private readonly List<RdfTriple> _all = new();

    public string SubjectIri { get; }

    public RdfGraph(string subjectIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectIri);
        SubjectIri = subjectIri;
    }

    public int Count => _all.Count;

    public void Add(RdfTriple triple)
    {
        _all.Add(triple);
        if (triple.Predicate.IsIri)
        {
            var key = (KeyOf(triple.Subject), triple.Predicate.Value);
            if (!_index.TryGetValue(key, out var list))
                _index[key] = list = new List<RdfTriple>();
            list.Add(triple);
        }
    }

    public IReadOnlyList<RdfTerm> ObjectsOf(string predicateIri) =>
        ObjectsOf(SubjectIri, predicateIri);

    public RdfTerm? FirstObjectOf(string predicateIri) =>
        FirstObjectOf(SubjectIri, predicateIri);

    public IReadOnlyList<RdfTerm> ObjectsOf(string subjectKey, string predicateIri) =>
        _index.TryGetValue((subjectKey, predicateIri), out var list)
            ? list.ConvertAll(t => t.Object)
            : (IReadOnlyList<RdfTerm>)Array.Empty<RdfTerm>();

    public RdfTerm? FirstObjectOf(string subjectKey, string predicateIri) =>
        _index.TryGetValue((subjectKey, predicateIri), out var list) && list.Count > 0
            ? list[0].Object
            : null;

    public IEnumerator<RdfTriple> GetEnumerator() => _all.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Subject key for a term (IRI value, or "_:label" for blank nodes).</summary>
    public static string KeyOf(RdfTerm term) =>
        term.Kind == RdfTermKind.BlankNode ? "_:" + term.Value : term.Value;
}
