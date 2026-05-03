using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Aspects;

/// <summary>
/// Thrown by the Aspects engine when one or more SHACL constraints are violated.
/// The surrounding <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/> catches
/// this exception and rolls back the transaction.
/// </summary>
public sealed class AspectViolationException : Exception
{
    /// <summary>All violations reported for the rejected operation.</summary>
    public IReadOnlyList<AspectViolation> Violations { get; }

    /// <summary>The operation that was rejected.</summary>
    public TransactionOperation RejectedOperation { get; }

    /// <summary>The IRI of the aspect whose shape produced the violation.</summary>
    public string SourceAspectIri { get; }

    public AspectViolationException(
        IReadOnlyList<AspectViolation> violations,
        TransactionOperation rejectedOperation,
        string sourceAspectIri)
        : base(BuildMessage(violations, rejectedOperation, sourceAspectIri))
    {
        Violations = violations;
        RejectedOperation = rejectedOperation;
        SourceAspectIri = sourceAspectIri;
    }

    private static string BuildMessage(
        IReadOnlyList<AspectViolation> violations,
        TransactionOperation op,
        string aspectIri)
    {
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectIri}' rejected operation on <{op.EntityIri}>: {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
