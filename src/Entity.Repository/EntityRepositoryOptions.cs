namespace Forge.Entity.Repository;

/// <summary>
/// Options for the entity repository. Bound from the <c>Forge:EntityRepository</c>
/// configuration section by <c>AddForgeEntityRepository(IConfiguration)</c>.
/// </summary>
public sealed class EntityRepositoryOptions
{
    /// <summary>
    /// Backend discriminator. Recognized values are <c>"InMemory"</c> and <c>"GraphDb"</c>.
    /// Each backend's DI extension validates the value at registration time.
    /// </summary>
    public string Backend { get; set; } = "InMemory";

    /// <summary>
    /// Optional named-graph IRI. When set, all reads and writes are scoped to this graph.
    /// </summary>
    public string? NamedGraph { get; set; }

    /// <summary>
    /// Base IRI used to construct the per-type IRI emitted as <c>rdf:type</c> on every entity.
    /// Defaults to <c>{EntityOptions.BaseIri}/types</c>.
    /// </summary>
    public string? TypeBaseIri { get; set; }

    /// <summary>The <c>rdf:type</c> IRI for an entity type, derived from <see cref="TypeBaseIri"/>.</summary>
    public string ResolveTypeIri(string typeName, string? entityPath)
    {
        var b = TypeBaseIri?.TrimEnd('/') ?? $"{Forge.Entity.EntityOptions.Current.BaseIri.TrimEnd('/')}/types";
        var leaf = string.IsNullOrEmpty(entityPath) ? typeName : entityPath!.Trim('/');
        return $"{b}/{leaf}";
    }
}
