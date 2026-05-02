namespace Forge.Entity.Aspects;

/// <summary>
/// Thrown by <see cref="IQueryAspectEngine"/> when an access gate or result-shape SHACL
/// check fails for a read or query operation. See Aspects ADR-0007.
/// </summary>
public sealed class QueryAspectViolationException : Exception
{
    /// <summary>All violations reported, if a SHACL result-shape pass failed.</summary>
    public IReadOnlyList<AspectViolation>? Violations { get; }

    /// <summary>The IRI of the entity or query that was rejected, if available.</summary>
    public string? EntityIri { get; }

    /// <summary>The name of the aspect whose gate or shape produced the violation.</summary>
    public string SourceAspectName { get; }

    /// <summary>Creates a gate-failure exception (no SHACL violations — access denied by filter).</summary>
    public QueryAspectViolationException(string entityIri, string sourceAspectName)
        : base($"Aspect '{sourceAspectName}' denied read access to <{entityIri}>.")
    {
        EntityIri = entityIri;
        SourceAspectName = sourceAspectName;
    }

    /// <summary>Creates a result-shape violation exception.</summary>
    public QueryAspectViolationException(
        IReadOnlyList<AspectViolation> violations,
        string? entityIri,
        string sourceAspectName)
        : base(BuildMessage(violations, entityIri, sourceAspectName))
    {
        Violations = violations;
        EntityIri = entityIri;
        SourceAspectName = sourceAspectName;
    }

    /// <summary>Creates a missing-placeholder exception for dynamic SPARQL.</summary>
    public QueryAspectViolationException(string message, string sourceAspectName, bool _)
        : base(message)
    {
        SourceAspectName = sourceAspectName;
    }

    private static string BuildMessage(
        IReadOnlyList<AspectViolation> violations,
        string? iri,
        string aspectName)
    {
        var target = iri is { } i ? $"<{i}>" : "query result";
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectName}' rejected {target}: {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
