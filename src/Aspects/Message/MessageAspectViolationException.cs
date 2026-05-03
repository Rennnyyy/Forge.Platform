using Forge.Entity;
namespace Forge.Aspects.Message;

/// <summary>
/// Thrown by the message aspect engine when one or more SHACL constraints are violated
/// during message validation. See Capability ADR-0001.
/// </summary>
public sealed class MessageAspectViolationException : Exception
{
    /// <summary>The CLR type of the message that was rejected.</summary>
    public Type MessageType { get; }

    /// <summary>The name of the aspect whose shape produced the violation.</summary>
    public string AspectName { get; }

    /// <summary>All violations reported for the rejected message.</summary>
    public IReadOnlyList<AspectViolation> Violations { get; }

    public MessageAspectViolationException(
        Type messageType,
        string aspectName,
        IReadOnlyList<AspectViolation> violations)
        : base(BuildMessage(messageType, aspectName, violations))
    {
        MessageType = messageType;
        AspectName = aspectName;
        Violations = violations;
    }

    private static string BuildMessage(
        Type messageType,
        string aspectName,
        IReadOnlyList<AspectViolation> violations)
    {
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectName}' rejected message of type '{messageType.Name}': {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
