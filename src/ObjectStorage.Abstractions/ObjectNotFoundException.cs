namespace Forge.ObjectStorage;

/// <summary>
/// Thrown by <see cref="IObjectStore.DownloadAsync"/> when no object exists for the
/// requested key.
/// </summary>
public sealed class ObjectNotFoundException : Exception
{
    /// <summary>The key that was not found in the store.</summary>
    public string ObjectKey { get; }

    /// <summary>
    /// Initializes a new <see cref="ObjectNotFoundException"/> for <paramref name="objectKey"/>.
    /// </summary>
    public ObjectNotFoundException(string objectKey)
        : base($"Object with key '{objectKey}' was not found in the store.")
    {
        ObjectKey = objectKey;
    }
}
