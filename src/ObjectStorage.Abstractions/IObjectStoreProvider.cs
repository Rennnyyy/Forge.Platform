namespace Forge.ObjectStorage;

/// <summary>
/// Resolves a named <see cref="IObjectStore"/> by DI key string.
/// Allows applications to register multiple stores (e.g. one per media type or domain area).
/// </summary>
/// <remarks>
/// The <paramref name="storeKey"/> passed to <see cref="GetStore"/> corresponds to the
/// <c>StoreKey</c> property of <c>[ObjectBearing(storeKey)]</c> on the entity class.
/// See <c>Forge.ObjectStorage.Abstractions</c> ADR-0001.
/// </remarks>
public interface IObjectStoreProvider
{
    /// <summary>
    /// Returns the <see cref="IObjectStore"/> registered under <paramref name="storeKey"/>.
    /// Implementations may create stores lazily on first access.
    /// </summary>
    IObjectStore GetStore(string storeKey);
}
