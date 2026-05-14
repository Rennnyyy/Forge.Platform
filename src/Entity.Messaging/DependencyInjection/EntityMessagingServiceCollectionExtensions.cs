using System.Text.RegularExpressions;
using Forge.Entity;
using Forge.Entity.Messaging;
using Forge.Messaging.Abstractions;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Entity.Messaging.DependencyInjection;

/// <summary>
/// DI extensions for the EntityEvents slice.
/// See root ADR-0021.
/// </summary>
public static class EntityMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="EventEmittingTransactionalStore"/> decorator under
    /// <see cref="ForgeEntityRepositoryBuilder.EventsTxKey"/>.
    /// <para>
    /// Resolves <see cref="ForgeEntityRepositoryBuilder.AspectsTxKey"/> at provider-build time,
    /// falling back to <see cref="ForgeEntityRepositoryBuilder.BackendStoreKey"/> when aspect
    /// enforcement is not registered. Call order relative to <c>AddForgeAspects()</c> and
    /// <c>AddForgeAuthorization()</c> is arbitrary.
    /// </para>
    /// <para>
    /// Individual entity types opt in to event emission via
    /// <see cref="AddForgeEntityEvent{TEntity}"/>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddForgeEntityEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEntityEventEmitterRegistry>(sp =>
            new EntityEventEmitterRegistry(sp.GetServices<IEntityEventEmitter>()));

        services.TryAddKeyedSingleton<ITransactionalEntityStore>(
            ForgeEntityRepositoryBuilder.EventsTxKey,
            (sp, _) =>
            {
                var inner =
                    sp.GetKeyedService<ITransactionalEntityStore>(ForgeEntityRepositoryBuilder.AspectsTxKey)
                    ?? sp.GetKeyedService<IEntityStore>(ForgeEntityRepositoryBuilder.BackendStoreKey)
                        as ITransactionalEntityStore
                    ?? throw new InvalidOperationException(
                        "AddForgeEntityEvents() requires an ITransactionalEntityStore backend. " +
                        "Ensure UseInMemory() or UseGraphDb() is called via AddForgeEntityRepository() " +
                        "at any point before the host is built.");

                var registry = sp.GetRequiredService<IEntityEventEmitterRegistry>();
                return new EventEmittingTransactionalStore(inner, registry);
            });

        // Expose unkeyed ITransactionalEntityStore for consumers that don't use AddForgeAuthorization.
        // Use the capture-and-replace pattern (same as AddForgeAuthorization / AddForgeBranch) so
        // that AddForgeEntityEvents() is order-independent relative to AddForgeAspects().
        // TryAddSingleton would silently lose to AddForgeAspects() if it ran first, meaning
        // EventEmittingTransactionalStore would never appear in the unkeyed slot.
        // See Entity.Messaging ADR-0001.
        var rawDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(ITransactionalEntityStore) && d.ServiceKey is null);
        if (rawDescriptor is not null)
            services.Remove(rawDescriptor);

        // Always resolves to the fully-decorated EventsTxKey store at provider-build time.
        // AddForgeAuthorization() will perform its own capture-and-replace on top of this.
        services.AddSingleton<ITransactionalEntityStore>(sp =>
            sp.GetRequiredKeyedService<ITransactionalEntityStore>(
                ForgeEntityRepositoryBuilder.EventsTxKey));

        return services;
    }

    /// <summary>
    /// Registers entity-change event emission for <typeparamref name="TEntity"/>.
    /// <para>
    /// Requires <see cref="AddForgeEntityEvents"/> to be called on the same
    /// <see cref="IServiceCollection"/>. Call order is arbitrary.
    /// </para>
    /// <para>
    /// Requires an <see cref="IMessageProducer{TKey,TValue}"/> for
    /// <c>IMessageProducer&lt;string, EntityChangedEnvelope&lt;TEntity&gt;&gt;</c> to be
    /// registered (e.g. via <c>AddForgeMessagingInMemory()</c> or a Kafka extension).
    /// </para>
    /// </summary>
    /// <typeparam name="TEntity">The entity type to emit events for.</typeparam>
    /// <param name="configure">
    /// Callback to configure <see cref="EntityEventOptions"/> for this entity type.
    /// Topic names default to <c>forge.entities.{type-name}.history</c> and
    /// <c>forge.entities.{type-name}.state</c>.
    /// <see cref="EntityEventOptions.TypeIri"/> must be set to a non-empty value.
    /// </param>
    public static IServiceCollection AddForgeEntityMessaging<TEntity>(
        this IServiceCollection services,
        Action<EntityEventOptions> configure)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new EntityEventOptions();
        var kebabName = ToKebabCase(typeof(TEntity).Name);
        opts.HistoryTopic = $"forge.entities.{kebabName}.history";
        opts.StateTopic = $"forge.entities.{kebabName}.state";

        configure(opts);

        if (string.IsNullOrWhiteSpace(opts.TypeIri))
            throw new InvalidOperationException(
                $"AddForgeEntityMessaging<{typeof(TEntity).Name}>: " +
                $"{nameof(EntityEventOptions.TypeIri)} must be set to a non-empty value.");

        services.AddSingleton<IEntityEventEmitter>(sp =>
        {
            var producer = sp.GetRequiredService<IMessageProducer<string, EntityChangedEnvelope<TEntity>>>();
            return new EntityEventEmitter<TEntity>(
                producer,
                typeName: typeof(TEntity).Name,
                typeIri: opts.TypeIri,
                historyTopic: opts.HistoryTopic,
                stateTopic: opts.StateTopic);
        });

        return services;
    }

    // ──────────────────────────────────────────────────────────────────────────────────

    private static readonly Regex PascalWordBoundary =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Converts a PascalCase CLR type name to lower-kebab-case.
    /// Examples: <c>"Artist"</c> → <c>"artist"</c>, <c>"ArtistManager"</c> → <c>"artist-manager"</c>.
    /// </summary>
    internal static string ToKebabCase(string name) =>
        PascalWordBoundary.Replace(name, "-").ToLowerInvariant();
}
