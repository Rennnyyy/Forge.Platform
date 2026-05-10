using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures.Sample;

/// <summary>
/// PropertyBasedPlain (Path) entity. Owns the <b>1:N</b> relationship to Album from the
/// "one" side: <see cref="Albums"/> persists the list of album IRIs released by this label.
/// </summary>
[Entity(Path = "labels", PredicatePath = "label")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
public partial class Label
{
    [IdentityPart(0)]
    [Predicate("slug")]
    public partial string Slug { get; init; }

    [Predicate("name")]
    public string Name { get; set; } = "";

    [Predicate("foundedYear")]
    public int FoundedYear { get; set; }

    /// <summary>1:N — this label released these albums (label owns the list).</summary>
    [Owning("hasAlbum")]
    public partial EntityRefCollection<Album> Albums { get; }
}
