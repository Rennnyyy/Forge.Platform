using System.Collections.Concurrent;
using Forge.ObjectStorage;

namespace Forge.ObjectStorage.InMemory;

/// <summary>
/// In-process object store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Intended for unit tests and sample applications — no external process required.
/// </summary>
/// <remarks>
/// Thread-safe by <see cref="ConcurrentDictionary{TKey,TValue}"/> semantics.
/// See <c>Forge.ObjectStorage.Abstractions</c> ADR-0001 for the contract specification.
/// </remarks>
public sealed class InMemoryObjectStore : IObjectStore
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType)> _store = new();

    /// <inheritdoc/>
    public async ValueTask UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentType);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        _store[objectKey] = (buffer.ToArray(), contentType);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectNotFoundException">
    /// Thrown when <paramref name="objectKey"/> does not exist in this store.
    /// </exception>
    public ValueTask<Stream> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectKey);

        if (!_store.TryGetValue(objectKey, out var entry))
            throw new ObjectNotFoundException(objectKey);

        return ValueTask.FromResult<Stream>(new MemoryStream(entry.Data));
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectKey);
        _store.TryRemove(objectKey, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectKey);
        return ValueTask.FromResult(_store.ContainsKey(objectKey));
    }

    /// <summary>
    /// Returns the content-type stored for <paramref name="objectKey"/>,
    /// or <see langword="null"/> when the key does not exist.
    /// </summary>
    /// <remarks>Used by tests to verify round-trip content-type storage.</remarks>
    internal string? GetContentType(string objectKey) =>
        _store.TryGetValue(objectKey, out var entry) ? entry.ContentType : null;
}
