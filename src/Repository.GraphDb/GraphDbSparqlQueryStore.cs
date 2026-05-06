using Forge.Entity;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Forge.Repository;
using Forge.Repository.Rdf;

namespace Forge.Repository.GraphDb;

/// <summary>
/// SPARQL-execution capability for <see cref="GraphDbEntityStore"/>. Implemented as a
/// partial because the bulk of the store lives in <c>GraphDbEntityStore.cs</c>; this
/// file is the HTTP JSON adapter for the <see cref="ISparqlQueryStore"/> seam
/// (Sparql ADR-0002, GraphDb ADR-0001).
/// </summary>
public sealed partial class GraphDbEntityStore : ISparqlQueryStore
{
    public async IAsyncEnumerable<SparqlResultRow> ExecuteSelectAsync(
        string sparql,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sparql);

        using var req = new HttpRequestMessage(HttpMethod.Post, _gdb.QueryEndpoint)
        {
            Content = new StringContent(sparql, Encoding.UTF8, "application/sparql-query"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));

        using var resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)
            || !results.TryGetProperty("bindings", out var bindings))
            yield break;

        foreach (var row in bindings.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dict = new Dictionary<string, RdfTerm>(StringComparer.Ordinal);
            foreach (var prop in row.EnumerateObject())
            {
                if (TryJsonBindingToTerm(prop.Value, out var term))
                    dict[prop.Name] = term;
            }
            yield return new SparqlResultRow(dict);
        }
    }

    private static bool TryJsonBindingToTerm(JsonElement binding, out RdfTerm term)
    {
        term = default;
        if (!binding.TryGetProperty("type", out var typeEl)
            || !binding.TryGetProperty("value", out var valueEl))
            return false;

        var type = typeEl.GetString();
        var value = valueEl.GetString() ?? string.Empty;

        term = type switch
        {
            "uri" => RdfTerm.Iri(value),
            "bnode" => RdfTerm.Blank(value),
            "literal" => RdfTerm.Literal(
                value,
                binding.TryGetProperty("datatype", out var dt) ? dt.GetString() : null,
                binding.TryGetProperty("xml:lang", out var lang) ? lang.GetString() : null),
            _ => default,
        };

        return type is "uri" or "bnode" or "literal";
    }
}
