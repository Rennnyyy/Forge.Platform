using Microsoft.AspNetCore.Http;

namespace Forge.Execution.Http;

/// <summary>
/// <see cref="IExecutionAspectIriProvider"/> implementation that reads the aspect IRI
/// from a configurable request header.
/// Absent, empty, or whitespace-only header returns <c>null</c> (permissive).
/// The header name is supplied at construction time so each slice can use its own header
/// (e.g. <c>X-Forge-Capability-AspectIri</c> for Capability.Http).
/// See Execution ADR-0002.
/// </summary>
public sealed class HeaderExecutionAspectIriProvider : IExecutionAspectIriProvider
{
    private readonly string _headerName;

    public HeaderExecutionAspectIriProvider(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        _headerName = headerName;
    }

    public ValueTask<string?> GetAspectIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var value = context.Request.Headers[_headerName].FirstOrDefault();
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return ValueTask.FromResult<string?>(trimmed);
    }
}
