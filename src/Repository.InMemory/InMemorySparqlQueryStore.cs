using Forge.Entity;
using System.Runtime.CompilerServices;
using Forge.Repository;
using Forge.Repository.Rdf;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Query.Datasets;

namespace Forge.Repository.InMemory;

/// <summary>
/// SPARQL-execution capability for <see cref="InMemoryEntityStore"/>. Implemented as a
/// partial because the bulk of the store lives in <c>InMemoryEntityStore.cs</c>; this
/// file is the dotNetRDF Leviathan adapter for the <see cref="ISparqlQueryStore"/>
/// seam (Sparql ADR-0002).
/// </summary>
public sealed partial class InMemoryEntityStore : ISparqlQueryStore
{
#pragma warning disable CS1998
    public async IAsyncEnumerable<SparqlResultRow> ExecuteSelectAsync(string sparql,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sparql);
        cancellationToken.ThrowIfCancellationRequested();

        var parser = new SparqlQueryParser();
        var parsed = parser.ParseFromString(sparql);

        var dataset = new InMemoryDataset(_graph);
        var processor = new LeviathanQueryProcessor(dataset);
        var raw = processor.ProcessQuery(parsed);

        if (raw is not SparqlResultSet rs)
            throw new NotSupportedException(
                "ExecuteSelectAsync requires a SPARQL SELECT query. Received: " +
                (raw?.GetType().Name ?? "null"));

        foreach (var result in rs.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bindings = new Dictionary<string, RdfTerm>(StringComparer.Ordinal);
            foreach (var v in result.Variables)
            {
                var node = result.Value(v);
                if (node is null) continue;
                bindings[v] = NodeToTerm(node);
            }
            yield return new SparqlResultRow(bindings);
        }
    }
#pragma warning restore CS1998
}
