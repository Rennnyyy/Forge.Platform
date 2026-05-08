using Forge.Capability;
using Forge.Execution;

namespace Forge.Application.Sample;

// ── AspectDemo  POST /api/capabilities/demo/aspect ────────────────────────────

/// <summary>
/// Inbound command for the capability-aspect demonstration.
/// </summary>
public sealed record AspectDemoCommand(string Name);

/// <summary>
/// Response that reflects the input name and the IRI of the active
/// <see cref="Forge.Aspects.CapabilityAspect"/>, or <c>null</c> when no aspect
/// IRI was supplied (permissive dispatch).
/// </summary>
public sealed record AspectDemoResponse(string Name, string? ActiveAspectIri);

/// <summary>
/// Demonstrates the capability aspect pipeline end-to-end.
///
/// <para>
/// Two execution modes are possible, controlled by the caller via the
/// <c>X-Forge-Capability-AspectIri</c> HTTP header (Capability.Http ADR-0003):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Permissive</b> — no header (or an unregistered IRI): the dispatcher skips
///     SHACL validation entirely. <see cref="CapabilityContext.Aspect"/> is <c>null</c>
///     and <see cref="AspectDemoResponse.ActiveAspectIri"/> comes back as <c>null</c>.
///   </item>
///   <item>
///     <b>Policy-bound</b> — header value <c>urn:forge:aspects:capability:demo-v1</c>:
///     the dispatcher resolves the registered <see cref="Forge.Aspects.CapabilityAspect"/>
///     from the <see cref="Forge.Aspects.IAspectStore"/>, applies the SHACL shape for
///     the command (enforcing <c>Name</c> is non-empty), and forwards the resolved aspect
///     to the handler via <see cref="CapabilityContext.Aspect"/>. The response echoes
///     <c>urn:forge:aspects:capability:demo-v1</c> back so callers can confirm which
///     policy was active. See sample ADR-0003.
///   </item>
/// </list>
///
/// <para>
/// When the registered aspect is active and the command violates the SHACL shape
/// (e.g. <c>Name</c> is empty), <see cref="Forge.Aspects.Message.MessageAspectViolationException"/>
/// is thrown before the handler is reached.
/// </para>
/// </summary>
[Capability("demo.aspect")]
public sealed class AspectDemoHandler : ICapabilityHandler<AspectDemoCommand, AspectDemoResponse>
{
    public ValueTask<ExecutionResult<AspectDemoResponse>> HandleAsync(
        AspectDemoCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<ExecutionResult<AspectDemoResponse>>(
            new ExecutionResult<AspectDemoResponse>.Ok(
                new AspectDemoResponse(command.Name, context.Aspect?.Iri)));
    }
}
