using System.Text.RegularExpressions;

namespace Forge.Capability;

/// <summary>
/// Immutable value object wrapping a dot-separated capability identity string
/// (e.g. <c>"catalog.artists.create"</c>).
/// <para>
/// Each dot-separated segment must match <c>[a-z0-9]([a-z0-9-]*[a-z0-9])?</c>:
/// lowercase letters, digits, and internal hyphens only; no uppercase, no underscores.
/// </para>
/// See Capability ADR-0010.
/// </summary>
public sealed record CapabilityIdentity
{
    private static readonly Regex SegmentPattern = new(
        @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>The raw dot-separated identity string.</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs and validates a capability identity.
    /// </summary>
    /// <param name="value">
    /// A dot-separated string where every segment satisfies
    /// <c>^[a-z0-9]([a-z0-9-]*[a-z0-9])?$</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is null, empty, or contains an invalid segment.
    /// </exception>
    public CapabilityIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var segments = value.Split('.');
        foreach (var segment in segments)
        {
            if (!SegmentPattern.IsMatch(segment))
                throw new ArgumentException(
                    $"Capability identity segment '{segment}' is invalid. " +
                    "Each segment must contain only lowercase letters (a–z), digits (0–9), " +
                    "and hyphens (-) in interior positions, and must not be empty.",
                    nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Converts the identity to an HTTP-compatible route path by replacing each
    /// <c>.</c> separator with <c>/</c>.  For example, <c>"catalog.artists.create"</c>
    /// becomes <c>"catalog/artists/create"</c>.
    /// </summary>
    public string ToRoutePath() => Value.Replace('.', '/');

    /// <inheritdoc/>
    public override string ToString() => Value;
}
