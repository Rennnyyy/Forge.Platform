using Forge.Repository.GraphDb;
using System.Reflection;
using Shouldly;

namespace Forge.Repository.GraphDb.Tests;

/// <summary>
/// Compile-time guard — <see cref="GraphDbOptions"/> must not expose any
/// native-SHACL-related member. Direct enforcement of Aspects ADR-0002.
/// This test will fail CI if someone adds such a member to GraphDbOptions.
/// Moved from Forge.Aspects.Tests (was flaw #5 — Aspects.Tests must not depend on GraphDb assembly).
/// </summary>
public sealed class GraphDbOptionsGuardTest
{
    private static readonly string[] ForbiddenPatterns =
    [
        "NativeShacl", "UseShacl", "EnableShacl", "NativeShacl",
        "UseNativeShacl", "EnableNativeShacl", "ShaclEnabled",
    ];

    [Fact]
    public void GraphDbOptions_has_no_native_shacl_member()
    {
        var type = typeof(GraphDbOptions);
        var allMembers = type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.NonPublic);

        foreach (var member in allMembers)
        {
            foreach (var pattern in ForbiddenPatterns)
            {
                member.Name.ShouldNotContain(
                    pattern,
                    Case.Insensitive,
                    $"GraphDbOptions must not expose native-SHACL configuration (ADR-0002). " +
                    $"Found forbidden member: '{member.Name}' matching pattern '{pattern}'.");
            }
        }
    }
}
