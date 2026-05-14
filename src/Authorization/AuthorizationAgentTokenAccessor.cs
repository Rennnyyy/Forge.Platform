using Forge.Execution;

namespace Forge.Authorization;

/// <summary>
/// <see cref="IAgentTokenAccessor"/> implementation backed by the ambient
/// <see cref="AuthorizationContext.CurrentAgentToken"/> <c>AsyncLocal</c>.
/// Register this as a singleton in any host that uses <c>Forge.Authorization</c>.
/// See Capability ADR-0019.
/// </summary>
public sealed class AuthorizationAgentTokenAccessor : IAgentTokenAccessor
{
    public string? GetAgentToken() => AuthorizationContext.CurrentAgentToken;
}
