namespace Forge.ObjectStorage.Http;

/// <summary>
/// Configuration options for the <c>Forge.ObjectStorage.Http</c> layer.
/// See <c>ObjectStorage.Http</c> ADR-0001.
/// </summary>
public sealed class ObjectStorageHttpOptions
{
    /// <summary>
    /// Maximum upload size in bytes. When the request declares a <c>Content-Length</c>
    /// header that exceeds this value the handler rejects it with
    /// <c>413 Payload Too Large</c> before touching the object store.
    /// Requests without a <c>Content-Length</c> header are passed through and rely on
    /// the server's own request-size limit (e.g. Kestrel <c>MaxRequestBodySize</c>).
    /// Default: 256 MB.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 256L * 1024L * 1024L;
}
