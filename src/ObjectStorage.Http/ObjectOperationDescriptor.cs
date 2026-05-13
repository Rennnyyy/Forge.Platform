namespace Forge.ObjectStorage.Http;

/// <summary>
/// Internal descriptor carrying the entity type, route path, and store key for a single
/// <c>[ObjectBearing]</c>-annotated entity.
/// Registered as a singleton by <c>AddForgeObjectStorageHttp()</c> and consumed by
/// <c>MapObjectOperations()</c> at application start-up.
/// </summary>
internal sealed class ObjectOperationDescriptor
{
    /// <summary>The entity CLR type (carries <c>[Entity]</c>, <c>[Identity]</c>, <c>[ObjectBearing]</c>).</summary>
    public Type EntityType { get; }

    /// <summary>The route path segment, e.g. <c>"documents"</c>.</summary>
    public string Path { get; }

    /// <summary>The DI key of the <c>IObjectStore</c> to use for this entity's blob.</summary>
    public string StoreKey { get; }

    public ObjectOperationDescriptor(Type entityType, string path, string storeKey)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        EntityType = entityType;
        Path = path;
        StoreKey = storeKey;
    }
}
