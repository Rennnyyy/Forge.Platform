using Forge.ObjectStorage;
using Forge.ObjectStorage.InMemory;
using Shouldly;

namespace Forge.ObjectStorage.InMemory.Tests;

public sealed class InMemoryObjectStoreTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static Stream StreamOf(string text) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAllTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    // ── UploadAsync + DownloadAsync round-trip ────────────────────────────────

    [Fact]
    public async Task UploadAsync_then_DownloadAsync_returns_same_content()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("docs/hello", StreamOf("hello forge"), "text/plain");

        await using var stream = await store.DownloadAsync("docs/hello");
        var text = await ReadAllTextAsync(stream);

        text.ShouldBe("hello forge");
    }

    [Fact]
    public async Task UploadAsync_stores_content_type()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("doc/1", StreamOf("data"), "application/pdf");

        store.GetContentType("doc/1").ShouldBe("application/pdf");
    }

    [Fact]
    public async Task UploadAsync_overwrites_existing_content()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("key", StreamOf("old"), "text/plain");
        await store.UploadAsync("key", StreamOf("new"), "text/html");

        await using var stream = await store.DownloadAsync("key");
        var text = await ReadAllTextAsync(stream);

        text.ShouldBe("new");
        store.GetContentType("key").ShouldBe("text/html");
    }

    [Fact]
    public async Task UploadAsync_does_not_require_seekable_stream()
    {
        var store = new InMemoryObjectStore();
        // NetworkStream-like: non-seekable, non-rewindable
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("non-seekable"));
        var nonSeekable = new NonSeekableStream(ms);

        await store.UploadAsync("ns-key", nonSeekable, "text/plain");

        await using var result = await store.DownloadAsync("ns-key");
        (await ReadAllTextAsync(result)).ShouldBe("non-seekable");
    }

    // ── DownloadAsync exceptions ──────────────────────────────────────────────

    [Fact]
    public async Task DownloadAsync_throws_ObjectNotFoundException_for_unknown_key()
    {
        var store = new InMemoryObjectStore();

        var ex = await Should.ThrowAsync<ObjectNotFoundException>(
            () => store.DownloadAsync("missing").AsTask());

        ex.ObjectKey.ShouldBe("missing");
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_removes_object()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("del-key", StreamOf("bye"), "text/plain");

        await store.DeleteAsync("del-key");

        (await store.ExistsAsync("del-key")).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_is_noop_when_key_not_found()
    {
        var store = new InMemoryObjectStore();

        // Must not throw
        await store.DeleteAsync("ghost-key");
    }

    // ── ExistsAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_returns_true_after_upload()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("e-key", StreamOf("x"), "text/plain");

        (await store.ExistsAsync("e-key")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsAsync_returns_false_for_unknown_key()
    {
        var store = new InMemoryObjectStore();

        (await store.ExistsAsync("unknown")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistsAsync_returns_false_after_delete()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("x", StreamOf("x"), "text/plain");
        await store.DeleteAsync("x");

        (await store.ExistsAsync("x")).ShouldBeFalse();
    }

    // ── Staging convention ────────────────────────────────────────────────────

    [Fact]
    public async Task Staging_key_convention_upload_promote_delete()
    {
        // Simulates the ObjectUploadSaga convention from ObjectStorage.Http ADR-0001:
        // 1. upload to {finalKey}.staging
        // 2. upload to {finalKey}      (promote)
        // 3. delete {finalKey}.staging (cleanup)
        var store = new InMemoryObjectStore();
        const string finalKey = "document/018fabc123";
        const string stagingKey = finalKey + ".staging";

        // step 1 — stage
        await store.UploadAsync(stagingKey, StreamOf("file bytes"), "application/octet-stream");
        (await store.ExistsAsync(stagingKey)).ShouldBeTrue();

        // step 2 — promote to final
        await store.UploadAsync(finalKey, StreamOf("file bytes"), "application/octet-stream");
        (await store.ExistsAsync(finalKey)).ShouldBeTrue();

        // step 3 — delete staging
        await store.DeleteAsync(stagingKey);
        (await store.ExistsAsync(stagingKey)).ShouldBeFalse();

        // final key is intact
        (await store.ExistsAsync(finalKey)).ShouldBeTrue();
    }

    // ── Key isolation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Different_keys_are_independent()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("key-a", StreamOf("A"), "text/plain");
        await store.UploadAsync("key-b", StreamOf("B"), "text/plain");

        await using var streamA = await store.DownloadAsync("key-a");
        await using var streamB = await store.DownloadAsync("key-b");

        (await ReadAllTextAsync(streamA)).ShouldBe("A");
        (await ReadAllTextAsync(streamB)).ShouldBe("B");
    }

    // ── DownloadAsync returns caller-owned stream ─────────────────────────────

    [Fact]
    public async Task DownloadAsync_returns_independent_stream_per_call()
    {
        var store = new InMemoryObjectStore();
        await store.UploadAsync("shared", StreamOf("content"), "text/plain");

        await using var s1 = await store.DownloadAsync("shared");
        await using var s2 = await store.DownloadAsync("shared");

        s1.ShouldNotBeSameAs(s2);

        (await ReadAllTextAsync(s1)).ShouldBe("content");
        (await ReadAllTextAsync(s2)).ShouldBe("content");
    }

    // ── helper type ──────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a <see cref="MemoryStream"/> and exposes it as a non-seekable stream,
    /// simulating network or pipe streams.
    /// </summary>
    private sealed class NonSeekableStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
