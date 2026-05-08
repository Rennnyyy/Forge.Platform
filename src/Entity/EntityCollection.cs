namespace Forge.Entity;

/// <summary>
/// Default in-memory implementation of <see cref="EntityRefCollection{T}"/>.
/// Generated owning collections wrap an instance of this and inject inverse-side
/// synchronization via callbacks.
/// </summary>
public sealed class EntityRefCollectionImpl<T> : EntityRefCollection<T>
    where T : class, IEntity
{
    private readonly Dictionary<string, T?> _byIri = new(StringComparer.Ordinal);
    private readonly Func<T, ValueTask>? _onAdd;
    private readonly Func<T, ValueTask>? _onRemove;

    public EntityRefCollectionImpl() { }

    /// <summary>
    /// Construct with inverse-sync hooks. The generator wires <paramref name="onAdd"/>/<paramref name="onRemove"/>
    /// to assign or clear the inverse-side reference on the added/removed entity.
    /// </summary>
    public EntityRefCollectionImpl(Func<T, ValueTask>? onAdd, Func<T, ValueTask>? onRemove)
    {
        _onAdd = onAdd;
        _onRemove = onRemove;
    }

    public int LoadedCount
    {
        get
        {
            var n = 0;
            foreach (var v in _byIri.Values) if (v is not null) n++;
            return n;
        }
    }

    public IReadOnlyCollection<string> Iris => _byIri.Keys;

    public bool IsResolved => true;

    public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default) => default;

    public async ValueTask AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_byIri.ContainsKey(entity.Iri)) return;
        _byIri[entity.Iri] = entity;
        if (_onAdd is not null) await _onAdd(entity).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_byIri.Remove(entity.Iri)) return;
        if (_onRemove is not null) await _onRemove(entity).ConfigureAwait(false);
    }

    public ValueTask<bool> ContainsAsync(string iri, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_byIri.ContainsKey(iri));

    public async IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        // Snapshot to allow mutation during iteration.
        var snapshot = new KeyValuePair<string, T?>[_byIri.Count];
        var i = 0;
        foreach (var kv in _byIri) snapshot[i++] = kv;

        foreach (var kv in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kv.Value is not null)
            {
                yield return kv.Value;
            }
            else
            {
                var loader = EntitySession.RequireLoader();
                var loaded = await loader.LoadAsync<T>(kv.Key, cancellationToken).ConfigureAwait(false);
                _byIri[kv.Key] = loaded;
                if (loaded is not null) yield return loaded;
            }
        }
    }
}

/// <summary>
/// Implementation of <see cref="EntityRefCollection{T}"/> that defers loading the member IRI
/// list until first access. On first access it calls
/// <see cref="ICollectionLoader.LoadCollectionIrisAsync{T}"/> via the ambient
/// <see cref="EntitySession"/>. Individual entity objects are still resolved lazily during
/// enumeration, identical to <see cref="EntityRefCollectionImpl{T}"/>.
/// </summary>
public sealed class DeferredEntityRefCollectionImpl<T> : EntityRefCollection<T>
    where T : class, IEntity
{
    private readonly Func<string> _ownerIriSelector;
    private readonly string _predicate;
    private readonly Func<T, ValueTask>? _onAdd;
    private readonly Func<T, ValueTask>? _onRemove;
    private readonly Dictionary<string, T?> _byIri = new(StringComparer.Ordinal);
    private bool _resolved;

    public DeferredEntityRefCollectionImpl(
        Func<string> ownerIriSelector,
        string predicate,
        Func<T, ValueTask>? onAdd = null,
        Func<T, ValueTask>? onRemove = null)
    {
        ArgumentNullException.ThrowIfNull(ownerIriSelector);
        if (string.IsNullOrEmpty(predicate))
            throw new ArgumentException("Predicate is required.", nameof(predicate));
        _ownerIriSelector = ownerIriSelector;
        _predicate = predicate;
        _onAdd = onAdd;
        _onRemove = onRemove;
    }

    public bool IsResolved => _resolved;

    public int LoadedCount
    {
        get
        {
            var n = 0;
            foreach (var v in _byIri.Values) if (v is not null) n++;
            return n;
        }
    }

    public IReadOnlyCollection<string> Iris => _byIri.Keys;

    public async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved) return;
        var session = EntitySession.Current;
        if (session is null)
        {
            // No active session: new-entity scenario — treat the collection as empty.
            _resolved = true;
            return;
        }
        if (session.Loader is not ICollectionLoader collLoader)
            throw new InvalidOperationException(
                $"A deferred EntityRefCollection<{typeof(T).Name}> requires the active IEntityLoader to also " +
                $"implement {nameof(ICollectionLoader)}. Current loader: {session.Loader.GetType().Name}.");
        await foreach (var iri in collLoader
            .LoadCollectionIrisAsync<T>(_ownerIriSelector(), _predicate, cancellationToken)
            .ConfigureAwait(false))
        {
            _byIri.TryAdd(iri, null);
        }
        _resolved = true;
    }

    public async ValueTask AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (_byIri.ContainsKey(entity.Iri)) return;
        _byIri[entity.Iri] = entity;
        if (_onAdd is not null) await _onAdd(entity).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (!_byIri.Remove(entity.Iri)) return;
        if (_onRemove is not null) await _onRemove(entity).ConfigureAwait(false);
    }

    public async ValueTask<bool> ContainsAsync(string iri, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _byIri.ContainsKey(iri);
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // Snapshot to allow mutation during iteration.
        var snapshot = new KeyValuePair<string, T?>[_byIri.Count];
        var i = 0;
        foreach (var kv in _byIri) snapshot[i++] = kv;

        foreach (var kv in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kv.Value is not null)
            {
                yield return kv.Value;
            }
            else
            {
                var loader = EntitySession.RequireLoader();
                var loaded = await loader.LoadAsync<T>(kv.Key, cancellationToken).ConfigureAwait(false);
                _byIri[kv.Key] = loaded;
                if (loaded is not null) yield return loaded;
            }
        }
    }
}

/// <summary>
/// Lazy inverse collection emitted by the generator for <c>[Inverse(Lazy = true)]</c>
/// collection properties. Behaves exactly like <see cref="EntityRefCollectionImpl{T}"/>
/// but starts with <see cref="IsResolved"/> = <see langword="false"/> so that
/// the HTTP serializer omits the key from responses where population was skipped
/// (e.g. list responses for enumeration entities). Once any IRI is added (e.g. by
/// the mapper's <c>HydrateAsync</c> step) or once <see cref="EnsureLoadedAsync"/> is
/// called, <see cref="IsResolved"/> flips to <see langword="true"/> and the key appears
/// in the response normally.
/// </summary>
public sealed class LazyInverseEntityRefCollectionImpl<T> : EntityRefCollection<T>
    where T : class, IEntity
{
    private readonly Dictionary<string, T?> _byIri = new(StringComparer.Ordinal);
    private bool _resolved;

    /// <summary>
    /// <see langword="false"/> until any IRI is added or <see cref="EnsureLoadedAsync"/> is called,
    /// allowing the JSON serializer to omit the key from list responses where population
    /// was intentionally skipped.
    /// </summary>
    public bool IsResolved => _resolved;

    public int LoadedCount
    {
        get
        {
            var n = 0;
            foreach (var v in _byIri.Values) if (v is not null) n++;
            return n;
        }
    }

    public IReadOnlyCollection<string> Iris => _byIri.Keys;

    public ValueTask EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        _resolved = true;
        return default;
    }

    public ValueTask AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _byIri[entity.Iri] = entity;
        _resolved = true;
        return default;
    }

    public ValueTask RemoveAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _byIri.Remove(entity.Iri);
        return default;
    }

    public ValueTask<bool> ContainsAsync(string iri, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_byIri.ContainsKey(iri));

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var snapshot = new KeyValuePair<string, T?>[_byIri.Count];
        var i = 0;
        foreach (var kv in _byIri) snapshot[i++] = kv;
        foreach (var kv in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kv.Value is not null)
            {
                yield return kv.Value;
            }
            else
            {
                var loader = EntitySession.RequireLoader();
                var loaded = await loader.LoadAsync<T>(kv.Key, cancellationToken).ConfigureAwait(false);
                _byIri[kv.Key] = loaded;
                if (loaded is not null) yield return loaded;
            }
        }
    }
}
