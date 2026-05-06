using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// Sample entity used to demonstrate the full Forge platform stack:
/// <list type="bullet">
///   <item><c>Forge.Entity.Generators</c> emits the partial structural half.</item>
///   <item><c>Forge.Operations.Generators</c> emits active-record CRUD methods
///         (<c>CreateAsync</c>, <c>ReadAsync</c>, <c>UpdateAsync</c>, <c>DeleteAsync</c>,
///         <c>ListAsync</c>).</item>
///   <item><c>[OperationEndpoints]</c> + <c>MapOperations()</c> expose five REST endpoints
///         under <c>api/entities/books</c> (POST, GET, PUT, DELETE).</item>
/// </list>
/// See Operations.Http ADR-0001, Operations ADR-*, Entity ADR-*.
/// </summary>
[Entity(Path = "books")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
[OperationEndpoints]
public partial class Book
{
    /// <summary>
    /// Primary identifier — the ISBN-13 string.
    /// Forms the IRI suffix: <c>{baseIri}/books/{isbn}</c>.
    /// </summary>
    [IdentityPart(0)]
    [Predicate("isbn")]
    public partial string Isbn { get; init; }

    [Predicate("title")]
    public string Title { get; set; } = string.Empty;

    [Predicate("author")]
    public string Author { get; set; } = string.Empty;

    [Predicate("publishedYear")]
    public int PublishedYear { get; set; }

    [Predicate("available")]
    public bool Available { get; set; } = true;
}
