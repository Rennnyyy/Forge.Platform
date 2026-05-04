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
    /// Runs all .bru files in samples/Application.Sample/bruno/demo/ (greet and catalog
    /// hand-written handlers) via the Bruno CLI. Skips gracefully when npx is absent.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_demo_collection_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var demoDir = Path.Combine(collectionRoot, "demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(demoDir).ShouldBeTrue($"Bruno demo folder not found at '{demoDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, demoDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Runs all .bru files in samples/Application.Sample/bruno/books/ (generated Book
    /// CRUD handlers) via the Bruno CLI. Skips gracefully when npx is absent.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_books_collection_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var booksDir = Path.Combine(collectionRoot, "books");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(booksDir).ShouldBeTrue($"Bruno books folder not found at '{booksDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, booksDir, _baseUrl);

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
                var response = await client.PostAsync($"{baseUrl}/demo/greet", content);
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
