namespace Forge.Aspects.Abstractions;

/// <summary>
/// Thrown by <see cref="IQueryAspectEngine"/> when an access gate or result-shape SHACL
/// check fails for a read or query operation. See Aspects ADR-0007.
/// </summary>
public sealed class QueryAspectViolationException : AspectException
{
    /// <summary>All violations reported, if a SHACL result-shape pass failed.</summary>
    public IReadOnlyList<AspectViolation>? Violations { get; }

    /// <summary>The IRI of the entity or query that was rejected, if available.</summary>
    public string? EntityIri { get; }

    /// <summary>The IRI of the aspect whose gate or shape produced the violation.</summary>
    public string SourceAspectIri { get; }

    /// <summary>Creates a gate-failure exception (no SHACL violations — access denied by filter).</summary>
    public QueryAspectViolationException(string entityIri, string sourceAspectIri)
        : base($"Aspect '{sourceAspectIri}' denied read access to <{entityIri}>.")
    {
        EntityIri = entityIri;
        SourceAspectIri = sourceAspectIri;
    }

    /// <summary>Creates a result-shape violation exception.</summary>
    public QueryAspectViolationException(
        IReadOnlyList<AspectViolation> violations,
        string? entityIri,
        string sourceAspectIri)
        : base(BuildMessage(violations, entityIri, sourceAspectIri))
    {
        Violations = violations;
        EntityIri = entityIri;
        SourceAspectIri = sourceAspectIri;
    }

    /// <summary>Creates a missing-placeholder exception for dynamic SPARQL.</summary>
    public QueryAspectViolationException(string message, string sourceAspectIri, bool _)
        : base(message)
    {
        SourceAspectIri = sourceAspectIri;
    }

    private static string BuildMessage(
        IReadOnlyList<AspectViolation> violations,
        string? iri,
        string aspectIri)
    {
        var target = iri is { } i ? $"<{i}>" : "query result";
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectIri}' rejected {target}: {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
