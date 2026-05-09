using Forge.Aspects.Abstractions;
using Forge.Entity;
using Forge.Aspects.Message;
using Forge.Aspects.Operation;
using Forge.Aspects.Query;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
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
    /// Registers the unified <see cref="IAspectStore"/>, shape infrastructure, all three
    /// aspect engines (operation, query, message), and:
    /// <list type="bullet">
    ///   <item>A decorated <see cref="ITransactionalEntityStore"/> that enforces operation-aspect validation.</item>
    ///   <item>A decorated <see cref="IEntityStore"/> that enforces query-aspect validation via <see cref="QueryAspectScope"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Must be called after the backend (e.g. <c>UseInMemory()</c>) has been registered.
    /// </remarks>
    public static IServiceCollection AddForgeAspects(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Unified aspect store — sealed after first resolve.
        services.TryAddSingleton<IAspectStore, AspectStore>();

        // Shape cache — shared by all engines.
        services.TryAddSingleton<IShapeCache, ShapeCache>();

        // Keep a list of pending aspect registrations so AddOperationAspect() / AddQueryAspect() /
        // AddMessageAspect() calls that precede or follow AddForgeAspects() can be executed at
        // first resolution (before the store is sealed).
        services.TryAddSingleton<PendingAspectRegistrations>();

        // Engines
        services.TryAddSingleton<IOperationAspectEngine, OperationAspectEngine>();
        services.TryAddSingleton<IQueryAspectEngine, QueryAspectEngine>();
        services.TryAddSingleton<IMessageAspectEngine, MessageAspectEngine>();

        // ── Inner (raw backend) store ─────────────────────────────────────────────────────────────
        // Capture any unkeyed IEntityStore that was registered *before* this call (e.g. a direct
        // services.AddSingleton<IEntityStore>(myStore) in tests that bypass UseInMemory/UseGraphDb).
        // If none exists yet the backend will be discovered at resolution time via BackendStoreKey,
        // making AddForgeAspects() order-independent relative to UseInMemory() / UseGraphDb().
        var rawDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IEntityStore) && d.ServiceKey is null);
        if (rawDescriptor is not null)
            services.Remove(rawDescriptor);

        services.TryAddKeyedSingleton<IEntityStore>(InnerStoreKey, (sp, _) =>
        {
            // Prefer a descriptor captured at registration time (backward-compat path for direct
            // IEntityStore registrations that don't use UseInMemory/UseGraphDb).
            if (rawDescriptor is not null)
                return ResolveFromDescriptor(rawDescriptor, sp);

            // Backend was registered after AddForgeAspects() — resolve from the well-known keyed key.
            return sp.GetKeyedService<IEntityStore>(ForgeEntityRepositoryBuilder.BackendStoreKey)
                ?? throw new InvalidOperationException(
                    "AddForgeAspects() requires an IEntityStore backend. " +
                    "Either call UseInMemory() or UseGraphDb() via AddForgeEntityRepository() " +
                    "(at any point before the host is built), or register an IEntityStore directly " +
                    "before calling AddForgeAspects().");
        });

        // ── AspectEnforcingEntityStore (unkeyed IEntityStore) ────────────────────────────────────
        services.AddSingleton<IEntityStore>(sp =>
        {
            var pending = sp.GetRequiredService<PendingAspectRegistrations>();
            pending.Execute(sp);

            return new AspectEnforcingEntityStore(
                sp.GetRequiredKeyedService<IEntityStore>(InnerStoreKey),
                sp.GetRequiredService<IQueryAspectEngine>(),
                sp.GetRequiredService<IAspectStore>(),
                sp.GetRequiredService<IRdfMapperRegistry>(),
                sp.GetRequiredService<IOptions<EntityRepositoryOptions>>());
        });

        // ── AspectEnforcingTransactionalStore ────────────────────────────────────────────────────
        // Registered under a well-known keyed key (AspectsTxKey) so AddForgeAuthorization() can
        // resolve it as the inner store for GuardedTransactionalStore regardless of call order.
        services.TryAddKeyedSingleton<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.AspectsTxKey, (sp, _) =>
        {
            var pending = sp.GetRequiredService<PendingAspectRegistrations>();
            pending.Execute(sp);

            var raw = sp.GetRequiredKeyedService<IEntityStore>(InnerStoreKey);

            if (raw is not ISparqlQueryStore queryStore)
                throw new InvalidOperationException(
                    $"AddForgeAspects() requires the registered IEntityStore to implement " +
                    $"ISparqlQueryStore, but '{raw.GetType().FullName}' does not. " +
                    $"Ensure the backend (e.g. UseInMemory) is registered before the host is built.");

            if (raw is not ITransactionalEntityStore txStore)
                throw new InvalidOperationException(
                    $"AddForgeAspects() requires the registered IEntityStore to implement " +
                    $"ITransactionalEntityStore, but '{raw.GetType().FullName}' does not.");

            return new AspectEnforcingTransactionalStore(
                txStore, queryStore, sp.GetRequiredService<IOperationAspectEngine>());
        });

        // Expose unkeyed ITransactionalEntityStore for consumers that don't use AddForgeAuthorization.
        // TryAdd so AddForgeAuthorization() (if registered before or after) can replace this with
        // GuardedTransactionalStore without conflict.
        services.TryAddSingleton<ITransactionalEntityStore>(sp =>
            sp.GetRequiredKeyedService<ITransactionalEntityStore>(
                ForgeEntityRepositoryBuilder.AspectsTxKey));

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
    /// Registers a code-origin operation aspect from a Turtle file on disk. The file path is resolved
    /// relative to <see cref="AppContext.BaseDirectory"/> unless rooted.
    /// TTL is parsed eagerly when <see cref="ITransactionalEntityStore"/> is first resolved;
    /// a malformed file throws <see cref="AspectTtlParseException"/> at that point.
    /// </summary>
    public static IServiceCollection AddOperationAspect(
        this IServiceCollection services,
        string ttlPath,
        string aspectIri)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(ttlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectIri);

        services.TryAddSingleton<PendingAspectRegistrations>();
        services.AddSingleton<IPendingAspectAction>(new FileOperationAspectAction(ttlPath, aspectIri));
        return services;
    }

    /// <summary>
    /// Registers a code-origin operation aspect directly. Keyed by <see cref="IAspect.Iri"/>.
    /// </summary>
    public static IServiceCollection AddOperationAspect(
        this IServiceCollection services,
        IOperationAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(aspect);

        services.TryAddSingleton<PendingAspectRegistrations>();
        services.AddSingleton<IPendingAspectAction>(new InlineOperationAspectAction(aspect));
        return services;
    }

    /// <summary>
    /// Registers a query aspect into the <see cref="IAspectStore"/> at startup.
    /// </summary>
    public static IServiceCollection AddQueryAspect(
        this IServiceCollection services,
        IQueryAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(aspect);

        services.TryAddSingleton<PendingAspectRegistrations>();
        services.AddSingleton<IPendingAspectAction>(new InlineQueryAspectAction(aspect));
        return services;
    }

    /// <summary>
    /// Registers a message aspect into the <see cref="IAspectStore"/> at startup.
    /// </summary>
    public static IServiceCollection AddMessageAspect(
        this IServiceCollection services,
        IMessageAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(aspect);

        services.TryAddSingleton<PendingAspectRegistrations>();
        services.AddSingleton<IPendingAspectAction>(new InlineMessageAspectAction(aspect));
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
            var store = sp.GetRequiredService<IAspectStore>();
            var cache = sp.GetRequiredService<IShapeCache>();

            foreach (var action in actions)
                action.Execute(store, cache);
        }
    }

    internal interface IPendingAspectAction
    {
        void Execute(IAspectStore store, IShapeCache cache);
    }

    private sealed class FileOperationAspectAction(string ttlPath, string aspectIri) : IPendingAspectAction
    {
        public void Execute(IAspectStore store, IShapeCache cache)
        {
            var fullPath = Path.IsPathRooted(ttlPath)
                ? ttlPath
                : Path.Combine(AppContext.BaseDirectory, ttlPath);
            var ttl = File.ReadAllText(fullPath);
            cache.GetOrParse(ttl); // throws AspectTtlParseException if malformed
            var aspect = new InlineTtlOperationAspect(aspectIri, ttl, contextWhere: null);
            store.RegisterOperation(aspect);
        }
    }

    private sealed class InlineOperationAspectAction(IOperationAspect aspect) : IPendingAspectAction
    {
        public void Execute(IAspectStore store, IShapeCache _) => store.RegisterOperation(aspect);
    }

    private sealed class InlineQueryAspectAction(IQueryAspect aspect) : IPendingAspectAction
    {
        public void Execute(IAspectStore store, IShapeCache _) => store.RegisterQuery(aspect);
    }

    private sealed class InlineMessageAspectAction(IMessageAspect aspect) : IPendingAspectAction
    {
        public void Execute(IAspectStore store, IShapeCache _) => store.RegisterMessage(aspect);
    }
}
