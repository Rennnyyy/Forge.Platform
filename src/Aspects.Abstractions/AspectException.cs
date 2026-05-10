namespace Forge.Aspects.Abstractions;

/// <summary>
/// Abstract base for all exceptions thrown by the Aspects engine.
/// Lets catch sites in HTTP transport layers reference a single type from
/// <c>Forge.Aspects.Abstractions</c> without depending on the full
/// <c>Forge.Aspects</c> implementation assembly.
/// </summary>
public abstract class AspectException : Exception
{
    /// <summary>Initializes the exception with a pre-built message string.</summary>
    protected AspectException(string message) : base(message) { }
}
