using Microsoft.AspNetCore.Http;

namespace Forge.Branch.Http;

/// <summary>
/// Resolves the branch IRI for the current HTTP request.
/// A <c>null</c> return signals "absent header — use the configured default branch".
/// An absent header is not an error; a structurally invalid value is.
/// See Branch.Http ADR-0002.
/// </summary>
public interface IBranchIriProvider
{
    /// <summary>
    /// Returns the branch IRI from <paramref name="context"/>, or <c>null</c> when no
    /// branch header is present (middleware falls back to the configured default branch).
    /// Implementations must return <c>null</c> for an absent or whitespace header value
    /// and must throw <see cref="InvalidBranchIriException"/> for a structurally invalid
    /// non-empty value.
    /// </summary>
    ValueTask<string?> GetBranchIriAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}
