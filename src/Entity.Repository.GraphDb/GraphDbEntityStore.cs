using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Forge.Entity.Repository.Rdf;
using Microsoft.Extensions.Options;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Parsing.Handlers;
using VDS.RDF.Query;

namespace Forge.Entity.Repository.GraphDb;

/// <summary>
/// HTTP-based <see cref="IEntityStore"/> against the Ontotext GraphDB SPARQL endpoint.
/// Reads via SPARQL <c>CONSTRUCT</c>; writes via SPARQL <c>UPDATE</c> (INSERT DATA /
/// DELETE WHERE). Behaves identically to <c>InMemoryEntityStore</c> w.r.t. the mapper
/// contract — the two share a behavioral test suite.
/// </summary>
public sealed class GraphDbEntityStore : IEntityStore
{
    private readonly HttpClient _http;
    private readonly IRdfMapperRegistry _registry;
    private readonly EntityRepositoryOptions _repoOptions;
    private readonly GraphDbOptions _gdb;

    public string? NamedGraph => _repoOptions.NamedGraph;

    public GraphDbEntityStore(
        HttpClient http,
        IRdfMapperRegistry registry,
        IOptions<EntityRepositoryOptions> repoOptions,
        IOptions<GraphDbOptions> gdb)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(registry);
        _http = http;
        _registry = registry;
        _repoOptions = repoOptions.Value;
        _gdb = gdb.Value;

        _http.Timeout = _gdb.Timeout;
        if (!string.IsNullOrEmpty(_gdb.Username))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_gdb.Username}:{_gdb.Password ?? string.Empty}"));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", creds);
        }
    }

    // ------------------------------------------------------------------ Load

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken ct) where T : class
        => LoadAsync<T>(iri, ct);

    public async ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);

        // CONSTRUCT a closure of the subject's reachable graph (follows blank nodes / rdf:Lists).
        var sparql = NamedGraphWrap(_repoOptions.NamedGraph,
            $"CONSTRUCT {{ ?s ?p ?o }} WHERE {{ <{Escape(iri)}> (<urn:forge:any>|!<urn:forge:any>)* ?s . ?s ?p ?o . }}");

        var graph = await ConstructAsync(sparql, cancellationToken).ConfigureAwait(false);
        var subjectGraph = ToRdfGraph(graph, iri);
        if (subjectGraph.Count == 0) return null;

        return _registry.For<T>().Hydrate(iri, subjectGraph);
    }

    // ------------------------------------------------------------------ Save

    public async ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entity);

        var mapper = _registry.For<T>();
        var typeIri = mapper.ResolveTypeIri(_repoOptions);
        var sink = new CollectingTripleSink();
        mapper.Project(entity, sink, typeIri);

        var sb = new StringBuilder();
        if (mode == WriteMode.Create)
        {
            // Guard: throw if the IRI is already present in the graph.
            var askSparql = NamedGraphWrap(_repoOptions.NamedGraph,
                $"ASK {{ <{Escape(entity.Iri)}> ?p ?o }}");
            if (await AskAsync(askSparql, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException(
                    $"An entity with IRI '{entity.Iri}' already exists; WriteMode is Create.");
        }

        if (mode == WriteMode.Replace)
        {
            // Direct triples on the subject.
            sb.Append("DELETE WHERE { ");
            if (_repoOptions.NamedGraph is not null)
                sb.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
            sb.Append("<").Append(Escape(entity.Iri)).Append("> ?p ?o ");
            if (_repoOptions.NamedGraph is not null) sb.Append("} ");
            sb.Append("} ; ");
            // Blank-node closures rooted at the subject (rdf:List heads).
            // Property paths are not allowed in DELETE WHERE templates — use DELETE {} WHERE {}.
            sb.Append("DELETE { ?bn ?p2 ?o2 } WHERE { ");
            if (_repoOptions.NamedGraph is not null)
                sb.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
            sb.Append("<").Append(Escape(entity.Iri)).Append("> (<urn:forge:any>|!<urn:forge:any>)+ ?bn . FILTER(isBlank(?bn)) . ?bn ?p2 ?o2 ");
            if (_repoOptions.NamedGraph is not null) sb.Append("} ");
            sb.Append("} ; ");
        }

        sb.Append("INSERT DATA { ");
        if (_repoOptions.NamedGraph is not null)
            sb.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
        foreach (var t in sink.Triples) AppendTriple(sb, t);
        if (_repoOptions.NamedGraph is not null) sb.Append("} ");
        sb.Append("}");

        await UpdateAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Delete

    public async ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        var sparql = new StringBuilder();
        // Direct triples.
        sparql.Append("DELETE WHERE { ");
        if (_repoOptions.NamedGraph is not null)
            sparql.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
        sparql.Append("<").Append(Escape(iri)).Append("> ?p ?o ");
        if (_repoOptions.NamedGraph is not null) sparql.Append("} ");
        sparql.Append("} ; ");
        // Blank-node closures — property paths require DELETE {} WHERE {} form.
        sparql.Append("DELETE { ?bn ?p2 ?o2 } WHERE { ");
        if (_repoOptions.NamedGraph is not null)
            sparql.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
        sparql.Append("<").Append(Escape(iri)).Append("> (<urn:forge:any>|!<urn:forge:any>)+ ?bn . FILTER(isBlank(?bn)) . ?bn ?p2 ?o2 ");
        if (_repoOptions.NamedGraph is not null) sparql.Append("} ");
        sparql.Append("}");
        await UpdateAsync(sparql.ToString(), cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Query by type

    public async IAsyncEnumerable<T> QueryByTypeAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var mapper = _registry.For<T>();
        var typeIri = mapper.ResolveTypeIri(_repoOptions);
        var sparql = NamedGraphWrap(_repoOptions.NamedGraph,
            $"SELECT DISTINCT ?s WHERE {{ ?s a <{Escape(typeIri)}> }}");
        var iris = await SelectIrisAsync(sparql, "s", cancellationToken).ConfigureAwait(false);
        foreach (var iri in iris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadAsync<T>(iri, cancellationToken).ConfigureAwait(false);
            if (loaded is not null) yield return loaded;
        }
    }

    // ------------------------------------------------------------------ ICollectionLoader

    public async IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var mapper = _registry.For<T>();
        var resolved = predicate.Contains(':') ? predicate
            : ResolveSimplePredicate(predicate, mapper.PredicatePath);

        // Try rdf:List traversal in SPARQL: walk first/rest until rdf:nil.
        var sparql = NamedGraphWrap(_repoOptions.NamedGraph, $@"
SELECT ?member WHERE {{
  <{Escape(ownerIri)}> <{Escape(resolved)}> ?head .
  ?head <{RdfVocab.Rest}>* ?node .
  ?node <{RdfVocab.First}> ?member .
}}");
        var iris = await SelectIrisAsync(sparql, "member", cancellationToken).ConfigureAwait(false);
        foreach (var iri in iris) yield return iri;
    }

    private static string ResolveSimplePredicate(string declared, string? predicatePath)
    {
        var basePart = Forge.Entity.EntityOptions.Current.PredicateBaseIri.TrimEnd('/');
        return string.IsNullOrEmpty(predicatePath)
            ? $"{basePart}/{declared}"
            : $"{basePart}/{predicatePath!.Trim('/')}/{declared}";
    }

    // ------------------------------------------------------------------ HTTP plumbing

    private async Task<IGraph> ConstructAsync(string sparql, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _gdb.QueryEndpoint)
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-query"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/n-triples"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var g = new Graph();
        var parser = new NTriplesParser();
        using var reader = new StringReader(body);
        parser.Load(g, reader);
        return g;
    }

    private async Task<bool> AskAsync(string sparql, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _gdb.QueryEndpoint)
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-query"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("boolean", out var b) && b.GetBoolean();
    }

    private async Task<List<string>> SelectIrisAsync(string sparql, string varName, CancellationToken ct)    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _gdb.QueryEndpoint)
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-query"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var result = new List<string>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("results", out var results)
            && results.TryGetProperty("bindings", out var bindings))
        {
            foreach (var b in bindings.EnumerateArray())
                if (b.TryGetProperty(varName, out var v) && v.TryGetProperty("value", out var val))
                    result.Add(val.GetString()!);
        }
        return result;
    }

    private async Task UpdateAsync(string sparql, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _gdb.UpdateEndpoint)
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-update"),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GraphDB update failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
        }
    }

    // ------------------------------------------------------------------ Helpers

    private static string NamedGraphWrap(string? graph, string sparql) =>
        graph is null ? sparql : sparql.Replace("WHERE {", $"WHERE {{ GRAPH <{Escape(graph)}> {{").TrimEnd('}') + " } }";

    private static string Escape(string iri) => iri.Replace(">", "%3E");

    private static RdfGraph ToRdfGraph(IGraph g, string subjectIri)
    {
        var result = new RdfGraph(subjectIri);
        foreach (var t in g.Triples) result.Add(ToTriple(t));
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

    private static void AppendTriple(StringBuilder sb, RdfTriple t)
    {
        AppendTerm(sb, t.Subject); sb.Append(' ');
        AppendTerm(sb, t.Predicate); sb.Append(' ');
        AppendTerm(sb, t.Object); sb.Append(" . ");
    }

    private static void AppendTerm(StringBuilder sb, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                sb.Append('<').Append(Escape(term.Value)).Append('>');
                break;
            case RdfTermKind.BlankNode:
                sb.Append("_:").Append(term.Value);
                break;
            case RdfTermKind.Literal:
                sb.Append('"').Append(term.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                if (term.Language is not null) sb.Append('@').Append(term.Language);
                else if (term.DatatypeIri is not null) sb.Append("^^<").Append(term.DatatypeIri).Append('>');
                break;
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return default;
    }
}
