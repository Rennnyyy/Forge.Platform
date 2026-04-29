using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

[Entity(Path = "bars", PredicatePath = "bar")]
[Identity(IdentityGenerator.Random)]
public partial class Bar
{
    [Required]
    public string Name { get; set; } = "";

    [Inverse(nameof(Foo.PrimaryBar), "isPrimaryBarOf")]
    public partial EntityRef<Foo>? Owner { get; }

    [Inverse(nameof(Foo.Bars), "isBarOf")]
    public partial EntityRef<Foo>? Container { get; }
}
