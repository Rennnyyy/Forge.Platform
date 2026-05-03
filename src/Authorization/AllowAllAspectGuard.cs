namespace Forge.Authorization;

/// <summary>
/// Default <see cref="IAspectGuard"/> implementation that unconditionally authorizes
/// every operation — the "allow all" stub. See Validation ADR-0004.
/// </summary>
/// <remarks>
/// Use <see cref="Instance"/> rather than constructing a new instance. This is the
/// guard wired by default when no custom guard is supplied.
/// </remarks>
public sealed class AllowAllAspectGuard : IAspectGuard
{
    /// <summary>The singleton allow-all guard instance.</summary>
    public static readonly AllowAllAspectGuard Instance = new();

    private AllowAllAspectGuard() { }

    /// <inheritdoc/>
    /// <remarks>Always returns a completed <see cref="ValueTask"/>; neither token is inspected.</remarks>
    public ValueTask AuthorizeAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default)
        => default;
}
