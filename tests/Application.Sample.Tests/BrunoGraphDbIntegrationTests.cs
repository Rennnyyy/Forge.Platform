using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Shouldly;

namespace Forge.Application.Sample.Tests;

/// <summary>
/// Mirrors <see cref="BrunoIntegrationTests"/> but runs the sample app backed by a live
/// Ontotext GraphDB instance managed by <see cref="BrunoGraphDbFixture"/>. See ADR-0018.
/// <para>
/// Each test skips when <c>npx</c> is absent (<c>Node.js</c> not installed) or when
/// <see cref="BrunoGraphDbFixture.Available"/> is <c>false</c> (no container runtime
/// present). The InMemory suite in <see cref="BrunoIntegrationTests"/> is unaffected.
/// </para>
/// </summary>
[Collection("BrunoGraphDb")]
[Trait("Category", "Integration")]
[Trait("Backend", "GraphDB")]
public sealed class BrunoGraphDbIntegrationTests : IAsyncLifetime
{
    private readonly BrunoGraphDbFixture _graphDb;
    private Process? _appProcess;
    private string _baseUrl = "";
    private readonly System.Text.StringBuilder _appLog = new();

    public BrunoGraphDbIntegrationTests(BrunoGraphDbFixture graphDb) => _graphDb = graphDb;

    // ─── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (!_graphDb.Available) return;

        // Wipe any triples left over from a previous test so each chapter starts clean.
        await _graphDb.ClearAsync();

        // ClearAsync may have flipped Available=false if the container went away
        // (parallel suite teardown). Guard here so tests skip cleanly.
        if (!_graphDb.Available) return;

        var port = FindFreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _appProcess = StartSampleApp(port, _graphDb.BaseUrl, _graphDb.RepositoryId, line => _appLog.AppendLine(line));
        await WaitForReadyAsync(_baseUrl);
    }

    public Task DisposeAsync()
    {
        if (_appProcess is { HasExited: false })
        {
            _appProcess.CancelOutputRead();
            _appProcess.CancelErrorRead();
            _appProcess.Kill(entireProcessTree: true);
            _appProcess.WaitForExit(5_000);
        }

        _appProcess?.Dispose();
        return Task.CompletedTask;
    }

    // ─── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>Chapter 01 — Greeting (see <see cref="BrunoIntegrationTests.Bruno_01_greeting_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_01_greeting_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("01-greeting");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 02 — Books (see <see cref="BrunoIntegrationTests.Bruno_02_books_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_02_books_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("02-books");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 03 — DataRecords (see <see cref="BrunoIntegrationTests.Bruno_03_data_records_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_03_data_records_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("03-data-records");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 04 — Catalog (see <see cref="BrunoIntegrationTests.Bruno_04_catalog_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_04_catalog_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("04-catalog");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 05 — Error demo (see <see cref="BrunoIntegrationTests.Bruno_05_error_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_05_error_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("05-error-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 06 — Capability aspect demo (see <see cref="BrunoIntegrationTests.Bruno_06_aspect_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_06_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("06-capability-aspect-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 07 — Entity aspect demo (see <see cref="BrunoIntegrationTests.Bruno_07_entity_aspect_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_07_entity_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("07-entity-aspect-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 08 — Update aspect combined (see <see cref="BrunoIntegrationTests.Bruno_08_update_aspect_combined_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_08_update_aspect_combined_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("08-update-aspect-combined");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 09 — Artists (see <see cref="BrunoIntegrationTests.Bruno_09_artists_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_09_artists_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("09-artists");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 10 — Genres (see <see cref="BrunoIntegrationTests.Bruno_10_genres_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_10_genres_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("10-genres");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 11 — Recordings (see <see cref="BrunoIntegrationTests.Bruno_11_recordings_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_11_recordings_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("11-recordings");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 12 — Studios (see <see cref="BrunoIntegrationTests.Bruno_12_studios_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_12_studios_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("12-studios");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 13 — Featured artists (see <see cref="BrunoIntegrationTests.Bruno_13_featured_artists_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_13_featured_artists_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("13-featured-artists");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 14 — Branches (see <see cref="BrunoIntegrationTests.Bruno_14_branches_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_14_branches_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("14-branches");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 15 — Snapshots (see <see cref="BrunoIntegrationTests.Bruno_15_snapshots_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_15_snapshots_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("15-snapshots");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 16 — Branch aspect demo (see <see cref="BrunoIntegrationTests.Bruno_16_branch_aspect_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_16_branch_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("16-branch-aspect-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 17 — Entity messaging demo (see <see cref="BrunoIntegrationTests.Bruno_17_entity_messaging_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_17_entity_messaging_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("17-entity-messaging-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 18 — Async capability demo (see <see cref="BrunoIntegrationTests.Bruno_18_async_capability_demo_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_18_async_capability_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("18-async-capability-demo");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 19 — Track masters (see <see cref="BrunoIntegrationTests.Bruno_19_track_masters_requests_all_pass"/>).</summary>
    [SkippableFact]
    public async Task Bruno_19_track_masters_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("19-track-masters");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 20 — Branch merge.</summary>
    [SkippableFact]
    public async Task Bruno_20_branch_merge_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("20-branch-merge");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 21 — Structure trees (GraphDB backend).
    /// Mirrors <c>BrunoIntegrationTests.Bruno_21_structure_tree_requests_all_pass</c>.
    /// Note: <see cref="Forge.Structure.Usage.Conditions"/> is not annotated with
    /// <c>[Predicate]</c> and is therefore not persisted by the RDF backend — condition
    /// evaluation relies on in-memory object state. This chapter exercises Node/Usage/
    /// Dimension CRUD via the RDF backend; StructureFilteringStore filtering is validated
    /// by the InMemory chapter. Skipped if GraphDB is unavailable.
    /// See Structure ADR-0002 and Structure ADR-0005.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_21_structure_tree_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");
        Skip.If(!_graphDb.Available, "GraphDB not available — no container runtime present.");

        var (collectionRoot, chapterDir) = ResolvePaths("21-variant-tree");
        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}\nApp Log:\n{_appLog}");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static (string CollectionRoot, string ChapterDir) ResolvePaths(string chapter)
    {
        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, chapter);

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        return (collectionRoot, chapterDir);
    }

    private static Process StartSampleApp(int port, string graphDbBaseUrl, string repositoryId, Action<string>? log = null)
    {
        var dll = FindSampleDll();

        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(dll);
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["Forge__EntityRepository__Backend"] = "GraphDb";
        psi.Environment["Forge__GraphDb__BaseUrl"] = graphDbBaseUrl;
        psi.Environment["Forge__GraphDb__RepositoryId"] = repositoryId;

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the sample app process.");

        process.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Invoke(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task WaitForReadyAsync(string baseUrl, int timeoutSeconds = 60)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var content = new StringContent(
                    "{\"name\":\"_probe\"}",
                    System.Text.Encoding.UTF8,
                    "application/json");
                var response = await client.PostAsync($"{baseUrl}/api/capabilities/demo/greet", content);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Sample app at '{baseUrl}' did not become ready within {timeoutSeconds} s.");
    }

    private static async Task<(int ExitCode, string Output)> RunBrunoAsync(
        string collectionRoot,
        string requestDir,
        string baseUrl)
    {
        var psi = new ProcessStartInfo("npx")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = collectionRoot,
        };
        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("@usebruno/cli");
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(requestDir);
        psi.ArgumentList.Add("--env");
        psi.ArgumentList.Add("local");
        psi.ArgumentList.Add("--env-var");
        psi.ArgumentList.Add($"baseUrl={baseUrl}");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start npx process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdoutTask + await stderrTask;

        return (process.ExitCode, output);
    }

    private static bool IsNpxAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("npx")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            p?.WaitForExit(5_000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindSampleDll()
    {
        var repoRoot = FindRepoRoot();

        var config = AppContext.BaseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        var dll = Path.Combine(
            repoRoot,
            "samples", "Application.Sample",
            "bin", config, "net10.0",
            "Forge.Application.Sample.dll");

        if (File.Exists(dll))
            return dll;

        throw new FileNotFoundException(
            $"Sample DLL not found at '{dll}'. Run 'dotnet build' first.", dll);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Forge.Platform.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repo root (Forge.Platform.slnx not found walking up from " +
            $"'{AppContext.BaseDirectory}').");
    }
}
