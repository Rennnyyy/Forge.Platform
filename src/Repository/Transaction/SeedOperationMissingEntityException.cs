namespace Forge.Repository.Transaction;

/// <summary>
/// Thrown when a <see cref="SeedGraphOperation"/> cannot find one or more of the
/// requested entity IRIs in the source graph. The entire transaction is aborted.
/// See Repository ADR-0004.
/// </summary>
public sealed class SeedOperationMissingEntityException : Exception
{
    /// <summary>
    /// Initializes the exception with the source graph IRI and the set of IRIs that
    /// were not found.
    /// </summary>
    /// <param name="sourceGraphIri">The IRI of the source named graph that was queried.</param>
    /// <param name="missingIris">The entity IRIs that were absent from the source graph.</param>
    public SeedOperationMissingEntityException(string sourceGraphIri, IReadOnlyList<string> missingIris)
        : base(BuildMessage(sourceGraphIri, missingIris))
    {
        SourceGraphIri = sourceGraphIri;
        MissingIris = missingIris;
    }

    /// <summary>The IRI of the source named graph that was queried.</summary>
    public string SourceGraphIri { get; }

    /// <summary>The entity IRIs that were absent from the source graph.</summary>
    public IReadOnlyList<string> MissingIris { get; }

    private static string BuildMessage(string sourceGraphIri, IReadOnlyList<string> missingIris)
    {
        var joined = string.Join(", ", missingIris);
        return $"SeedGraphOperation aborted: the following entity IRIs were not found in source graph " +
               $"<{sourceGraphIri}>: {joined}";
    }
}
