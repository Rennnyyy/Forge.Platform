using Forge.Entity.Repository;

namespace Forge.Entity.Aspects;

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

    /// <summary>The name of the aspect whose shape produced the violation.</summary>
    public string SourceAspectName { get; }

    public AspectViolationException(
        IReadOnlyList<AspectViolation> violations,
        TransactionOperation rejectedOperation,
        string sourceAspectName)
        : base(BuildMessage(violations, rejectedOperation, sourceAspectName))
    {
        Violations = violations;
        RejectedOperation = rejectedOperation;
        SourceAspectName = sourceAspectName;
    }

    private static string BuildMessage(
        IReadOnlyList<AspectViolation> violations,
        TransactionOperation op,
        string aspectName)
    {
        var first = violations.Count > 0 ? violations[0].Message : "(no message)";
        return $"Aspect '{aspectName}' rejected operation on <{op.EntityIri}>: {first}" +
               (violations.Count > 1 ? $" (+{violations.Count - 1} more)" : string.Empty);
    }
}
