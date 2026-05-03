using Forge.Aspects.Message;

namespace Forge.Capability;

/// <summary>
/// Default implementation of <see cref="ICapabilityDispatcher{TCommand,TResponse}"/>.
/// Implements the pipeline defined in Capability ADR-0002, ADR-0006, and ADR-0007:
/// <list type="number">
///   <item>Validate command against the per-call <see cref="CapabilityAspects.CommandAspect"/> (null = permissive).</item>
///   <item>Build <see cref="CapabilityContext"/> from the caller-supplied aspects.</item>
///   <item>Call handler.</item>
///   <item>Validate response (Ok only) and all emitted events against their per-call aspects.</item>
/// </list>
/// </summary>
internal sealed class CapabilityDispatcher<TCommand, TResponse> : ICapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    private readonly ICapabilityHandler<TCommand, TResponse> _handler;
    private readonly IMessageAspectEngine _engine;

    public CapabilityDispatcher(
        ICapabilityHandler<TCommand, TResponse> handler,
        IMessageAspectEngine engine)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(engine);

        _handler = handler;
        _engine = engine;
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

        // ① Validate command. No-op when commandAspect is null or ShapeTtl is null.
        await _engine.ValidateAsync(command, commandAspect, cancellationToken);

        // ② Build context — handler sees the full set of per-call aspects.
        var context = new CapabilityContext
        {
            CommandAspect  = commandAspect,
            ResponseAspect = responseAspect,
            EventAspects   = eventAspects,
        };

        // ③ Call handler.
        var result = await _handler.HandleAsync(command, context, cancellationToken);

        // ④ Validate response and events.
        if (result is CapabilityResult<TResponse>.Ok ok)
            await _engine.ValidateAsync(ok.Response, responseAspect, cancellationToken);

        foreach (var evt in result.Events)
        {
            eventAspects.TryGetValue(evt.GetType(), out var eventAspect);
            await _engine.ValidateAsync(evt, eventAspect, cancellationToken);
        }

        return result;
    }
}
