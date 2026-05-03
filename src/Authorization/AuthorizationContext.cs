namespace Forge.Authorization;

/// <summary>
/// Ambient binding that propagates the calling agent's identity token through the
/// async call stack to the <see cref="GuardedTransactionalStore"/>. See Authorization ADR-0002.
/// </summary>
/// <remarks>
/// <para>
/// Bind the token once at the top of the call stack (e.g. in ASP.NET Core middleware)
/// and every downstream transaction or query within that scope carries it automatically:
/// <code>
/// using var _ = AuthorizationContext.Use(bearerToken);
/// await artist.CreateAsync();   // guarded — agent token flows through
/// </code>
/// </para>
/// <para>
/// Tests that use <see cref="AllowAllAspectGuard"/> do not need to call
/// <see cref="Use"/> because the allow-all guard ignores the token. A stricter guard
/// that rejects an empty agent token forces authenticated call sites to establish a scope.
/// </para>
/// </remarks>
public static class AuthorizationContext
{
    private static readonly AsyncLocal<string?> _agentToken = new();

    /// <summary>
    /// The agent token bound to the current async control flow, or <see langword="null"/>
    /// if no scope has been opened with <see cref="Use"/>.
    /// </summary>
    public static string? CurrentAgentToken => _agentToken.Value;

    /// <summary>
    /// Opens an ambient scope that carries <paramref name="agentToken"/> for the current
    /// async control flow. Dispose the returned handle to restore the previous value.
    /// </summary>
    /// <param name="agentToken">The identity token of the calling agent. Must not be null or whitespace.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the prior agent token on dispose.</returns>
    public static IDisposable Use(string agentToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentToken);
        var previous = _agentToken.Value;
        _agentToken.Value = agentToken;
        return new AgentTokenScope(previous);
    }

    private sealed class AgentTokenScope(string? previous) : IDisposable
    {
        public void Dispose() => _agentToken.Value = previous;
    }
}
