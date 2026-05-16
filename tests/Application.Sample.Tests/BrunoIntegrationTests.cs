using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Shouldly;

namespace Forge.Application.Sample.Tests;

/// <summary>
/// Integration tests that start the merged Application.Sample app as a subprocess and
/// drive it via the committed Bruno collections. See ADR-0012 and ADR-0013.
/// </summary>
[Collection("BrunoGraphDb")]
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
        var chapterDir = Path.Combine(collectionRoot, "06-capability-aspect-demo");

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

    /// <summary>
    /// Chapter 9 — Artists: verifies the generated CRUD handlers for the <c>Artist</c>
    /// entity, which uses <see cref="IdentityGenerator.Random"/> (UUID-based IRI).
    /// See ADR-0013, sample ADR-0005.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_09_artists_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "09-artists");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 10 — Genres: verifies the read-only <c>GET api/entities/genres</c> endpoints
    /// for the <c>[Enumeration]</c> <c>Genre</c> type. Because <c>[Enumeration]</c> entities
    /// carry pre-sealed static named individuals, <c>MapOperations()</c> registers only List
    /// and Read (no Create, Update, or Delete). See sample ADR-0006.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_10_genres_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "10-genres");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 11 — Recordings: verifies scalar CRUD for the <c>Recording</c> child entity,
    /// which is the 1:N target of <c>Studio.Recordings</c>. See sample ADR-0005.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_11_recordings_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "11-recordings");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 12 — Studios: verifies scalar CRUD for the complex <c>Studio</c> aggregate,
    /// which carries all 22 supported scalar CLR types (11 non-nullable + 11 nullable) and
    /// all three owned-relation flavours (N:1, 1:N, M:N) at the entity model level.
    /// The HTTP layer surfaces scalars only; see sample ADR-0005 and Operations.Http ADR-0001.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_12_studios_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "12-studios");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// <summary>
    /// Chapter 13 — Featured Artists: verifies the generated CRUD handlers for the
    /// <c>FeaturedArtist : Artist</c> child entity, which demonstrates entity type
    /// inheritance. Includes a polymorphic-listing step that GETs
    /// <c>api/entities/artists</c> and asserts the featured-artist IRI appears in the
    /// results, proving that <c>QueryByTypeAsync&lt;Artist&gt;</c> returns subtypes.
    /// See sample ADR-0008 and root Entity ADR-0016.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_13_featured_artists_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "13-featured-artists");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 14 — Branches: verifies the full branch lifecycle via <c>MapBranches()</c>
    /// endpoints and the branch-scoped entity store:
    /// <list type="bullet">
    ///   <item>POST/GET/PUT/GET(list)/DELETE on <c>api/branches</c></item>
    ///   <item>POST/GET(list)/GET(read) on <c>api/entities/books</c> with
    ///         <c>X-Forge-BranchIri</c> header (branch-scoped write and read)</item>
    ///   <item>GET <c>api/entities/books</c> without header (default-branch isolation check)</item>
    /// </list>
    /// See Branch ADR-0001 and Branch.Http ADR-0001.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_14_branches_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "14-branches");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Runs chapter 15 — Snapshot management: full lifecycle from branch seeding through
    /// snapshot creation, immutability enforcement (CUD rejected with 422), semver lookup,
    /// drop, and source-branch cleanup.
    /// <list type="bullet">
    ///   <item>POST <c>api/branches</c> — create source branch</item>
    ///   <item>POST/GET <c>api/entities/books</c> — seed a book entity into the source branch</item>
    ///   <item>POST <c>api/snapshots</c> — create &amp; seed snapshot at semver 1.0.0</item>
    ///   <item>GET <c>api/entities/books</c> with snapshot IRI header — read frozen data</item>
    ///   <item>GET <c>api/branches</c> — list all mutable branches</item>
    ///   <item>GET <c>api/branches?type=snapshot</c> — list snapshots only</item>
    ///   <item>GET <c>api/branches?semver=1.0.0</c> — lookup snapshot by semver</item>
    ///   <item>POST/PUT/DELETE <c>api/entities/books</c> with snapshot IRI header — rejected 422 SNAPSHOT_IMMUTABLE</item>
    ///   <item>DELETE <c>api/snapshots/v1.0.0</c> — drop snapshot (204)</item>
    ///   <item>GET <c>api/branches?semver=1.0.0</c> — verify gone (404)</item>
    ///   <item>DELETE <c>api/branches</c> — cleanup source branch</item>
    /// </list>
    /// See Branch ADR-0002 and ADR-0003.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_15_snapshots_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "15-snapshots");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 16 — Branch aspect demo: aspects enforced on branch/snapshot CRUD
    /// (SHACL description length, semver format, archived status, delete guard).
    /// See root ADR-0019 and Branch.Http ADR-0001.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_16_branch_aspect_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "16-branch-aspect-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 17 — Entity messaging demo: verifies that entity mutations (Create, Update,
    /// Delete) on the <c>Book</c> entity emit <see cref="Forge.Entity.Messaging.EntityChangedEnvelope{T}"/>
    /// messages through the in-memory broker and that the diagnostic endpoint
    /// <c>GET /api/diagnostics/entity-events</c> captures all three operations in order,
    /// including a <c>latest</c> lookup returning the final <c>Deleted</c> state.
    /// See root ADR-0021 and sample ADR-0010.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_17_entity_messaging_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "17-entity-messaging-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 18 — Async capability messaging demo: verifies both the fire-and-forget path
    /// (<c>POST /api/async-capability/fire</c> → 202 Accepted) and the request-reply path
    /// (<c>POST /api/async-capability/dispatch</c> → 200 with <see cref="Forge.Capability.Messaging.CapabilityReplyEnvelope{T}"/>)
    /// using <see cref="Forge.Capability.Messaging.IAsyncCapabilityDispatcher{TCommand,TResponse}"/>
    /// over the in-memory broker.
    /// See root ADR-0022 and sample ADR-0011.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_18_async_capability_demo_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "18-async-capability-demo");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 19 — Track masters: verifies the full <see cref="Forge.ObjectStorage.Http"/>
    /// lifecycle for a <c>[ObjectBearing]</c> entity (<c>TrackMaster</c>):
    /// <list type="bullet">
    ///   <item>POST <c>api/objects/track-masters</c> — create metadata entity</item>
    ///   <item>GET <c>api/objects/track-masters?iri=…</c> — read metadata</item>
    ///   <item>GET <c>api/objects/track-masters</c> — list entities</item>
    ///   <item>PUT <c>api/entities/track-masters?iri=…</c> — update metadata; aspect
    ///         <c>track-master-write-v1</c> validates title is non-empty</item>
    ///   <item>PUT <c>api/objects/track-masters/content?iri=…</c> — upload WAV via
    ///         multipart form; aspect <c>track-master-write-v1</c> validates write access</item>
    ///   <item>GET <c>api/objects/track-masters/content?iri=…</c> — download blob</item>
    ///   <item>PUT <c>api/objects/track-masters/content?iri=…</c> (re-upload) — blocked by
    ///         lock aspect <c>track-master-lock-v1</c> → 422 <c>ENTITY_SHACL_VIOLATION</c></item>
    ///   <item>Branch-scoped upload/download — entity created and blob uploaded on a named
    ///         branch; default branch remains clean</item>
    ///   <item>DELETE <c>api/objects/track-masters?iri=…</c> — combined entity + blob delete</item>
    /// </list>
    /// See root ADR-0023, ObjectStorage.Http ADR-0001, and sample ADR-0011.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_19_track_masters_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "19-track-masters");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>Chapter 20 — Branch merge.</summary>
    [SkippableFact]
    public async Task Bruno_20_branch_merge_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "20-branch-merge");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 21 — Structure trees: verifies the full structure-tree lifecycle using
    /// the <c>Forge.Structure</c> slice:
    /// <list type="bullet">
    ///   <item>POST <c>api/entities/structure-dimensions</c> × 2 — create EV flag and
    ///         Body Segment enumeration dimensions; IRIs stored as Bruno variables</item>
    ///   <item>POST <c>api/entities/structure-nodes</c> × 10 — create structural nodes
    ///         (Vehicle, EV/ICE Powertrain, Battery, Motor, Engine, Gearbox, Luxury/Standard
    ///         Interior, Race Edition Package)</item>
    ///   <item>POST <c>api/entities/structure-usages</c> × 9 — create Usage edges with
    ///         polymorphic JSON conditions: <see cref="Forge.Structure.FlagOptionCondition"/>
    ///         (ev required), <see cref="Forge.Structure.EnumerationOptionCondition"/>
    ///         (segment optional, open-world), and <see cref="Forge.Structure.TimeCondition"/>
    ///         (2025-only time window)</item>
    ///   <item>POST <c>api/capabilities/structure/configured-tree/get</c> × 5 — assert
    ///         correct sub-tree per EV/ICE + luxury/standard + in-/out-of-time-window
    ///         configurations</item>
    ///   <item>GET/GET/PUT/DELETE <c>api/entities/structure-nodes</c> — list, read, update,
    ///         delete; GET <c>api/entities/structure-dimensions</c> — list dimensions</item>
    /// </list>
    /// See Structure ADR-0005, Structure ADR-0002, and root ADR-0016.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_21_structure_tree_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "21-variant-tree");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 22 — Car build demo: pure-structure 150&amp;#37; tree with two milestone sub-trees.
    /// <list type="bullet">
    ///   <item>POST <c>api/capabilities/car/demo/populate</c> — seeds 44 nodes and 43 unconditional
    ///         edges in a single call; returns landmark IRIs for subsequent queries</item>
    ///   <item>GET configured-tree from Car root (44 nodes), Initial milestone (17 nodes),
    ///         Update-1 milestone (26 nodes) — demonstrates structural variance via topology</item>
    ///   <item>GET sub-tree from Initial EV Package (5 nodes), Update-1 EV Package (7 nodes),
    ///         Update-1 Gearbox (3 nodes) — demonstrates any-node-as-head and milestone evolution</item>
    ///   <item>GET <c>api/entities/structure-nodes</c> — reads the Car root node via CRUD endpoint</item>
    /// </list>
    /// See Sample ADR-0013 and root ADR-0016.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_22_car_build_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "22-car-build");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 23 — Geometry snapshot demo: per-edge branch annotation via <c>Usage.SnapshotIri</c>.
    /// <list type="bullet">
    ///   <item>POST <c>api/capabilities/car/demo/populate</c> — seeds 11 geometry nodes (10 on main
    ///         + 1 v1.0 steel-outline on the geometry-v1 snapshot branch)</item>
    ///   <item>POST <c>structure/configured-tree/get</c> — verifies that the SteelFrame node carries
    ///         <c>snapshotBranchIri</c> equal to the geometry-v1 branch IRI; verifies root is null</item>
    ///   <item>GET <c>api/entities/geometry-nodes</c> with snapshot branch header — verifies exactly
    ///         one node (the v1.0 outline) is visible in the snapshot named graph</item>
    /// </list>
    /// See Sample ADR-0014 and Structure ADR-0006.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_23_geometry_snapshot_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "23-geometry-snapshot");

        Directory.Exists(collectionRoot).ShouldBeTrue($"Bruno collection root not found at '{collectionRoot}'.");
        Directory.Exists(chapterDir).ShouldBeTrue($"Bruno chapter folder not found at '{chapterDir}'.");

        var (exitCode, output) = await RunBrunoAsync(collectionRoot, chapterDir, _baseUrl);

        exitCode.ShouldBe(0,
            $"Bruno exited with code {exitCode} — one or more requests failed.\nOutput:\n{output}");
    }

    /// <summary>
    /// Chapter 24 — Big-car sample: large-scale geometry with mass download.
    /// <list type="bullet">
    ///   <item>POST <c>api/capabilities/car/demo/big/populate</c> — seeds 7 576 structure nodes
    ///         across 7 levels, 4 000 unique <c>Geometry3D</c> nodes (standard-part boxes), and
    ///         10 000 <c>GeometryUsage3D</c> placements (average reuse 2.5×).
    ///         Uses a 120-second per-request timeout because the operation creates ≈ 29 000 entities.</item>
    ///   <item>POST <c>api/capabilities/structure/configured-tree/get</c> — queries the full
    ///         7 576-node tree (all unconditional edges) from the big-car root.</item>
    ///   <item>GET <c>api/objects/geometry3d-nodes/bundle</c> — downloads all 4 000 OBJ blobs as
    ///         a single ZIP archive; asserts <c>application/zip</c> content-type and the
    ///         <c>geometry3d-bundle.zip</c> filename in the <c>Content-Disposition</c> header.</item>
    /// </list>
    /// See Sample ADR-0015, ADR-0016, and root ADR-0027.
    /// </summary>
    [SkippableFact]
    public async Task Bruno_24_big_car_sample_requests_all_pass()
    {
        Skip.If(!IsNpxAvailable(), "npx not found on PATH — install Node.js to enable Bruno integration tests.");

        var repoRoot = FindRepoRoot();
        var collectionRoot = Path.Combine(repoRoot, "samples", "Application.Sample", "bruno");
        var chapterDir = Path.Combine(collectionRoot, "24-big-car-sample");

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
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

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
