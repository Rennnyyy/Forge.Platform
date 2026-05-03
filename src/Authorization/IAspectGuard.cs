namespace Forge.Authorization;

/// <summary>
/// Authorization primitive: decides whether a given agent may act under a given
/// aspect policy. See Validation ADR-0004.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Default behavior</strong> — use <see cref="AllowAllAspectGuard.Instance"/>
/// to allow every operation unconditionally. Enforcement is an explicit configuration
/// choice.
/// </para>
/// <para>
/// <strong>Denial</strong> — throw any exception to deny. There is no prescribed
/// exception type; implementations choose what is most appropriate for their context.
/// </para>
/// <para>
/// <strong>Agent token</strong> — bind the caller's identity via
/// <see cref="ValidationContext.Use"/>. Call sites pass
/// <see cref="ValidationContext.CurrentAgentToken"/> (or an empty string when no scope
/// is active). A strict guard should treat an empty token as anonymous and reject it.
/// </para>
/// </remarks>
public interface IAspectGuard
{
    /// <summary>
    /// Authorizes an agent to perform an operation under the given aspect policy.
    /// Return normally to allow; throw to deny.
    /// </summary>
    /// <param name="agentToken">The calling agent's identity token.</param>
    /// <param name="aspectToken">
    /// The policy name: <c>aspect.Name</c> for a named aspect, or <c>"noop"</c> when
    /// no explicit aspect applies (permissive).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AuthorizeAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default);
}
