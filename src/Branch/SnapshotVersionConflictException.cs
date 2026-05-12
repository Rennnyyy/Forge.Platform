namespace Forge.Branch;

/// <summary>
/// Thrown by <see cref="BranchSeedingService.CreateSnapshotAsync"/> when an attempt is
/// made to create a <see cref="Snapshot"/> whose semantic version tuple
/// <c>(Major, Minor, Patch, PreRelease)</c> already exists in the management graph.
/// See Branch ADR-0003.
/// </summary>
public sealed class SnapshotVersionConflictException : Exception
{
    /// <summary>The version string that already exists, e.g. <c>"1.2.3-alpha.1"</c>.</summary>
    public string Version { get; }

    private SnapshotVersionConflictException(string version, string message)
        : base(message)
    {
        Version = version;
    }

    internal static SnapshotVersionConflictException Duplicate(
        int major, int? minor, int? patch, string? preRelease)
    {
        var ver = FormatVersion(major, minor, patch, preRelease);
        return new SnapshotVersionConflictException(
            ver,
            $"A snapshot with version '{ver}' already exists in the management graph.");
    }

    private static string FormatVersion(int major, int? minor, int? patch, string? preRelease)
    {
        var version = $"{major}";
        if (minor is not null) version += $".{minor}";
        if (patch is not null) version += $".{patch}";
        if (!string.IsNullOrEmpty(preRelease)) version += $"-{preRelease}";
        return version;
    }
}
