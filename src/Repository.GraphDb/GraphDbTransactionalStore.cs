using Forge.Entity;
using System.Net.Http.Headers;
using System.Text;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;

namespace Forge.Repository.GraphDb;

/// <summary>
/// Implements <see cref="ITransactionalEntityStore"/> for <see cref="GraphDbEntityStore"/>
/// using the Ontotext GraphDB REST Transactions API. See GraphDb ADR-0002.
/// </summary>
public sealed partial class GraphDbEntityStore : ITransactionalEntityStore
{
    private string TransactionsEndpoint =>
        $"{_gdb.BaseUrl.TrimEnd('/')}/repositories/{_gdb.RepositoryId}/transactions";

    /// <inheritdoc/>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0) return;

        var txUrl = await OpenTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var op in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ApplyOperationToTransactionAsync(op, txUrl, cancellationToken)
                    .ConfigureAwait(false);
            }

            await CommitTransactionAsync(txUrl, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackTransactionAsync(txUrl, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // ------------------------------------------------------------------ Open / commit / rollback

    private async Task<string> OpenTransactionAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TransactionsEndpoint)
        {
            Content = new StringContent(string.Empty),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var location = resp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException(
                "GraphDB did not return a transaction URL in the Location header.");
        return location;
    }

    private async Task CommitTransactionAsync(string txUrl, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{txUrl}?action=COMMIT");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GraphDB transaction commit failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
        }
    }

    private async Task RollbackTransactionAsync(string txUrl, CancellationToken ct)
    {
        try
        {
            // Best-effort: if the server is unreachable or the tx has already expired,
            // we still rethrow the original exception (not this one).
            using var req = new HttpRequestMessage(HttpMethod.Delete, txUrl);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            _ = resp.IsSuccessStatusCode; // ignore; rollback is best-effort
        }
        catch
        {
            // Intentionally swallowed — the originating exception takes precedence.
        }
    }

    // ------------------------------------------------------------------ Operation dispatch

    private async Task ApplyOperationToTransactionAsync(
        TransactionOperation op, string txUrl, CancellationToken ct)
    {
        switch (op)
        {
            case DeleteOperation del:
                var deleteSparql = BuildDeleteSparql(del.Iri);
                await TxUpdateAsync(txUrl, deleteSparql, ct).ConfigureAwait(false);
                break;

            case EntityWriteOperation write:
                var mapper = _registry.ForEntityType(write.Entity.GetType());
                var typeIri = mapper.ResolveTypeIri(_repoOptions);
                var sink = new CollectingTripleSink();
                mapper.ProjectEntity(write.Entity, sink, typeIri);

                if (write.Mode == WriteMode.Create)
                {
                    // Guard: fail (and rollback) if the IRI already exists.
                    var askSparql = NamedGraphWrap(_repoOptions.NamedGraph,
                        $"ASK {{ <{Escape(write.Entity.Iri)}> ?p ?o }}");
                    if (await TxAskAsync(txUrl, askSparql, ct).ConfigureAwait(false))
                        throw new InvalidOperationException(
                            $"Entity '{write.Entity.Iri}' already exists; WriteMode is Create.");
                }
                else // Replace
                {
                    var replaceSparql = BuildDeleteSparql(write.Entity.Iri);
                    await TxUpdateAsync(txUrl, replaceSparql, ct).ConfigureAwait(false);
                }

                var insertSparql = BuildInsertDataSparql(sink, _repoOptions.NamedGraph);
                await TxUpdateAsync(txUrl, insertSparql, ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported transaction operation type: {op.GetType().Name}");
        }
    }

    // ------------------------------------------------------------------ SPARQL builders

    private string BuildDeleteSparql(string iri)
    {
        var sb = new StringBuilder();
        // Delete direct triples on the subject.
        sb.Append("DELETE WHERE { ");
        if (_repoOptions.NamedGraph is not null)
            sb.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
        sb.Append('<').Append(Escape(iri)).Append("> ?p ?o ");
        if (_repoOptions.NamedGraph is not null) sb.Append("} ");
        sb.Append("} ; ");
        // Delete blank-node closures rooted at the subject (rdf:List heads).
        sb.Append("DELETE { ?bn ?p2 ?o2 } WHERE { ");
        if (_repoOptions.NamedGraph is not null)
            sb.Append("GRAPH <").Append(Escape(_repoOptions.NamedGraph)).Append("> { ");
        sb.Append('<').Append(Escape(iri))
          .Append("> (<urn:forge:any>|!<urn:forge:any>)+ ?bn . FILTER(isBlank(?bn)) . ?bn ?p2 ?o2 ");
        if (_repoOptions.NamedGraph is not null) sb.Append("} ");
        sb.Append('}');
        return sb.ToString();
    }

    private string BuildInsertDataSparql(CollectingTripleSink sink, string? namedGraph)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT DATA { ");
        if (namedGraph is not null)
            sb.Append("GRAPH <").Append(Escape(namedGraph)).Append("> { ");
        foreach (var t in sink.Triples) AppendTriple(sb, t);
        if (namedGraph is not null) sb.Append("} ");
        sb.Append('}');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Transaction-scoped HTTP

    private async Task<bool> TxAskAsync(string txUrl, string sparql, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{txUrl}?action=QUERY")
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

    private async Task TxUpdateAsync(string txUrl, string sparql, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{txUrl}?action=UPDATE")
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-update"),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GraphDB transaction update failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
        }
    }
}
