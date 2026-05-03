using Forge.Aspects.Message;
using Forge.Repository;
using Forge.Authorization;

namespace Forge.Capability;

/// <summary>
/// Default implementation of <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>.
/// Implements the pipeline defined in Capability ADR-0002, ADR-0006, and ADR-0007:
/// <list type="number">
///   <item>Capture <see cref="ValidationContext.CurrentAgentToken"/> and forward it to <see cref="CapabilityContext.AgentToken"/>.</item>
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
    private readonly IAspectGuard _guard;

    public CapabilityDispatcher(
        ICapabilityHandler<TCommand, TResponse> handler,
        IMessageAspectEngine engine,
        IAspectGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(engine);

        _handler = handler;
        _engine  = engine;
        _guard   = guard ?? AllowAllAspectGuard.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask<CapabilityResult<TResponse>> DispatchAsync(
        TCommand command,
        CapabilityAspects? aspects = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandAspect  = aspects?.CommandAspect;
        var responseAspect = aspects?.ResponseAspect;
        var eventAspects   = aspects?.EventAspects
                             ?? System.Collections.Immutable.ImmutableDictionary<Type, IMessageAspect>.Empty;

        // ① Capture the ambient agent token. The dispatcher only observes it.
        //   See Capability ADR-0008.
        var agentToken = AuthorizationContext.CurrentAgentToken;
        var agentTokenForGuard = agentToken ?? string.Empty;

        // ② Authorize command — guard runs before SHACL. See Capability ADR-0009.
        await _guard.AuthorizeAsync(
            agentTokenForGuard,
            commandAspect?.Name ?? Aspect.NoOp.Name,
            cancellationToken);

        // ③ Validate command shape. No-op when commandAspect is null or ShapeTtl is null.
        await _engine.ValidateAsync(command, commandAspect, cancellationToken);

        // ④ Build context — handler sees the full set of per-call aspects and the agent token.
        var context = new CapabilityContext
        {
            CommandAspect  = commandAspect,
            ResponseAspect = responseAspect,
            EventAspects   = eventAspects,
            AgentToken     = agentToken,
        };

        // ⑤ Call handler.
        var result = await _handler.HandleAsync(command, context, cancellationToken);

        // ⑥ Authorize and validate response and events (Ok only).
        if (result is CapabilityResult<TResponse>.Ok ok)
        {
            await _guard.AuthorizeAsync(
                agentTokenForGuard,
                responseAspect?.Name ?? Aspect.NoOp.Name,
                cancellationToken);
            await _engine.ValidateAsync(ok.Response, responseAspect, cancellationToken);
        }

        foreach (var evt in result.Events)
        {
            eventAspects.TryGetValue(evt.GetType(), out var eventAspect);
            await _guard.AuthorizeAsync(
                agentTokenForGuard,
                eventAspect?.Name ?? Aspect.NoOp.Name,
                cancellationToken);
            await _engine.ValidateAsync(evt, eventAspect, cancellationToken);
        }

        return result;
    }
}
