using Forge.Entity;
using Forge.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Forge.Aspects.DependencyInjection;

/// <summary>
/// DI extensions for the Aspects slice. See Aspects ADR-0003, ADR-0004, ADR-0006, ADR-0007.
/// </summary>
public static class AspectsServiceCollectionExtensions
{
    // Key for exposing the raw (pre-decoration) IEntityStore via keyed services so the
    // ITransactionalEntityStore factory can bypass the AspectEnforcingEntityStore wrapper
    // and cast the store to ISparqlQueryStore / ITransactionalEntityStore.
    internal const string InnerStoreKey = "forge.aspects.inner";

    /// <summary>
    /// Registers shape infrastructure, the Aspects engines (write + read), and:
    /// <list type="bullet">
    ///   <item>A decorated <see cref="ITransactionalEntityStore"/> that enforces write-aspect validation.</item>
    ///   <item>A decorated <see cref="IEntityStore"/> that enforces read-aspect validation via <see cref="QueryAspectScope"/>.</item>
    /// </list>
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

        // Message aspect registry — null-on-miss, sealed after first read.
        var messageRegistry = new MessageAspectRegistry();
        services.TryAddSingleton<IMessageAspectRegistry>(messageRegistry);

        // Keep a list of pending aspect registrations so AddCodeAspect() calls that
        // precede or follow AddForgeAspects() can be executed at first resolution.
        services.TryAddSingleton<PendingAspectRegistrations>();

        // Engines
        services.TryAddSingleton<IAspectEngine, AspectEngine>();
        services.TryAddSingleton<IQueryAspectEngine, QueryAspectEngine>();

        // Capture the raw IEntityStore descriptor BEFORE decoration so the
        // ITransactionalEntityStore factory can reach the store that actually implements
        // ISparqlQueryStore and ITransactionalEntityStore (AspectEnforcingEntityStore
        // does not forward those interfaces). Exposed via a keyed singleton.
        var rawDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEntityStore));
        if (rawDescriptor is not null)
        {
            services.Remove(rawDescriptor);

            // For ImplementationInstance descriptors, use the instance overload so that
            // DI does not attempt to dispose the instance during container teardown.
            // For factory/type descriptors, use the factory overload (resolved once, singleton).
            if (rawDescriptor.ImplementationInstance is IEntityStore existingInstance)
                services.AddKeyedSingleton<IEntityStore>(InnerStoreKey, existingInstance);
            else
                services.AddKeyedSingleton<IEntityStore>(InnerStoreKey,
                    (sp, _) => ResolveFromDescriptor(rawDescriptor, sp));

            services.AddSingleton<IEntityStore>(sp =>
                new AspectEnforcingEntityStore(
                    sp.GetRequiredKeyedService<IEntityStore>(InnerStoreKey),
                    sp.GetRequiredService<IQueryAspectEngine>(),
                    sp.GetRequiredService<IRdfMapperRegistry>(),
                    sp.GetRequiredService<IOptions<EntityRepositoryOptions>>()));
        }

        // ITransactionalEntityStore resolves the raw inner store directly so that ISparqlQueryStore
        // and ITransactionalEntityStore casts remain valid after IEntityStore is decorated.
        services.TryAddSingleton<ITransactionalEntityStore>(sp =>
        {
            var pending = sp.GetRequiredService<PendingAspectRegistrations>();
            pending.Execute(sp);

            IEntityStore raw = rawDescriptor is not null
                ? sp.GetRequiredKeyedService<IEntityStore>(InnerStoreKey)
                : sp.GetRequiredService<IEntityStore>();

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

    private static IEntityStore ResolveFromDescriptor(ServiceDescriptor d, IServiceProvider sp) => d switch
    {
        { ImplementationFactory: { } f } => (IEntityStore)f(sp),
        { ImplementationInstance: { } i } => (IEntityStore)i,
        { ImplementationType: { } t } => (IEntityStore)ActivatorUtilities.CreateInstance(sp, t),
        _ => throw new InvalidOperationException("Unexpected IEntityStore service descriptor shape.")
    };

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
            var aspect = new InlineTtlWriteAspect(aspectName, ttl, contextWhere: null);
            registry.Register(aspect, entityType, kind);
        }
    }
}
