using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// Thrown at application startup when a code-origin TTL file fails to parse as valid Turtle.
/// The application must not start with broken shape definitions.
/// </summary>
public sealed class AspectTtlParseException : Exception
{
    /// <summary>
    /// The first ~200 characters of the offending TTL content to aid diagnosis,
    /// or the full content if it is shorter.
    /// </summary>
    public string TtlExcerpt { get; }

    public AspectTtlParseException(string ttlExcerpt, Exception innerException)
        : base($"Failed to parse SHACL Turtle: {innerException.Message}", innerException)
    {
        TtlExcerpt = ttlExcerpt.Length > 200 ? ttlExcerpt[..200] + "…" : ttlExcerpt;
    }
}
