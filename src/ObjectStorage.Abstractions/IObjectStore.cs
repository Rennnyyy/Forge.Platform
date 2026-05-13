namespace Forge.ObjectStorage;

/// <summary>
/// Provider-agnostic binary object store.
/// Objects are identified by opaque, globally unique string keys.
/// Branch isolation is the caller's responsibility via key choice — the store itself
/// is branch-unaware.
/// </summary>
/// <remarks>
/// See <c>Forge.ObjectStorage.Abstractions</c> ADR-0001 for the full interface contract,
/// the convention-key staging protocol, and the InMemory implementation specification.
/// </remarks>
public interface IObjectStore
{
    /// <summary>
    /// Write <paramref name="content"/> under <paramref name="objectKey"/>.
    /// Overwrites any existing object stored at that key.
    /// The caller is responsible for rewinding the stream before calling if it has
    /// been read previously.
    /// </summary>
    ValueTask UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a readable stream over the stored content.
    /// The caller owns the returned stream and must dispose it.
    /// </summary>
    /// <exception cref="ObjectNotFoundException">
    /// Thrown when no object exists for <paramref name="objectKey"/>.
    /// </exception>
    ValueTask<Stream> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the object identified by <paramref name="objectKey"/>.
    /// No-op when the key does not exist.
    /// </summary>
    ValueTask DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when an object exists for <paramref name="objectKey"/>.
    /// </summary>
    ValueTask<bool> ExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
