using Forge.Entity;
using System.Runtime.CompilerServices;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Rdf;
using Microsoft.Extensions.Options;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace Forge.Repository.InMemory;

/// <summary>
/// In-memory <see cref="IEntityStore"/> backed by a dotNetRDF graph. Uses direct
/// graph traversal (no SPARQL) for fast subject lookups; suitable for tests and for
/// embedded scenarios. The same behavioral spec is asserted against the GraphDB
/// backend to keep the two implementations interchangeable.
/// </summary>
public sealed partial class InMemoryEntityStore : IEntityStore, IInverseRefLoader
{
    private readonly Graph _graph;
    private readonly IRdfMapperRegistry _registry;
    private readonly EntityRepositoryOptions _options;

    public string? NamedGraph => _options.NamedGraph;

    public InMemoryEntityStore(IRdfMapperRegistry registry, IOptions<EntityRepositoryOptions> options)
        : this(new Graph(), registry, options.Value) { }

    public InMemoryEntityStore(Graph graph, IRdfMapperRegistry registry, EntityRepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        _graph = graph;
        _registry = registry;
        _options = options;
    }

    /// <summary>The underlying dotNetRDF graph (for tests and Turtle import).</summary>
    public Graph Graph => _graph;

    // ------------------------------------------------------------------ Load

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => LoadAsync<T>(iri, cancellationToken);

    public async ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        cancellationToken.ThrowIfCancellationRequested();

        var subj = _graph.CreateUriNode(UriFactory.Create(iri));
        var subjectGraph = BuildSubjectClosure(subj, iri);
        if (subjectGraph.Count == 0) return null;

        var mapper = _registry.For<T>();
        return await mapper.HydrateAsync(iri, subjectGraph, this, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walk the graph collecting all triples reachable from <paramref name="subject"/>,
    /// following blank-node objects (for <c>rdf:List</c> traversal).</summary>
    private RdfGraph BuildSubjectClosure(INode subject, string subjectIri)
    {
        var result = new RdfGraph(subjectIri);
        var seen = new HashSet<INode>(new FastNodeComparer());
        var queue = new Queue<INode>();
        queue.Enqueue(subject);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node)) continue;
            foreach (var t in _graph.GetTriplesWithSubject(node))
            {
                result.Add(ToTriple(t));
                if (t.Object is IBlankNode || (t.Object is IUriNode && t.Object.NodeType == NodeType.Uri))
                {
                    // Follow blank nodes and IRI nodes that have their own triples (rdf:List nesting).
                    if (t.Object is IBlankNode) queue.Enqueue(t.Object);
                }
            }
        }
        return result;
    }

    private static RdfTriple ToTriple(Triple t) =>
        new(NodeToTerm(t.Subject), NodeToTerm(t.Predicate), NodeToTerm(t.Object));

    private static RdfTerm NodeToTerm(INode n) => n switch
    {
        IUriNode u => RdfTerm.Iri(u.Uri.AbsoluteUri),
        IBlankNode b => RdfTerm.Blank(b.InternalID),
        ILiteralNode l => RdfTerm.Literal(l.Value, l.DataType?.AbsoluteUri, l.Language),
        _ => throw new NotSupportedException($"Unsupported node type: {n?.NodeType}"),
    };

    private sealed class FastNodeComparer : IEqualityComparer<INode>
    {
        public bool Equals(INode? x, INode? y) => x is null ? y is null : x.Equals(y);
        public int GetHashCode(INode obj) => obj.GetHashCode();
    }

    // ------------------------------------------------------------------ Save

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        var mapper = _registry.For<T>();
        var typeIri = mapper.ResolveTypeIri(_options);
        var sink = new CollectingTripleSink();
        mapper.Project(entity, sink, typeIri);

        var subj = _graph.CreateUriNode(UriFactory.Create(entity.Iri));
        if (mode == WriteMode.Replace)
            DeleteSubjectClosure(subj);
        else if (mode == WriteMode.Create && _graph.GetTriplesWithSubject(subj).Any())
            throw new InvalidOperationException(
                $"Entity '{entity.Iri}' already exists and WriteMode is Create.");

        var blankCache = new Dictionary<string, IBlankNode>(StringComparer.Ordinal);
        foreach (var triple in sink.Triples)
            _graph.Assert(ToDotNetRdfTriple(triple, blankCache));

        return default;
    }

    private void DeleteSubjectClosure(INode subject)
    {
        var seen = new HashSet<INode>(new FastNodeComparer());
        var queue = new Queue<INode>();
        queue.Enqueue(subject);
        var toRemove = new List<Triple>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node)) continue;
            foreach (var t in _graph.GetTriplesWithSubject(node))
            {
                toRemove.Add(t);
                if (t.Object is IBlankNode) queue.Enqueue(t.Object);
            }
        }
        foreach (var t in toRemove) _graph.Retract(t);
    }

    private Triple ToDotNetRdfTriple(RdfTriple triple, Dictionary<string, IBlankNode> blanks)
    {
        return new Triple(
            ToNode(triple.Subject, blanks),
            ToNode(triple.Predicate, blanks),
            ToNode(triple.Object, blanks));
    }

    private INode ToNode(RdfTerm term, Dictionary<string, IBlankNode> blanks)
    {
        return term.Kind switch
        {
            RdfTermKind.Iri => _graph.CreateUriNode(UriFactory.Create(term.Value)),
            RdfTermKind.BlankNode => blanks.TryGetValue(term.Value, out var b)
                ? b
                : (blanks[term.Value] = _graph.CreateBlankNode(term.Value)),
            RdfTermKind.Literal when term.Language is not null =>
                _graph.CreateLiteralNode(term.Value, term.Language!),
            RdfTermKind.Literal when term.DatatypeIri is not null =>
                _graph.CreateLiteralNode(term.Value, UriFactory.Create(term.DatatypeIri!)),
            RdfTermKind.Literal => _graph.CreateLiteralNode(term.Value),
            _ => throw new NotSupportedException($"Unsupported term kind: {term.Kind}"),
        };
    }

    // ------------------------------------------------------------------ Delete

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        cancellationToken.ThrowIfCancellationRequested();
        var subj = _graph.CreateUriNode(UriFactory.Create(iri));
        DeleteSubjectClosure(subj);
        return default;
    }

    // ------------------------------------------------------------------ Query

    public async IAsyncEnumerable<T> QueryByTypeAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var mapper = _registry.For<T>();
        var typeIri = mapper.ResolveTypeIri(_options);
        var rdfType = _graph.CreateUriNode(UriFactory.Create(RdfVocab.Type));
        var typeNode = _graph.CreateUriNode(UriFactory.Create(typeIri));

        var subjects = _graph
            .GetTriplesWithPredicateObject(rdfType, typeNode)
            .Select(t => t.Subject)
            .OfType<IUriNode>()
            .Select(n => n.Uri.AbsoluteUri)
            .ToList();

        foreach (var iri in subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadAsync<T>(iri, cancellationToken).ConfigureAwait(false);
            if (loaded is not null) yield return loaded;
        }
    }

    // ------------------------------------------------------------------ IInverseRefLoader

    /// <summary>
    /// Reverse lookup: finds the subject that points to <paramref name="targetIri"/> via
    /// <paramref name="predicate"/> either as a direct object or inside an <c>rdf:List</c>.
    /// Returns the first matching subject IRI, or <see langword="null"/> if none found.
    /// </summary>
    public ValueTask<string?> LoadInverseRefIriAsync(
        string targetIri,
        string predicate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predNode = _graph.CreateUriNode(UriFactory.Create(predicate));

        foreach (var triple in _graph.GetTriplesWithPredicate(predNode))
        {
            if (triple.Subject is not IUriNode ownerUri) continue;
            if (ListOrDirectContains(triple.Object, targetIri))
                return ValueTask.FromResult<string?>(ownerUri.Uri.AbsoluteUri);
        }
        return ValueTask.FromResult<string?>(null);
    }

    /// <summary>
    /// Reverse collection lookup: yields the IRI of every subject that points to
    /// <paramref name="targetIri"/> via <paramref name="predicate"/> (absolute IRI),
    /// either as a direct object or inside an <c>rdf:List</c> chain (ADR-0018).
    /// </summary>
    public async IAsyncEnumerable<string> LoadInverseCollectionIrisAsync<T>(
        string targetIri,
        string predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predNode = _graph.CreateUriNode(UriFactory.Create(predicate));
        foreach (var triple in _graph.GetTriplesWithPredicate(predNode))
        {
            if (triple.Subject is not IUriNode ownerUri) continue;
            if (ListOrDirectContains(triple.Object, targetIri))
                yield return ownerUri.Uri.AbsoluteUri;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="head"/> either IS
    /// <paramref name="targetIri"/> (direct reference) or is an <c>rdf:List</c> blank
    /// node chain that contains <paramref name="targetIri"/> as a <c>rdf:first</c> value.
    /// </summary>
    private bool ListOrDirectContains(INode head, string targetIri)
    {
        var rdfFirst = _graph.CreateUriNode(UriFactory.Create(RdfVocab.First));
        var rdfRest  = _graph.CreateUriNode(UriFactory.Create(RdfVocab.Rest));
        var rdfNil   = _graph.CreateUriNode(UriFactory.Create(RdfVocab.Nil));

        // Direct IRI match.
        if (head is IUriNode u && u.Uri.AbsoluteUri == targetIri) return true;

        // Walk rdf:List chain looking for rdf:first = targetIri.
        var current = head;
        while (current is IBlankNode)
        {
            var first = _graph.GetTriplesWithSubjectPredicate(current, rdfFirst).FirstOrDefault()?.Object;
            if (first is IUriNode fu && fu.Uri.AbsoluteUri == targetIri) return true;
            var rest = _graph.GetTriplesWithSubjectPredicate(current, rdfRest).FirstOrDefault()?.Object;
            if (rest is null || (rest is IUriNode ru && ru.Equals(rdfNil))) break;
            current = rest;
        }
        return false;
    }


    public async IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var resolved = predicate.Contains(':')
            ? predicate
            : PredicateLookup<T>(predicate);

        var subj = _graph.CreateUriNode(UriFactory.Create(ownerIri));
        var pred = _graph.CreateUriNode(UriFactory.Create(resolved));
        var first = _graph.GetTriplesWithSubjectPredicate(subj, pred).FirstOrDefault();
        if (first is null) yield break;

        // Try rdf:List traversal; if the object is a non-list IRI, return the direct triples.
        var head = first.Object;
        if (head is IBlankNode)
        {
            var rdfFirst = _graph.CreateUriNode(UriFactory.Create(RdfVocab.First));
            var rdfRest = _graph.CreateUriNode(UriFactory.Create(RdfVocab.Rest));
            var rdfNil = _graph.CreateUriNode(UriFactory.Create(RdfVocab.Nil));
            while (head is IBlankNode bn && !bn.Equals(rdfNil))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memberT = _graph.GetTriplesWithSubjectPredicate(bn, rdfFirst).FirstOrDefault();
                if (memberT?.Object is IUriNode m) yield return m.Uri.AbsoluteUri;
                var nextT = _graph.GetTriplesWithSubjectPredicate(bn, rdfRest).FirstOrDefault();
                head = nextT?.Object!;
                if (head is IUriNode ru && ru.Uri.AbsoluteUri == RdfVocab.Nil) yield break;
                if (head is null) yield break;
            }
        }
        else
        {
            // Multi-triple membership (non-list) — return all object IRIs.
            foreach (var t in _graph.GetTriplesWithSubjectPredicate(subj, pred))
                if (t.Object is IUriNode u) yield return u.Uri.AbsoluteUri;
        }
    }
#pragma warning restore CS1998

    private string PredicateLookup<T>(string shortName)
        where T : class, IEntity
    {
        var mapper = _registry.For<T>();
        return PredicateResolverShim.Resolve(shortName, mapper.PredicatePath);
    }

    // ------------------------------------------------------------------ Convenience: load Turtle

    /// <summary>Parse Turtle text and merge into the underlying graph. Useful in tests.</summary>
    public InMemoryEntityStore LoadTurtle(string turtle)
    {
        ArgumentNullException.ThrowIfNull(turtle);
        var parser = new TurtleParser();
        using var reader = new StringReader(turtle);
        parser.Load(_graph, reader);
        return this;
    }

    public ValueTask DisposeAsync()
    {
        _txLock.Dispose();
        return default;
    }
}

/// <summary>Internal shim so the InMemory backend can use the core's predicate resolver
/// without exposing it publicly.</summary>
internal static class PredicateResolverShim
{
    public static string Resolve(string declared, string? predicatePath)
    {
        if (declared.Contains(':')) return declared;
        var basePart = Forge.Entity.EntityOptions.Current.PredicateBaseIri.TrimEnd('/');
        if (string.IsNullOrEmpty(predicatePath))
            return $"{basePart}/{declared}";
        return $"{basePart}/{predicatePath!.Trim('/')}/{declared}";
    }
}
