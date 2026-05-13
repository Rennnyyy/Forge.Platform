using System.Collections.Concurrent;
using Forge.ObjectStorage;

namespace Forge.ObjectStorage.InMemory;

/// <summary>
/// In-process <see cref="IObjectStoreProvider"/> that lazily creates and caches one
/// <see cref="InMemoryObjectStore"/> per named store key.
/// </summary>
/// <remarks>
/// Intended for unit tests and sample applications.
/// Call <see cref="Reset"/> between test runs to discard all stored objects across
/// every named store managed by this provider.
/// </remarks>
public sealed class InMemoryObjectStoreProvider : IObjectStoreProvider
{
    private readonly ConcurrentDictionary<string, InMemoryObjectStore> _stores = new();

    /// <inheritdoc/>
    public IObjectStore GetStore(string storeKey)
    {
        ArgumentNullException.ThrowIfNull(storeKey);
        return _stores.GetOrAdd(storeKey, _ => new InMemoryObjectStore());
    }

    /// <summary>
    /// Discards all named stores managed by this provider.
    /// Any previously resolved <see cref="InMemoryObjectStore"/> instances remain
    /// valid but are no longer returned by subsequent <see cref="GetStore"/> calls
    /// for the same key — a new empty store is created on the next call.
    /// </summary>
    public void Reset() => _stores.Clear();
}
