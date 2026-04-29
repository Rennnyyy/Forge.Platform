using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

[Entity(Path = "authors", PredicatePath = "author")]
[Identity(IdentityGenerator.Random)]
public partial class Author
{
    [Required]
    public string Name { get; set; } = "";

    [Owning("hasTag", Lazy = true)]
    public partial EntityRefCollection<Tag> Tags { get; }
}
