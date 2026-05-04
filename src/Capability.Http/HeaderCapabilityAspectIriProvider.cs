using Microsoft.AspNetCore.Http;

namespace Forge.Capability.Http;

/// <summary>
/// Default <see cref="ICapabilityAspectIriProvider"/> that reads the
/// <c>X-Forge-Capability-AspectIri</c> request header.
/// Absent, empty, or whitespace-only header returns <c>null</c> (permissive).
/// See Capability.Http ADR-0003.
/// </summary>
internal sealed class HeaderCapabilityAspectIriProvider : ICapabilityAspectIriProvider
{
    internal const string HeaderName = "X-Forge-Capability-AspectIri";

    public ValueTask<string?> GetCapabilityAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var value = context.Request.Headers[HeaderName].FirstOrDefault();
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return ValueTask.FromResult<string?>(trimmed);
    }
}
