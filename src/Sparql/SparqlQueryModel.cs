using Forge.Entity;
namespace Forge.Sparql;

/// <summary>Terminal shape for a translated query.</summary>
internal enum SparqlTerminalKind
{
    /// <summary>Stream subjects and materialize each via <c>IEntityStore.LoadAsync</c>.</summary>
    Entities,
    /// <summary>Server-side <c>SELECT (COUNT(DISTINCT ?s) AS ?c)</c>.</summary>
    Count,
    /// <summary>Server-side <c>ASK</c>-style; emitted as <c>SELECT ?s ... LIMIT 1</c> + presence check.</summary>
    Any,
}

/// <summary>
/// Intermediate representation built by <see cref="LinqToSparqlVisitor"/> and consumed
/// by <see cref="SparqlEmitter"/>. Mutable while the visitor walks the method-call
/// chain; treated as read-only by the emitter.
/// </summary>
internal sealed class SparqlQueryModel
{
    public string TypeIri { get; init; } = "";

    /// <summary>Property bindings referenced by filters or orderings.</summary>
    public Dictionary<string, PropertyBinding> Referenced { get; } = new(StringComparer.Ordinal);

    /// <summary>FILTER expression list (joined with <c>&amp;&amp;</c>). Empty = no FILTER block.</summary>
    public List<string> Filters { get; } = new();

    /// <summary>Orderings in declaration order.</summary>
    public List<Ordering> Orderings { get; } = new();

    public int? Skip { get; set; }
    public int? Take { get; set; }

    public SparqlTerminalKind Terminal { get; set; } = SparqlTerminalKind.Entities;

    /// <summary>True when the original LINQ call was <c>All(predicate)</c>: the result
    /// of <see cref="SparqlTerminalKind.Any"/> must be inverted by the provider.</summary>
    public bool AllInverted { get; set; }

    /// <summary>
    /// True when the materialized result is to be reduced to a single row (First /
    /// Single semantics). Causes <c>LIMIT 1</c> (or 2 for Single, to detect duplicates).
    /// </summary>
    public SingleResultMode Single { get; set; } = SingleResultMode.None;

    public void Reference(PropertyBinding b) => Referenced[b.Name] = b;

    public sealed record Ordering(PropertyBinding Property, bool Descending);
}

internal enum SingleResultMode { None, First, Single }
