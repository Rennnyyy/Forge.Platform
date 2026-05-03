namespace Forge.Capability;

/// <summary>
/// Orchestrates the full capability dispatch pipeline for a single handler pair
/// (<typeparamref name="TCommand"/> → <typeparamref name="TResponse"/>):
/// per-call aspect injection, command validation, handler invocation,
/// response and event validation. See Capability ADR-0002, ADR-0006, and ADR-0007.
/// </summary>
/// <typeparam name="TCommand">The inbound command message type.</typeparam>
/// <typeparam name="TResponse">The outbound response message type.</typeparam>
public interface ICapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    /// <summary>
    /// Executes the full dispatch pipeline:
    /// <list type="number">
    ///   <item>Resolves the <see cref="Forge.Aspects.CapabilityAspect"/> from the store by <paramref name="capabilityAspectIri"/> (permissive when null).</item>
    ///   <item>Validates the command against the resolved command aspect (permissive when null).</item>
    ///   <item>Calls the underlying <see cref="ICapabilityHandler{TCommand,TResponse}"/>.</item>
    ///   <item>Validates the response and all emitted events against the resolved aspects.</item>
    /// </list>
    /// Throws <see cref="Forge.Aspects.Message.MessageAspectViolationException"/> if any
    /// SHACL constraint is violated.
    /// </summary>
    /// <param name="capabilityAspectIri">
    /// IRI of a <see cref="Forge.Aspects.CapabilityAspect"/> registered in the
    /// <see cref="Forge.Aspects.IAspectStore"/>, or <c>null</c> for a fully permissive
    /// execution (no SHACL validation on any message). See Capability ADR-0007.
    /// </param>
    ValueTask<CapabilityResult<TResponse>> DispatchAsync(
        TCommand command,
        string? capabilityAspectIri = null,
        CancellationToken cancellationToken = default);
}
