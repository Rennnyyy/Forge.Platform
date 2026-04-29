using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

[Entity(Path = "colors")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
[Enumeration]
public sealed partial class Color
{
    [IdentityPart(0)]
    public partial string Name { get; init; }

    private Color(string name) { Name = name; }

    public static readonly Color Red   = new("red");
    public static readonly Color Green = new("green");
    public static readonly Color Blue  = new("blue");

    public static IReadOnlyList<Color> All { get; } = [Red, Green, Blue];
}
