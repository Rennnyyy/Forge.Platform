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
using Microsoft.Extensions.Hosting;
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
    /// May be called in any order relative to the backend (<c>UseInMemory()</c> / <c>UseGraphDb()</c>)
    /// and <see cref="Forge.Authorization.DependencyInjection.AuthorizationServiceCollectionExtensions.AddForgeAuthorization"/>.
    /// The backend <see cref="IEntityStore"/> and the authorization
    /// <see cref="ITransactionalEntityStore"/> are resolved via keyed services at
    /// provider-build time rather than at registration time (see ADR-0014).
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
                txStore, queryStore, sp.GetRequiredService<IOperationAspectEngine>(),
                // The unkeyed IEntityStore is AspectEnforcingEntityStore, which applies
                // QueryAspectScope filtering on reads. Injecting it here ensures that callers
                // who resolve ITransactionalEntityStore and call LoadAsync / QueryByTypeAsync
                // still get query-aspect enforcement (Fix #5 / flaw identified in review).
                sp.GetRequiredService<IEntityStore>());
        });

        // Expose unkeyed ITransactionalEntityStore for consumers that don't use AddForgeAuthorization.
        // TryAdd so AddForgeAuthorization() (if registered before or after) can replace this with
        // GuardedTransactionalStore without conflict.
        services.TryAddSingleton<ITransactionalEntityStore>(sp =>
            sp.GetRequiredKeyedService<ITransactionalEntityStore>(
                ForgeEntityRepositoryBuilder.AspectsTxKey));

        return services;
    }

    /// <summary>
    /// Inserts an <see cref="AspectEnforcingTransactionalStore"/> decorator on a specified keyed
    /// <see cref="ITransactionalEntityStore"/>, satisfying obligation 2 of root ADR-0019.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="storeKey">
    /// The keyed-service key of the managed-entity store to decorate
    /// (e.g. <c>"forge.branch.management"</c>).
    /// </param>
    /// <param name="sparqlStoreResolver">
    /// Factory that resolves the raw backend <see cref="ISparqlQueryStore"/> (before any
    /// guard decorators). Used by the Context pass of the aspect engine. Typically resolves
    /// the raw backend registered under <c>"&lt;storeKey&gt;.raw"</c>.
    /// </param>
    /// <remarks>
    /// The existing <see cref="ITransactionalEntityStore"/> registration for
    /// <paramref name="storeKey"/> is captured at extension-method call time and used as the
    /// inner store for the wrapper. Adding a new keyed singleton under the same key is
    /// intentional: .NET DI returns the last registration for
    /// <c>GetRequiredKeyedService&lt;T&gt;(key)</c>, so the aspect wrapper transparently
    /// replaces the previous head of the chain for all callers.
    /// </remarks>
    public static IServiceCollection AddForgeAspectsForKeyedStore(
        this IServiceCollection services,
        string storeKey,
        Func<IServiceProvider, ISparqlQueryStore> sparqlStoreResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        ArgumentNullException.ThrowIfNull(sparqlStoreResolver);

        // Capture the CURRENT last ITransactionalEntityStore descriptor for storeKey at
        // registration time (closure capture). This descriptor is the pre-decoration chain —
        // calling its factory at resolution time gives us the guarded-chain singleton without
        // any circularity, because the new registration below is added AFTER this capture.
        var existingDesc = services.LastOrDefault(
            d => d.ServiceType == typeof(ITransactionalEntityStore)
              && Equals(d.ServiceKey, storeKey))
            ?? throw new InvalidOperationException(
                $"AddForgeAspectsForKeyedStore() found no ITransactionalEntityStore registered " +
                $"for key '{storeKey}'. Call the entity-layer DI helper (e.g. AddForgeBranch()) " +
                $"before calling AddForgeAspectsForKeyedStore(). See root ADR-0019.");

        // Add a new ITransactionalEntityStore registration under storeKey. Because DI returns
        // the last registration for GetRequiredKeyedService, this becomes the new default
        // while the captured descriptor still resolves the original guarded chain.
        services.AddKeyedSingleton<ITransactionalEntityStore>(storeKey, (sp, _) =>
        {
            var pending = sp.GetRequiredService<PendingAspectRegistrations>();
            pending.Execute(sp);

            // Resolve the inner (guarded chain) singleton via the captured descriptor.
            var inner = ResolveFromKeyedDescriptor(existingDesc, sp, storeKey);
            var queryStore = sparqlStoreResolver(sp);
            var engine = sp.GetRequiredService<IOperationAspectEngine>();

            // Use the inner store for reads — no query-aspect filtering on management stores
            // (the management store is a privileged internal surface, not a user-facing query API).
            return new AspectEnforcingTransactionalStore(inner, queryStore, engine, inner);
        });

        // Enforcement marker — checked at startup by ManagedEntityAspectValidationService.
        services.AddSingleton(new AspectEnforcedKeyedStoreRegistration(storeKey));

        // Validation service — registered once (TryAdd) whenever AddForgeAspectsForKeyedStore
        // is called. Requires IAspectStore to be resolvable (i.e. AddForgeAspects() was called).
        services.TryAddSingleton<PendingAspectRegistrations>();
        services.TryAddSingleton<ManagedEntityAspectValidationService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ManagedEntityAspectValidationService>(
                sp => sp.GetRequiredService<ManagedEntityAspectValidationService>()));

        return services;
    }

    private static ITransactionalEntityStore ResolveFromKeyedDescriptor(
        ServiceDescriptor d, IServiceProvider sp, object key) => d switch
    {
        { ImplementationInstance: ITransactionalEntityStore inst } => inst,
        { KeyedImplementationFactory: { } f } => (ITransactionalEntityStore)f(sp, key),
        { KeyedImplementationInstance: { } i } => (ITransactionalEntityStore)i,
        { KeyedImplementationType: { } t } =>
            (ITransactionalEntityStore)ActivatorUtilities.CreateInstance(sp, t),
        _ => throw new InvalidOperationException(
            $"Unexpected ITransactionalEntityStore service descriptor shape for key '{key}'.")
    };

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
