using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Forge.Application.Sample.Tests;

/// <summary>
/// xUnit collection fixture that ensures a local GraphDB instance is available for the
/// Bruno/GraphDB integration tests. See ADR-0018.
/// <para>
/// Lifecycle:
/// <list type="number">
///   <item>Probe <c>:7200/rest/repositories</c> for an already-running GraphDB.</item>
///   <item>If not reachable, run <c>{cli} compose up -d</c> using the bundled
///         <c>docker-compose.graphdb.yml</c> and wait up to 90 s for the server to
///         become healthy. The CLI is auto-detected: <c>podman</c> is preferred;
///         <c>docker</c> is the fallback. Override with
///         <c>FORGE_CONTAINER_CLI=podman|docker</c>.</item>
///   <item>Ensure the <c>forge-sample-tests</c> repository exists.</item>
/// </list>
/// Sets <see cref="Available"/> to <c>false</c> only when neither approach succeeds
/// (e.g. no container runtime installed), in which case every dependent test skips and
/// the suite stays green.
/// </para>
/// <para>
/// On dispose, if <em>this fixture</em> started the compose stack it runs
/// <c>{cli} compose down</c> to leave the machine clean.
/// </para>
/// </summary>
public sealed class BrunoGraphDbFixture : IAsyncLifetime
{
    private static readonly string ComposeFile =
        Path.Combine(AppContext.BaseDirectory, "docker-compose.graphdb.yml");

    private static readonly string ContainerCli = FindContainerCli();

    public string BaseUrl { get; } = Environment.GetEnvironmentVariable("FORGE_GRAPHDB_URL")
                                          ?? "http://localhost:7201";

    /// <summary>
    /// Separate repository from "forge-tests" used by Repository.GraphDb.Tests so both
    /// suites can run in parallel on the same GraphDB instance without interfering.
    /// </summary>
    public string RepositoryId { get; } = "forge-sample-tests";

    public bool Available { get; private set; }

    private bool _weStartedCompose;
    private bool _usedDirectRun;

    public HttpClient Http { get; } = new HttpClient();

    public async Task InitializeAsync()
    {
        if (!await PingAsync())
        {
            if (!await TryComposeUpAsync())
                return;

            if (!await WaitForReadyAsync(timeoutSeconds: 90))
                return;

            _weStartedCompose = true;
        }

        using var probe = await Http.GetAsync($"{BaseUrl}/rest/repositories/{RepositoryId}");
        if (probe.StatusCode == HttpStatusCode.NotFound)
            await CreateRepositoryAsync();

        Available = true;
    }

    public async Task ClearAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{BaseUrl}/repositories/{RepositoryId}/statements");
            req.Content = new StringContent("CLEAR ALL", Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sparql-update");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            // The container was torn down between fixture init and this test
            // (e.g. Repository.GraphDb.Tests ran compose-down in parallel).
            // Signal unavailability so all remaining tests skip instead of fail.
            Available = false;
        }
    }

    private async Task CreateRepositoryAsync()
    {
        var config = $@"
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix rep:  <http://www.openrdf.org/config/repository#> .
@prefix sr:   <http://www.openrdf.org/config/repository/sail#> .
@prefix sail: <http://www.openrdf.org/config/sail#> .
@prefix graphdb: <http://www.ontotext.com/config/graphdb#> .

[] a rep:Repository ;
   rep:repositoryID ""{RepositoryId}"" ;
   rdfs:label ""Forge sample integration test repo"" ;
   rep:repositoryImpl [
       rep:repositoryType ""graphdb:SailRepository"" ;
       sr:sailImpl [
           sail:sailType ""graphdb:Sail"" ;
           graphdb:base-URL ""http://example.org/owlim#"" ;
           graphdb:defaultNS """" ;
           graphdb:entity-index-size ""200000"" ;
           graphdb:entity-id-size ""32"" ;
           graphdb:imports """" ;
           graphdb:repository-type ""file-repository"" ;
           graphdb:ruleset ""empty"" ;
           graphdb:storage-folder ""storage"" ;
           graphdb:enable-context-index ""false"" ;
           graphdb:cache-memory ""80m"" ;
           graphdb:tuple-index-memory ""80m"" ;
           graphdb:enablePredicateList ""true"" ;
           graphdb:predicate-memory ""20m"" ;
           graphdb:fts-memory ""20m"" ;
           graphdb:ftsIndexPolicy ""never"" ;
           graphdb:in-memory-literal-properties ""true"" ;
           graphdb:enable-literal-index ""true"" ;
           graphdb:check-for-inconsistencies ""false"" ;
           graphdb:disable-sameAs ""true"" ;
           graphdb:query-timeout ""0"" ;
           graphdb:query-limit-results ""0"" ;
           graphdb:throw-QueryEvaluationException-on-timeout ""false"" ;
           graphdb:read-only ""false"" ;
           graphdb:nonInterpretablePredicates ""http://www.w3.org/2000/01/rdf-schema#label;http://www.w3.org/1999/02/22-rdf-syntax-ns#type""
       ]
   ] .
";
        using var content = new MultipartFormDataContent();
        var cfgPart = new ByteArrayContent(Encoding.UTF8.GetBytes(config));
        cfgPart.Headers.ContentType = new MediaTypeHeaderValue("text/turtle");
        content.Add(cfgPart, "config", $"{RepositoryId}.ttl");

        using var resp = await Http.PostAsync($"{BaseUrl}/rest/repositories", content);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Failed to create test repository '{RepositoryId}': {(int)resp.StatusCode} {body}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_weStartedCompose)
            await TryComposeDownAsync();

        Http.Dispose();
    }

    // ── Ping / wait helpers ──────────────────────────────────────────────────

    private async Task<bool> PingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var resp = await Http.GetAsync($"{BaseUrl}/rest/repositories", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> WaitForReadyAsync(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await PingAsync()) return true;
            await Task.Delay(2_000);
        }
        return false;
    }

    // ── Container-runtime helpers ────────────────────────────────────────────

    private static string FindContainerCli()
    {
        var envOverride = Environment.GetEnvironmentVariable("FORGE_CONTAINER_CLI");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride.Trim();

        foreach (var candidate in new[] { "podman", "docker" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                probe?.WaitForExit();
                if (probe?.ExitCode == 0) return candidate;
            }
            catch { /* not installed */ }
        }

        return "docker";
    }

    private async Task<bool> TryComposeUpAsync()
    {
        if (File.Exists(ComposeFile))
        {
            int composeExit = await RunAsync(ContainerCli, $"compose -f \"{ComposeFile}\" up -d");
            if (composeExit == 0) return true;
        }

        // Fallback: start the container directly when compose is unavailable.
        // Use the correct container name and port so this container is distinct from
        // the one used by Repository.GraphDb.Tests (forge-graphdb on 7200).
        int runExit = await RunAsync(ContainerCli,
            "run -d --name forge-graphdb-sample -p 7201:7200 -e GDB_HEAP_SIZE=1g ontotext/graphdb:10.7.3");

        if (runExit != 0) return false;

        _usedDirectRun = true;
        return true;
    }

    private async Task TryComposeDownAsync()
    {
        if (_usedDirectRun)
        {
            await RunAsync(ContainerCli, "stop forge-graphdb-sample");
            await RunAsync(ContainerCli, "rm forge-graphdb-sample");
            return;
        }

        if (!File.Exists(ComposeFile)) return;
        await RunAsync(ContainerCli, $"compose -f \"{ComposeFile}\" down");
    }

    private static async Task<int> RunAsync(string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            proc.Start();
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }
        catch { return -1; }
    }
}

[CollectionDefinition("BrunoGraphDb")]
public sealed class BrunoGraphDbCollection : ICollectionFixture<BrunoGraphDbFixture> { }
