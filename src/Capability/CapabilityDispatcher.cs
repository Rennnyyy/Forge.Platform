using Forge.Aspects;
using Forge.Aspects.Message;
using Forge.Repository;
using Forge.Authorization;

namespace Forge.Capability;

/// <summary>
/// Default implementation of <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>.
/// Implements the pipeline defined in Capability ADR-0002, ADR-0006, and ADR-0007:
/// <list type="number">
///   <item>Resolve <see cref="CapabilityAspect"/> from <see cref="IAspectStore"/> by IRI.</item>
///   <item>Capture <see cref="AuthorizationContext.CurrentAgentToken"/> and forward to <see cref="CapabilityContext.AgentToken"/>.</item>
///   <item>Authorize command via <see cref="IAspectGuard"/> (before SHACL). See Validation ADR-0004.</item>
///   <item>Validate command shape via <see cref="IMessageAspectEngine"/> (SHACL).</item>
///   <item>Build <see cref="CapabilityContext"/> and call handler.</item>
///   <item>Authorize and validate response and events (Ok only).</item>
/// </list>
/// </summary>
internal sealed class CapabilityDispatcher<TCommand, TResponse> : ICapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly ICapabilityHandler<TCommand, TResponse> _handler;
    private readonly IMessageAspectEngine _engine;
    private readonly IAspectStore _store;
    private readonly IAspectGuard _guard;

    public CapabilityDispatcher(
        ICapabilityHandler<TCommand, TResponse> handler,
        IMessageAspectEngine engine,
        IAspectStore store,
        IAspectGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(store);

        _handler = handler;
        _engine  = engine;
        _store   = store;
        _guard   = guard ?? AllowAllAspectGuard.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask<CapabilityResult<TResponse>> DispatchAsync(
        TCommand command,
        string? capabilityAspectIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ① Resolve the capability aspect (null means permissive).
        var capAspect = capabilityAspectIri is not null
            ? _store.TryResolveCapabilityAspect(capabilityAspectIri)
            : null;

        // Resolve per-slot message aspects from the store.
        var commandAspect  = ResolveMessageAspect(capAspect?.CommandAspectIri);
        var responseAspect = ResolveMessageAspect(capAspect?.ResponseAspectIri);

        // ② Capture the ambient agent token.
        var agentToken = AuthorizationContext.CurrentAgentToken;
        var agentTokenForGuard = agentToken ?? string.Empty;

        // ③ Authorize command — guard runs before SHACL. See Capability ADR-0009.
        await _guard.AuthorizeAsync(
            agentTokenForGuard,
            commandAspect?.Iri ?? Aspect.NoOpIri,
            cancellationToken);

        // ④ Validate command shape. No-op when commandAspect is null or ShapeTtl is null.
        await _engine.ValidateAsync(command, commandAspect, cancellationToken);

        // ⑤ Build context — handler sees the CapabilityAspect record and the agent token.
        var context = new CapabilityContext
        {
            Aspect    = capAspect,
            AgentToken = agentToken,
        };

        // ⑥ Call handler.
        var result = await _handler.HandleAsync(command, context, cancellationToken);

        // ⑦ Authorize and validate response and events (Ok only).
        if (result is CapabilityResult<TResponse>.Ok ok)
        {
            await _guard.AuthorizeAsync(
                agentTokenForGuard,
                responseAspect?.Iri ?? Aspect.NoOpIri,
                cancellationToken);
            await _engine.ValidateAsync(ok.Response, responseAspect, cancellationToken);
        }

        foreach (var evt in result.Events)
        {
            IMessageAspect? eventAspect = null;
            if (capAspect?.EventAspectIris.TryGetValue(evt.GetType(), out var evtIri) == true && evtIri is not null)
                eventAspect = ResolveMessageAspect(evtIri);

            await _guard.AuthorizeAsync(
                agentTokenForGuard,
                eventAspect?.Iri ?? Aspect.NoOpIri,
                cancellationToken);
            await _engine.ValidateAsync(evt, eventAspect, cancellationToken);
        }

        return result;
    }

    private IMessageAspect? ResolveMessageAspect(string? iri)
        => iri is not null ? _store.TryResolveMessage(iri) : null;
}


