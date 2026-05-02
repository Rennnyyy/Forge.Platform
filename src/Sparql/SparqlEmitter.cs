using Forge.Entity;
using System.Text;

namespace Forge.Sparql;

/// <summary>
/// Renders a <see cref="SparqlQueryModel"/> into a SPARQL string suitable for
/// <see cref="Forge.Repository.ISparqlQueryStore.ExecuteSelectAsync"/>.
/// </summary>
internal static class SparqlEmitter
{
    /// <summary>The variable name carrying the entity subject IRI in the result-set.</summary>
    public const string SubjectVar = "s";

    /// <summary>The variable name carrying the count for <see cref="SparqlTerminalKind.Count"/> queries.</summary>
    public const string CountVar = "c";

    public static string Emit(SparqlQueryModel model)
    {
        var sb = new StringBuilder(256);

        switch (model.Terminal)
        {
            case SparqlTerminalKind.Count:
                sb.Append("SELECT (COUNT(DISTINCT ?").Append(SubjectVar).Append(") AS ?").Append(CountVar).Append(") WHERE {");
                break;
            case SparqlTerminalKind.Any:
            case SparqlTerminalKind.Entities:
                sb.Append("SELECT DISTINCT ?").Append(SubjectVar).Append(" WHERE {");
                break;
        }

        sb.Append(' ');
        // Type pattern.
        sb.Append('?').Append(SubjectVar).Append(" a <").Append(model.TypeIri).Append("> .");

        // OPTIONAL bindings for every referenced property.
        foreach (var binding in model.Referenced.Values)
        {
            sb.Append(' ');
            sb.Append("OPTIONAL { ?").Append(SubjectVar).Append(" <").Append(binding.PredicateIri)
              .Append("> ?").Append(binding.Variable).Append(" } .");
        }

        // FILTER (...).
        if (model.Filters.Count > 0)
        {
            sb.Append(' ').Append("FILTER (");
            sb.Append(string.Join(" && ", model.Filters));
            sb.Append(')');
        }

        sb.Append(" }");

        // ORDER BY (only meaningful for Entities terminal; harmless for Any).
        if (model.Terminal == SparqlTerminalKind.Entities && model.Orderings.Count > 0)
        {
            sb.Append(" ORDER BY");
            foreach (var o in model.Orderings)
            {
                sb.Append(' ');
                if (o.Descending) sb.Append("DESC(?").Append(o.Property.Variable).Append(')');
                else              sb.Append("ASC(?").Append(o.Property.Variable).Append(')');
            }
        }

        // LIMIT / OFFSET.
        var take = model.Take;
        if (model.Terminal == SparqlTerminalKind.Any) take = 1;
        else if (model.Single == SingleResultMode.First) take = take is null ? 1 : Math.Min(take.Value, 1);
        else if (model.Single == SingleResultMode.Single) take = take is null ? 2 : Math.Min(take.Value, 2);

        if (take is int t) sb.Append(" LIMIT ").Append(t);
        if (model.Skip is int sk && sk > 0) sb.Append(" OFFSET ").Append(sk);

        return sb.ToString();
    }
}
