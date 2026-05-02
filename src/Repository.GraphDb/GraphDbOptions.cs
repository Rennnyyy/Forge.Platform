using Forge.Entity;
namespace Forge.Repository.GraphDb;

/// <summary>
/// Configuration for the Ontotext GraphDB backend. Bound from
/// <c>Forge:GraphDb</c> by <c>AddGraphDbEntityStore</c>.
/// </summary>
public sealed class GraphDbOptions
{
    /// <summary>Base URL of the GraphDB server, e.g. <c>http://localhost:7200</c>.</summary>
    public string BaseUrl { get; set; } = "http://localhost:7200";

    /// <summary>Repository identifier inside the server (must already exist).</summary>
    public string RepositoryId { get; set; } = "forge";

    /// <summary>Optional Basic-Auth username.</summary>
    public string? Username { get; set; }

    /// <summary>Optional Basic-Auth password.</summary>
    public string? Password { get; set; }

    /// <summary>HTTP request timeout. Default 30 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The full query endpoint URL.</summary>
    public string QueryEndpoint => $"{BaseUrl.TrimEnd('/')}/repositories/{RepositoryId}";

    /// <summary>The full update endpoint URL.</summary>
    public string UpdateEndpoint => $"{BaseUrl.TrimEnd('/')}/repositories/{RepositoryId}/statements";
}
