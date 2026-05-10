namespace Forge.Aspects.Abstractions;

/// <summary>
/// Thrown by the message aspect engine when one or more SHACL constraints are violated
/// during message validation. See Capability ADR-0001.
/// </summary>
public sealed class MessageAspectViolationException : AspectException
{
    /// <summary>The CLR type of the message that was rejected.</summary>
    public Type MessageType { get; }

    /// <summary>The IRI of the aspect whose shape produced the violation.</summary>
    public string AspectIri { get; }

    /// <summary>All violations reported for the rejected message.</summary>
    public IReadOnlyList<AspectViolation> Violations { get; }

    public MessageAspectViolationException(
        Type messageType,
        string aspectIri,
        IReadOnlyList<AspectViolation> violations)
        : base(BuildMessage(messageType, aspectIri, violations))
    {
        MessageType = messageType;
        AspectIri = aspectIri;
        Violations = violations;
    }

    private static string BuildMessage(
        Type messageType,
        string aspectIri,
        IReadOnlyList<AspectViolation> violations)
    {
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectIri}' rejected message of type '{messageType.Name}': {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
