namespace Forge.Repository;

/// <summary>
/// Base class for guard-layer violations that represent client-caused data integrity
/// errors (semantically "unprocessable entity", HTTP 422).
/// <para>
/// Examples: writing to an immutable snapshot graph, deleting a protected branch.
/// Concrete subtypes live in slices that own the relevant domain invariant
/// (e.g. <c>Forge.Branch</c>).
/// </para>
/// See Repository ADR-0005.
/// </summary>
public abstract class EntityGuardViolationException : InvalidOperationException
{
    protected EntityGuardViolationException(string message) : base(message) { }

    /// <summary>
    /// Short machine-readable code returned in the HTTP 422 response body.
    /// Subclasses override this to provide domain-specific codes (e.g. <c>SNAPSHOT_IMMUTABLE</c>).
    /// Defaults to <c>ENTITY_GUARD_VIOLATION</c>.
    /// </summary>
    public virtual string ErrorCode => "ENTITY_GUARD_VIOLATION";
}
