namespace Forge.Entity;

/// <summary>
/// Mutable <see cref="IEntityOptions"/> implementation suitable for DI registration,
/// <c>Microsoft.Extensions.Options</c> binding, or direct construction in tests.
/// </summary>
/// <example>
/// DI registration with appsettings:
/// <code>
/// services.Configure&lt;EntityOptionsInstance&gt;(config.GetSection("Entity"));
/// services.AddSingleton&lt;IEntityOptions&gt;(
///     sp =&gt; sp.GetRequiredService&lt;IOptions&lt;EntityOptionsInstance&gt;&gt;().Value);
/// </code>
/// Per-request ambient scope in middleware:
/// <code>
/// var opts = context.RequestServices.GetRequiredService&lt;IEntityOptions&gt;();
/// using var _ = EntityOptions.Use(opts);
/// await next(context);
/// </code>
/// </example>
public sealed class EntityOptionsInstance : IEntityOptions
{
    private string _baseIri = "https://forge.local";
    private string? _predicateBaseIri;

    /// <inheritdoc cref="IEntityOptions.BaseIri"/>
    public string BaseIri
    {
        get => _baseIri;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("BaseIri must not be empty.", nameof(value));
            _baseIri = value.TrimEnd('/');
        }
    }

    /// <inheritdoc cref="IEntityOptions.PredicateBaseIri"/>
    public string PredicateBaseIri
    {
        get => _predicateBaseIri ?? $"{_baseIri}/predicates";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("PredicateBaseIri must not be empty.", nameof(value));
            _predicateBaseIri = value.TrimEnd('/');
        }
    }
}
