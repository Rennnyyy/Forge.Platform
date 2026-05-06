using Microsoft.AspNetCore.Http;

namespace Forge.Execution.Http;

/// <summary>
/// Resolves the execution-aspect IRI for the current HTTP request.
/// The IRI is forwarded to the dispatcher as the aspect IRI for SHACL validation.
/// A <c>null</c> return means fully permissive dispatch (no aspect applied).
/// See Execution ADR-0002.
/// </summary>
public interface IExecutionAspectIriProvider
{
    /// <summary>
    /// Returns the execution-aspect IRI for <paramref name="context"/>, or
    /// <c>null</c> if none is present (permissive).
    /// </summary>
    ValueTask<string?> GetAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
