namespace Forge.Execution.Http;

/// <summary>
/// Thrown by <see cref="IBranchIriProvider"/> when the supplied branch IRI value is
/// present but not a valid absolute URI. Caught by <see cref="BranchScopeMiddleware"/>
/// and translated to HTTP 400 Bad Request.
/// See Execution.Http ADR-0001.
/// </summary>
public sealed class InvalidBranchIriException(string value)
    : Exception($"The value '{value}' is not a valid absolute IRI.");
