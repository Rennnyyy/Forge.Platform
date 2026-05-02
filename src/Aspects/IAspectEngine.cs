using Forge.Entity;
using Forge.Repository;

namespace Forge.Aspects;

/// <summary>
/// Orchestrates the Local and Context validation passes for a single
/// <see cref="TransactionOperation"/>. See Aspects ADR-0001 for pipeline details.
/// </summary>
public interface IAspectEngine
{
    /// <summary>
    /// Validate <paramref name="operation"/> against its declared aspect.
    /// <list type="bullet">
    ///   <item>If the aspect is <see cref="Aspect.NoOp"/>, returns immediately.</item>
    ///   <item>Otherwise resolves the shape, runs the Local pass, then the Context pass.</item>
    ///   <item>On violation (sh:Violation severity) throws <see cref="AspectViolationException"/>.</item>
    ///   <item>On unregistered aspect throws <see cref="AspectNotRegisteredException"/>.</item>
    /// </list>
    /// </summary>
    ValueTask ValidateAsync(
        TransactionOperation operation,
        ISparqlQueryStore queryStore,
        CancellationToken cancellationToken = default);
}
