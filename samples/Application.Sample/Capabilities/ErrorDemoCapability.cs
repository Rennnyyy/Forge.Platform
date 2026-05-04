using Forge.Capability;

namespace Forge.Application.Sample;

// ── TriggerFault  POST /api/capabilities/demo/fault ───────────────────────────

/// <summary>
/// Inbound command for the fault demonstration capability.
/// No fields are required; the handler ignores the payload entirely.
/// </summary>
public sealed record TriggerFaultCommand;

/// <summary>
/// Response type required by the generic handler signature.
/// Never actually returned — the handler always produces a <see cref="CapabilityResult{TResponse}.Fail"/>.
/// </summary>
public sealed record TriggerFaultResponse;

/// <summary>
/// Demonstration handler that <em>always</em> returns a structured failure.
/// Registered under the identity <c>demo.fault</c>; the HTTP transport maps this to
/// <c>POST /api/capabilities/demo/fault</c> and converts the <see cref="CapabilityResult{TResponse}.Fail"/>
/// to <c>422 Unprocessable Entity</c> with the <see cref="CapabilityError"/> as the JSON body.
/// See root ADR-0014 and Capability ADR-0005.
/// </summary>
[Capability("demo.fault")]
public sealed class TriggerFaultHandler : ICapabilityHandler<TriggerFaultCommand, TriggerFaultResponse>
{
    public ValueTask<CapabilityResult<TriggerFaultResponse>> HandleAsync(
        TriggerFaultCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<CapabilityResult<TriggerFaultResponse>>(
            new CapabilityResult<TriggerFaultResponse>.Fail(
                new CapabilityError("DEMO_FAULT", "This handler always fails by design.")));
    }
}
