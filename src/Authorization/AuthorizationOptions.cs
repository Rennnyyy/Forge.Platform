namespace Forge.Authorization;

/// <summary>
/// Configuration options for the Authorization slice.
/// Bound from the <c>Forge:Authorization</c> configuration section.
/// </summary>
public sealed class AuthorizationOptions
{
    /// <summary>
    /// The configuration section path used by
    /// <see cref="Http.DependencyInjection.AuthorizationHttpServiceCollectionExtensions.AddForgeAuthorizationHttp"/>.
    /// </summary>
    public const string ConfigurationSection = "Forge:Authorization";

    /// <summary>
    /// When <see langword="true"/> (the default), the application fails to start if the only
    /// registered <see cref="Forge.Aspects.Abstractions.IAspectGuard"/> is
    /// <see cref="Forge.Aspects.Abstractions.AllowAllAspectGuard"/> —
    /// the built-in permit-all stub that enforces no policy.
    /// <para>
    /// Set to <see langword="false"/> in environments where a real guard is intentionally absent
    /// (e.g. <c>appsettings.Development.json</c>).
    /// </para>
    /// </summary>
    /// <example>
    /// Disable in development only:
    /// <code>
    /// // appsettings.json
    /// { "Forge": { "Authorization": { "RequireExplicitGuard": true } } }
    ///
    /// // appsettings.Development.json
    /// { "Forge": { "Authorization": { "RequireExplicitGuard": false } } }
    /// </code>
    /// </example>
    public bool RequireExplicitGuard { get; set; } = true;
}
