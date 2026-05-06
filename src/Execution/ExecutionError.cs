namespace Forge.Execution;

/// <summary>
/// Carries a structured error code and human-readable message for a failed execution result.
/// See Execution ADR-0001.
/// </summary>
public sealed record ExecutionError(string Code, string Message);
