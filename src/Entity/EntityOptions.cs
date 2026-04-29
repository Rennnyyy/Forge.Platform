namespace Forge.Entity;

/// <summary>
/// Configures how IRIs are constructed for entities in the current process.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Test / startup usage</strong> — set the static properties before any entity is
/// materialized. This is the simplest approach and requires no DI setup:
/// <code>EntityOptions.BaseIri = "https://forge.example";</code>
/// </para>
/// <para>
/// <strong>DI / per-request usage</strong> — register an <see cref="IEntityOptions"/>
/// implementation and activate it for the current async scope:
/// <code>using var _ = EntityOptions.Use(injectedOptions);</code>
/// See <see cref="EntityOptionsInstance"/> for a ready-made concrete class.
/// </para>
/// </remarks>
public static class EntityOptions
{
    private static readonly AsyncLocal<IEntityOptions?> _ambient = new();
    private static string _baseIri = "https://forge.local";
    private static string? _predicateBaseIri;

    // Read-only view that proxies the static backing fields.
    // Allocated once; reads live values on each property access.
    private static readonly IEntityOptions _staticView = new StaticEntityOptions();

    /// <summary>
    /// The active <see cref="IEntityOptions"/> for the current async control flow.
    /// Returns the ambient override opened with <see cref="Use"/> if one is active,
    /// otherwise returns a live view of the global static fields.
    /// </summary>
    public static IEntityOptions Current => _ambient.Value ?? _staticView;

    /// <summary>
    /// Opens an ambient scope that overrides options for the current async control flow.
    /// Dispose to restore the previous scope.
    /// </summary>
    /// <param name="options">The options to activate for this scope.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> that, on disposal, restores the options that were
    /// active before this call.
    /// </returns>
    /// <example>
    /// <code>
    /// using var _ = EntityOptions.Use(new EntityOptionsInstance { BaseIri = "https://tenant.example" });
    /// var iri = new MyEntity { Slug = "foo" }.Iri; // uses tenant-specific base
    /// </code>
    /// </example>
    public static IDisposable Use(IEntityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = _ambient.Value;
        _ambient.Value = options;
        return new OptionsScope(previous);
    }

    /// <summary>
    /// Global base IRI prepended to every entity's path-prefix and identity suffix.
    /// Trailing slashes are normalized away.
    /// Mutates the global default; prefer <see cref="Use"/> for per-scope overrides.
    /// </summary>
    public static string BaseIri
    {
        get => _baseIri;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("BaseIri must not be empty.", nameof(value));
            _baseIri = value.TrimEnd('/');
        }
    }

    /// <summary>
    /// Global base IRI used to resolve short predicate names declared on
    /// <c>[Owning]</c> / <c>[Inverse]</c>. Defaults to <c>{BaseIri}/predicates</c>.
    /// Setting an absolute IRI on a relation bypasses this.
    /// Mutates the global default; prefer <see cref="Use"/> for per-scope overrides.
    /// </summary>
    public static string PredicateBaseIri
    {
        get => _predicateBaseIri ?? $"{_baseIri}/predicates";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("PredicateBaseIri must not be empty.", nameof(value));
            _predicateBaseIri = value.TrimEnd('/');
        }
    }

    // Proxies the static backing fields as an IEntityOptions without allocating per-call.
    private sealed class StaticEntityOptions : IEntityOptions
    {
        public string BaseIri => EntityOptions._baseIri;
        public string PredicateBaseIri => EntityOptions._predicateBaseIri ?? $"{EntityOptions._baseIri}/predicates";
    }

    private sealed class OptionsScope(IEntityOptions? previous) : IDisposable
    {
        public void Dispose() => _ambient.Value = previous;
    }
}
