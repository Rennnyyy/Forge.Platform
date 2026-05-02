using Forge.Entity.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Forge.Entity.Aspects.DependencyInjection;

/// <summary>
/// DI extensions for the Aspects slice. See Aspects ADR-0003 and ADR-0004.
/// </summary>
public static class AspectsServiceCollectionExtensions
{
    /// <summary>
    /// Registers shape infrastructure, the Aspects engine, and exposes a decorated
    /// <see cref="ITransactionalEntityStore"/> singleton that enforces aspect validation.
    /// </summary>
    /// <remarks>
    /// Must be called after the backend (e.g. <c>UseInMemory()</c>) has been registered.
    /// The registered <see cref="IEntityStore"/> must implement <see cref="ISparqlQueryStore"/>;
    /// otherwise the singleton factory throws <see cref="InvalidOperationException"/> on
    /// first resolve (fail-fast at application startup).
    /// </remarks>
    public static IServiceCollection AddForgeAspects(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Shape infrastructure — singletons; registry is shared instance for IShapeRegistry + IAspectResolver.
        services.TryAddSingleton<IShapeCache, ShapeCache>();

        var registry = new ShapeRegistry();
        services.TryAddSingleton<IShapeRegistry>(registry);
        services.TryAddSingleton<IAspectResolver>(registry);

        // Keep a list of pending aspect registrations so AddCodeAspect() calls that
        // precede or follow AddForgeAspects() can be executed at first resolution.
        services.TryAddSingleton<PendingAspectRegistrations>();

        // Engine
        services.TryAddSingleton<IAspectEngine, AspectEngine>();

        // ITransactionalEntityStore is not registered by the backend DI extensions — they
        // only register IEntityStore. We add it here as a decorated singleton.
        services.TryAddSingleton<ITransactionalEntityStore>(sp =>
        {
            // Run any deferred code-aspect registrations first.
            var pending = sp.GetRequiredService<PendingAspectRegistrations>();
            pending.Execute(sp);

            var raw = sp.GetRequiredService<IEntityStore>();

            if (raw is not ISparqlQueryStore queryStore)
                throw new InvalidOperationException(
                    $"AddForgeAspects() requires the registered IEntityStore to implement " +
                    $"ISparqlQueryStore, but '{raw.GetType().FullName}' does not. " +
                    $"Ensure the backend (e.g. UseInMemory) is registered before AddForgeAspects().");

            if (raw is not ITransactionalEntityStore txStore)
                throw new InvalidOperationException(
                    $"AddForgeAspects() requires the registered IEntityStore to implement " +
                    $"ITransactionalEntityStore, but '{raw.GetType().FullName}' does not.");

            var engine = sp.GetRequiredService<IAspectEngine>();
            return new AspectEnforcingTransactionalStore(txStore, queryStore, engine);
        });

        return services;
    }

    /// <summary>
    /// Registers a code-origin aspect from a Turtle file on disk. The file path is resolved
    /// relative to <see cref="AppContext.BaseDirectory"/> unless rooted.
    /// TTL is parsed eagerly when <see cref="ITransactionalEntityStore"/> is first resolved;
    /// a malformed file throws <see cref="AspectTtlParseException"/> at that point.
    /// </summary>
    public static IServiceCollection AddCodeAspect(
        this IServiceCollection services,
        string ttlPath,
        Type forEntityType,
        AspectKind kind,
        string aspectName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(ttlPath);
        ArgumentNullException.ThrowIfNull(forEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectName);

        services.TryAddSingleton<PendingAspectRegistrations>();
        services.AddSingleton<IPendingAspectAction>(new FileAspectAction(ttlPath, forEntityType, kind, aspectName));
        return services;
    }

    // ------------------------------------------------------------------ Internal plumbing

    /// <summary>Lazy queue of aspect registrations that runs once on first store resolution.</summary>
    internal sealed class PendingAspectRegistrations
    {
        private int _executed;

        public void Execute(IServiceProvider sp)
        {
            if (System.Threading.Interlocked.Exchange(ref _executed, 1) != 0) return;

            var actions = sp.GetServices<IPendingAspectAction>();
            var registry = sp.GetRequiredService<IShapeRegistry>();
            var cache = sp.GetRequiredService<IShapeCache>();

            foreach (var action in actions)
                action.Execute(registry, cache);
        }
    }

    internal interface IPendingAspectAction
    {
        void Execute(IShapeRegistry registry, IShapeCache cache);
    }

    private sealed class FileAspectAction(
        string ttlPath, Type entityType, AspectKind kind, string aspectName) : IPendingAspectAction
    {
        public void Execute(IShapeRegistry registry, IShapeCache cache)
        {
            var fullPath = Path.IsPathRooted(ttlPath)
                ? ttlPath
                : Path.Combine(AppContext.BaseDirectory, ttlPath);
            var ttl = File.ReadAllText(fullPath);
            cache.GetOrParse(ttl); // throws AspectTtlParseException if malformed
            var aspect = new InlineTtlShapeAspect(aspectName, ttl, contextWhere: null);
            registry.Register(aspect, entityType, kind);
        }
    }
}
