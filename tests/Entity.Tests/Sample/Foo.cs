using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

[Entity(Path = "foos", PredicatePath = "foo")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
public partial class Foo
{
    [IdentityPart(0)]
    public partial string Slug { get; init; }

    public string? Description { get; set; }

    [Owning("hasPrimaryBar")]
    public partial EntityRef<Bar>? PrimaryBar { get; set; }

    [Owning("hasBar")]
    public partial EntityRefCollection<Bar> Bars { get; }
}
