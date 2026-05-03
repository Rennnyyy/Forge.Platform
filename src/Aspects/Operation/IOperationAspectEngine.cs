using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Aspects.Operation;

/// <summary>
/// Orchestrates the Local and Context validation passes for a single
/// <see cref="TransactionOperation"/>. See Aspects ADR-0001 for pipeline details.
/// </summary>
public interface IOperationAspectEngine
{
    /// <summary>
    /// Validate <paramref name="operation"/> against its declared aspect.
    /// <list type="bullet">
    ///   <item>If <c>AspectIri</c> equals <see cref="Forge.Aspects.Aspect.NoOpIri"/>, returns immediately.</item>
    ///   <item>Otherwise resolves the <see cref="IOperationAspect"/> from <see cref="IAspectStore"/>,
    ///   runs the Local SHACL pass, then the Context SPARQL pass.</item>
    ///   <item>On violation throws <see cref="AspectViolationException"/>.</item>
    ///   <item>On unregistered IRI throws <see cref="AspectNotFoundException"/>.</item>
    /// </list>
    /// </summary>
    ValueTask ValidateAsync(
        TransactionOperation operation,
        ISparqlQueryStore queryStore,
        CancellationToken cancellationToken = default);
}
