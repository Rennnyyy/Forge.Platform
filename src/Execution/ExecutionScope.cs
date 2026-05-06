namespace Forge.Execution;

/// <summary>
/// Provides ambient, async-local access to the <see cref="ExecutionCorrelation"/>
/// for the current logical execution flow.
/// See Execution ADR-0002.
/// </summary>
/// <remarks>
/// Named <c>ExecutionScope</c> to avoid collision with
/// <see cref="System.Threading.ExecutionContext"/>.
/// </remarks>
public static class ExecutionScope
{
    private static readonly AsyncLocal<ExecutionCorrelation?> _correlation = new();

    /// <summary>Gets the ambient <see cref="ExecutionCorrelation"/>, or <c>null</c> when none is set.</summary>
    public static ExecutionCorrelation? Current => _correlation.Value;

    /// <summary>
    /// Sets <paramref name="correlation"/> as the ambient correlation for the current
    /// async flow and returns an <see cref="IDisposable"/> that clears it on disposal.
    /// </summary>
    public static IDisposable Use(ExecutionCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);
        _correlation.Value = correlation;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _correlation.Value = null;
    }
}
