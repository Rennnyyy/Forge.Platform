namespace Forge.Repository;

/// <summary>
/// Provides ambient, async-local access to the branch IRI for the current logical
/// execution flow. Store implementations read this value to determine which RDF named
/// graph to target; when it is <see langword="null"/> they fall back to
/// <see cref="EntityRepositoryOptions.DefaultBranchIri"/>.
/// See Repository ADR-0002.
/// </summary>
/// <remarks>
/// Bind the branch IRI once at the top of the call stack (e.g. in ASP.NET Core
/// middleware) and every downstream store operation within that scope automatically
/// targets the correct named graph:
/// <code>
/// using var _ = BranchScope.Use("https://forge-it.net/branches/feature-X");
/// var artist = await repo.LoadAsync(iri);   // reads from feature-X graph
/// </code>
/// Nested scopes are fully supported: disposing an inner scope restores the outer
/// branch IRI, not null.
/// </remarks>
public static class BranchScope
{
    private static readonly AsyncLocal<string?> _branchIri = new();

    /// <summary>
    /// The branch IRI bound to the current async control flow, or <see langword="null"/>
    /// when no scope has been opened with <see cref="Use"/>. Consumers fall back to
    /// <see cref="EntityRepositoryOptions.DefaultBranchIri"/> when this is null.
    /// </summary>
    public static string? Current => _branchIri.Value;

    /// <summary>
    /// Opens an ambient scope that routes all store operations in the current async
    /// control flow to the named graph identified by <paramref name="branchIri"/>.
    /// Dispose the returned handle to restore the previous branch IRI.
    /// </summary>
    /// <param name="branchIri">
    /// The IRI of the target branch named graph. Must not be null or whitespace.
    /// Full structural IRI validation (absolute URI) is the caller's responsibility.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> that restores the prior branch IRI (or null) on dispose.
    /// </returns>
    public static IDisposable Use(string branchIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchIri);
        var previous = _branchIri.Value;
        _branchIri.Value = branchIri;
        return new BranchIriScope(previous);
    }

    private sealed class BranchIriScope(string? previous) : IDisposable
    {
        public void Dispose() => _branchIri.Value = previous;
    }
}
