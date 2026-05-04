using Microsoft.AspNetCore.Http;

namespace Forge.Capability.Http;

/// <summary>
/// Resolves the capability-aspect IRI for the current HTTP request.
/// The IRI is passed unchanged to
/// <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/> as
/// <c>capabilityAspectIri</c>. A <c>null</c> return means fully permissive dispatch
/// (no SHACL validation). See Capability.Http ADR-0003.
/// </summary>
public interface ICapabilityAspectIriProvider
{
    /// <summary>
    /// Returns the capability-aspect IRI for <paramref name="context"/>, or
    /// <c>null</c> if none is present (permissive).
    /// </summary>
    ValueTask<string?> GetCapabilityAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
