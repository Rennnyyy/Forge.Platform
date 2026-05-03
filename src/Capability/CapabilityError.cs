namespace Forge.Capability;

/// <summary>
/// Carries a structured error code and human-readable message for a failed capability result.
/// See Capability ADR-0005.
/// </summary>
public sealed record CapabilityError(string Code, string Message);
