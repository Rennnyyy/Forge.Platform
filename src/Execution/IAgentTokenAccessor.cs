namespace Forge.Execution;

/// <summary>
/// Abstracts ambient agent-identity access so that consumers in the execution layer
/// do not need a direct reference to the authorization infrastructure.
/// The default implementation reads from <c>AuthorizationContext.CurrentAgentToken</c>
/// in <c>Forge.Authorization</c>.
/// See Capability ADR-0019.
/// </summary>
public interface IAgentTokenAccessor
{
    /// <summary>
    /// Returns the agent identity token for the current async call stack,
    /// or <see langword="null"/> if no authorization scope is active.
    /// </summary>
    string? GetAgentToken();
}
