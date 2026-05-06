using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Shouldly;

namespace Forge.Application.Sample.Tests;

/// <summary>
/// Integration tests that start the merged Application.Sample app as a subprocess and
/// drive it via the committed Bruno collections. See ADR-0012 and ADR-0013.
/// </summary>
public sealed class BrunoIntegrationTests : IAsyncLifetime
{
    private Process? _appProcess;
    private string _baseUrl = "";

    // ─── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var port = FindFreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _appProcess = StartSampleApp(port);
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

    /// <summary>
    /// Chapter 1 — Greet: verifies the hand-written capability handler responds correctly
    /// and that an aspect-IRI header is accepted without error (permissive guard).
    /// See ADR-0013.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_01_greeting_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "01-greeting");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 2 — Books: verifies the generated CRUD handlers (create/read/update/list/delete)
    /// for the Book entity. See ADR-0013.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_02_books_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "02-books");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 3 — DataRecords: verifies the generated CRUD handlers for the DataRecord
    /// entity, which exercises every supported scalar CLR type. See ADR-0013.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_03_data_records_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "03-data-records");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 4 — Catalog: verifies the hand-written POST/PUT/PATCH capability handlers
    /// for catalog item management. See ADR-0013.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_04_catalog_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "04-catalog");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 5 — Error demo: verifies that an intentionally-failing capability handler
    /// returns HTTP 422 with a structured <c>CapabilityError</c> payload.
    /// See root ADR-0014 and Capability ADR-0005.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_05_error_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "05-error-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 6 — Aspect demo: verifies the capability aspect pipeline.
    /// Exercises permissive dispatch (no header) and policy-bound dispatch (with a
    /// registered capability-aspect IRI). The handler reflects the active aspect IRI
    /// back in the response. See sample ADR-0003.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_06_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "06-aspect-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 7 — Entity aspect demo: verifies <see cref="IOperationAspect"/> validation
    /// on generated CUD handlers via <c>EntityTransaction</c>.
    /// Exercises Local SHACL pass (publishedYear &lt; 1800 → 422), Context SPARQL pass
    /// (delete checked-out book → 422), and permissive bypass (no aspect header → 200
    /// regardless of year). See sample ADR-0004, Aspects ADR-0010, Capability ADR-0015.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_07_entity_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "07-entity-aspect-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 8 — Update aspect with combined conditions: verifies that a single
    /// <see cref="IOperationAspect"/> can enforce both passes simultaneously.
    /// The <c>book-update-strict-v1</c> aspect applies a Local SHACL pass
    /// (publishedYear ≥ 1800) <em>and</em> a Context WHERE pass (reject if
    /// <c>available = false</c>). Exercises:
    /// <list type="bullet">
    ///   <item>Both passes clear → 200</item>
    ///   <item>SHACL violation (year &lt; 1800) → 422 <c>ENTITY_SHACL_VIOLATION</c></item>
    ///   <item>WHERE violation (checked-out book) → 422 <c>ENTITY_SHACL_VIOLATION</c></item>
    ///   <item>Permissive bypass (no aspect header) → 200 regardless of data</item>
    /// </list>
    /// See sample ADR-0004, Aspects ADR-0010, Capability ADR-0015.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_08_update_aspect_combined_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "08-update-aspect-combined");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static Process StartSampleApp(int port)
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

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the sample app process.");

        // Drain stdout/stderr continuously so the pipe buffer never fills and
        // blocks the child process while the probe loop runs (pipe-deadlock prevention).
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task WaitForReadyAsync(string baseUrl, int timeoutSeconds = 30)
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

    /// <summary>
    /// Invokes <c>npx @usebruno/cli run &lt;requestDir&gt; --env local --env-var baseUrl=&lt;url&gt;</c>
    /// with the collection root as the working directory (so Bruno can find environments/local.bru).
    /// Returns the combined stdout+stderr output and the exit code.
    /// </summary>
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

        // Read both streams concurrently to prevent buffer-full deadlock.
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

        // Detect Debug vs Release from the current assembly's output path.
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

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until a directory containing
    /// <c>Forge.Platform.slnx</c> is found, which anchors the repo root.
    /// </summary>
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
