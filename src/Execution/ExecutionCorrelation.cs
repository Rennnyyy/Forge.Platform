namespace Forge.Execution;

/// <summary>
/// Carries the correlation identifiers for a single execution.
/// Propagated via <see cref="ExecutionScope"/> and surfaced onto HTTP response headers
/// by <c>ExecutionCorrelationMiddleware</c>.
/// See Execution ADR-0002.
/// </summary>
public sealed record ExecutionCorrelation
{
    /// <summary>
    /// Unique identifier assigned to this execution, generated at dispatch time.
    /// Surfaced as the <c>X-Forge-Execution-ID</c> response header.
    /// </summary>
    public Guid ExecutionId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Correlation identifier supplied by the caller, typically forwarded from
    /// the <c>X-Forge-Correlation-ID</c> request header.
    /// <c>null</c> when the caller did not supply one.
    /// </summary>
    public Guid? CallerCorrelationId { get; init; }
}
