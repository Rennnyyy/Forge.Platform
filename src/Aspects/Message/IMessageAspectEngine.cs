namespace Forge.Aspects.Message;

/// <summary>
/// Engine for validating message objects (commands, responses, events) against a SHACL shape.
/// See Capability ADR-0001.
/// </summary>
public interface IMessageAspectEngine
{
    /// <summary>
    /// Validates <paramref name="message"/> against <paramref name="aspect"/>.ShapeTtl.
    /// No-op if <paramref name="aspect"/> is <c>null</c> or its <c>ShapeTtl</c> is <c>null</c>.
    /// Throws <see cref="MessageAspectViolationException"/> on <c>sh:Violation</c> severity.
    /// </summary>
    ValueTask ValidateAsync(
        object message,
        IMessageAspect? aspect,
        CancellationToken cancellationToken = default);
}
