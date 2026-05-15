namespace Forge.Structure;

/// <summary>
/// Ambient scope that makes a <see cref="StructureConfiguration"/> available to all
/// variant-aware operations in the current async control flow. Mirrors the
/// <c>QueryAspectScope</c> pattern from <c>Forge.Aspects</c>. See Variant ADR-0003.
/// </summary>
/// <example>
/// <code>
/// var config = new StructureConfiguration(
///     BranchIri: branchIri,
///     Options: new Dictionary&lt;string, OptionValue&gt;
///     {
///         [evDimensionIri] = new FlagOptionValue(true)
///     });
///
/// using var _ = StructureScope.Use(config);
/// var usages = await usageRepository.QueryByTypeAsync&lt;Usage&gt;(); // filtered by config
/// </code>
/// </example>
public static class StructureScope
{
    private static readonly AsyncLocal<StructureConfiguration?> _current = new();

    /// <summary>
    /// The <see cref="StructureConfiguration"/> bound to the current async control flow,
    /// or <c>null</c> if no scope has been opened. A <c>null</c> value means
    /// "no variant filtering" — all Usages pass.
    /// </summary>
    public static StructureConfiguration? Current => _current.Value;

    /// <summary>
    /// Opens an ambient scope that makes <paramref name="configuration"/> available via
    /// <see cref="Current"/> for all operations in the current async control flow.
    /// Disposing the returned handle restores the previous configuration (nested scopes
    /// are supported).
    /// </summary>
    /// <param name="configuration">The variant configuration to activate. Must not be null.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous scope on disposal.</returns>
    public static IDisposable Use(StructureConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var previous = _current.Value;
        _current.Value = configuration;
        return new VariantConfigScope(previous);
    }

    private sealed class VariantConfigScope(StructureConfiguration? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
