using Microsoft.AspNetCore.Http;

namespace Forge.Execution.Http;

/// <summary>
/// <see cref="IBranchIriProvider"/> implementation that reads the branch IRI from
/// the <c>X-Forge-BranchIri</c> request header. Returns <c>null</c> when the header
/// is absent or whitespace. Throws <see cref="InvalidBranchIriException"/> when the
/// value is present but not a valid absolute URI.
/// See Execution.Http ADR-0001.
/// </summary>
public sealed class HeaderBranchIriProvider : IBranchIriProvider
{
    internal const string BranchIriRequestHeader = "X-Forge-BranchIri";

    public ValueTask<string?> GetBranchIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var raw = context.Request.Headers[BranchIriRequestHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return ValueTask.FromResult<string?>(null);

        var trimmed = raw.Trim();
        if (!Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
            throw new InvalidBranchIriException(trimmed);

        return ValueTask.FromResult<string?>(trimmed);
    }
}
