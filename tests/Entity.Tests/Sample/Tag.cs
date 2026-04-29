using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

[Entity(Path = "tags", PredicatePath = "tag")]
[Identity(IdentityGenerator.Random)]
public partial class Tag
{
    [Required]
    public string Label { get; set; } = "";

    [Inverse(nameof(Author.Tags), "isTagOf")]
    public partial EntityRefCollection<Author> Authors { get; }
}
