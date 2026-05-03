namespace Forge.Repository;

/// <summary>
/// Marker for a named validation policy attached to a <see cref="TransactionOperation"/>.
/// Use <see cref="Aspect.NoOp"/> to declare that no validation applies.
/// </summary>
/// <remarks>
/// Thin identity token only. Shape data (SHACL TTL, SPARQL) is carried by
/// <c>IOperationAspect</c> and <c>IQueryAspect</c> in the <c>Forge.Aspects</c> slice,
/// which extend this interface. Placing the token here avoids a circular project reference
/// between Repository and Aspects (see Aspects ADR-0004, ADR-0009).
/// </remarks>
public interface IAspect
{
    /// <summary>A human-readable name identifying this aspect. The value "noop" is reserved.</summary>
    string Name { get; }
}
