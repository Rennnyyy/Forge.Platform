namespace Forge.Branch;

/// <summary>
/// Thrown when a transaction operation would violate a branch protection invariant:
/// <list type="bullet">
///   <item>Deleting the default branch entity.</item>
///   <item>Dropping the management graph.</item>
/// </list>
/// </summary>
public sealed class BranchProtectionViolationException : InvalidOperationException
{
    /// <summary>The IRI of the protected resource that the operation targeted.</summary>
    public string ProtectedIri { get; }

    /// <summary>Initializes a new instance with a formatted message.</summary>
    internal BranchProtectionViolationException(string message, string protectedIri)
        : base(message)
    {
        ProtectedIri = protectedIri;
    }

    /// <summary>Throws for an attempt to delete the default branch entity.</summary>
    internal static BranchProtectionViolationException DefaultBranchDelete(string iri) =>
        new($"Cannot delete the default branch '{iri}'. " +
            "Change the default branch IRI in configuration before deleting this branch.", iri);

    /// <summary>Throws for an attempt to drop the management graph.</summary>
    internal static BranchProtectionViolationException ManagementGraphDrop(string iri) =>
        new($"Cannot drop the management graph '{iri}'. " +
            "The management graph is a protected system resource.", iri);
}
