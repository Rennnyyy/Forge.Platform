namespace Forge.Branch;

/// <summary>
/// Exposes the platform's configured default branch IRI as a static, globally accessible
/// property. Populated once by <c>AddForgeBranch()</c> at DI registration time.
/// </summary>
/// <remarks>
/// The property is guaranteed non-null (but may be an empty string) after DI registration
/// completes. Reading it before <c>AddForgeBranch()</c> has been called yields an empty
/// string — a detectable misconfiguration state per Branch ADR-0001.
/// <para>
/// The class is named <c>BranchDefault</c> (not <c>Default</c>) because <c>Default</c> is a
/// reserved keyword in pattern expressions in C#.
/// </para>
/// </remarks>
public static class BranchDefault
{
    /// <summary>
    /// The IRI of the default branch named graph (i.e. <c>BranchOptions.DefaultBranchIri</c>).
    /// Populated by <c>AddForgeBranch()</c>; empty string before that point.
    /// </summary>
    public static string BranchIri { get; internal set; } = string.Empty;
}
