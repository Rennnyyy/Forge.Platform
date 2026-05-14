using System.Reflection;
using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Options;

namespace Forge.Branch.Merge;

/// <summary>
/// Default implementation of <see cref="IMergePlanner"/>. See Branch ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// For each entry in the <see cref="EntityGraphDelta"/>:
/// <list type="number">
///   <item>Resolves the CLR type via <c>IRdfMapperRegistry.ForTypeIri</c>.</item>
///   <item>Hydrates the entity from the source store (scoped via
///     <see cref="BranchScope.Use"/>).</item>
///   <item>Checks existence in the target store to decide
///     <c>CreateOperation&lt;T&gt;</c> vs <c>UpdateOperation&lt;T&gt;</c>.</item>
///   <item>Builds an owning-dependency DAG by inspecting <c>[Owning]</c> properties
///     on the hydrated entity.</item>
///   <item>Returns operations in Kahn-sorted topological order.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class MergePlanner : IMergePlanner
{
    private readonly IRdfMapperRegistry _registry;
    private readonly EntityRepositoryOptions _repoOptions;

    // Cached MethodInfo for the LoadShimAsync<T> helper.
    private static readonly MethodInfo _loadShimMethod =
        typeof(MergePlanner).GetMethod(nameof(LoadShimAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Cached MethodInfo for IEntityStore.LoadAsync<T> — used for target-existence check.
    private static readonly MethodInfo _targetCheckShimMethod =
        typeof(MergePlanner).GetMethod(nameof(ExistsInTargetAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public MergePlanner(IRdfMapperRegistry registry, IOptions<EntityRepositoryOptions> repoOptions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(repoOptions);
        _registry = registry;
        _repoOptions = repoOptions.Value;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TransactionOperation>> PlanAsync(
        EntityGraphDelta delta,
        IEntityStore sourceStore,
        IEntityStore targetStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentNullException.ThrowIfNull(sourceStore);
        ArgumentNullException.ThrowIfNull(targetStore);

        if (delta.IsEmpty) return [];

        // ── Step 1: resolve types, hydrate from source, check target existence ──
        var iriToOp = new Dictionary<string, TransactionOperation>(
            delta.Entries.Count, StringComparer.Ordinal);

        foreach (var entry in delta.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mapper = _registry.ForTypeIri(entry.TypeIri, _repoOptions)
                ?? throw new MergePlanUnresolvableTypeException(entry.TypeIri);

            // Load entity from source graph.
            var entity = await InvokeLoadShimAsync(mapper.EntityType, sourceStore,
                entry.EntityIri, delta.SourceGraphIri, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new MergePlanHydrationException(entry.EntityIri);

            // Decide create vs update by checking the target graph.
            bool existsInTarget = await InvokeExistsInTargetAsync(mapper.EntityType, targetStore,
                entry.EntityIri, delta.TargetGraphIri, cancellationToken)
                .ConfigureAwait(false);

            TransactionOperation op = existsInTarget
                ? MakeUpdateOperation(mapper.EntityType, entity)
                : MakeCreateOperation(mapper.EntityType, entity);

            iriToOp[entry.EntityIri] = op;
        }

        // ── Step 2: build owning-dependency graph ──────────────────────────────
        var batchIris = new HashSet<string>(iriToOp.Keys, StringComparer.Ordinal);

        // outEdges[A] = list of entity IRIs that depend on A (A must come before them).
        var outEdges = iriToOp.Keys.ToDictionary(
            k => k, _ => new List<string>(), StringComparer.Ordinal);
        var inDegree = iriToOp.Keys.ToDictionary(
            k => k, _ => 0, StringComparer.Ordinal);

        foreach (var entry in delta.Entries)
        {
            var mapper = _registry.ForTypeIri(entry.TypeIri, _repoOptions)!;
            var entity = ((EntityWriteOperation)iriToOp[entry.EntityIri]).Entity;
            AddOwningEdges(entity, mapper.EntityType, batchIris, outEdges, inDegree);
        }

        // ── Step 3: Kahn's topological sort ───────────────────────────────────
        var queue = new Queue<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        var result = new List<TransactionOperation>(iriToOp.Count);

        while (queue.Count > 0)
        {
            var iri = queue.Dequeue();
            result.Add(iriToOp[iri]);

            foreach (var dependent in outEdges[iri])
            {
                if (--inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count < iriToOp.Count)
        {
            var cycleIris = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .ToList();
            throw new MergePlanCycleException(cycleIris);
        }

        return result;
    }

    // ── Owning-dependency extraction ──────────────────────────────────────────

    private static void AddOwningEdges(
        IEntity entity,
        Type entityType,
        HashSet<string> batchIris,
        Dictionary<string, List<string>> outEdges,
        Dictionary<string, int> inDegree)
    {
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<OwningAttribute>() is null) continue;

            var propType = prop.PropertyType;
            var value = prop.GetValue(entity);
            if (value is null) continue;

            // EntityRef<T> — single owning reference.
            if (propType.IsGenericType &&
                propType.GetGenericTypeDefinition() == typeof(EntityRef<>))
            {
                var iriProp = propType.GetProperty(nameof(EntityRef<IEntity>.Iri));
                if (iriProp is null) continue;
                var referencedIri = iriProp.GetValue(value) as string;
                if (referencedIri is null) continue;
                RecordEdge(referencedIri, entity.Iri, batchIris, outEdges, inDegree);
                continue;
            }

            // IEntityRefCollectionState — owning collection (EntityRefCollection<T>).
            if (value is IEntityRefCollectionState)
            {
                var irisProp = value.GetType().GetProperty("Iris");
                if (irisProp is null) continue;
                var iris = irisProp.GetValue(value) as IReadOnlyCollection<string>;
                if (iris is null) continue;
                foreach (var referencedIri in iris)
                    RecordEdge(referencedIri, entity.Iri, batchIris, outEdges, inDegree);
            }
        }
    }

    /// <summary>
    /// Records a dependency edge: <paramref name="prereqIri"/> must come before
    /// <paramref name="ownerIri"/> in the operation list. Only recorded when both
    /// IRIs are present in the current batch.
    /// </summary>
    private static void RecordEdge(
        string prereqIri,
        string ownerIri,
        HashSet<string> batchIris,
        Dictionary<string, List<string>> outEdges,
        Dictionary<string, int> inDegree)
    {
        if (!batchIris.Contains(prereqIri) || !batchIris.Contains(ownerIri)) return;
        outEdges[prereqIri].Add(ownerIri);
        inDegree[ownerIri]++;
    }

    // ── Reflection shims ──────────────────────────────────────────────────────

    private static async Task<IEntity?> InvokeLoadShimAsync(
        Type entityType, IEntityStore store, string iri, string graphIri, CancellationToken ct)
    {
        var method = _loadShimMethod.MakeGenericMethod(entityType);
        var task = (Task<IEntity?>)method.Invoke(null, [store, iri, graphIri, ct])!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<bool> InvokeExistsInTargetAsync(
        Type entityType, IEntityStore store, string iri, string graphIri, CancellationToken ct)
    {
        var method = _targetCheckShimMethod.MakeGenericMethod(entityType);
        var task = (Task<bool>)method.Invoke(null, [store, iri, graphIri, ct])!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<IEntity?> LoadShimAsync<T>(
        IEntityStore store, string iri, string graphIri, CancellationToken ct)
        where T : class, IEntity
    {
        using var _ = BranchScope.Use(graphIri);
        return await store.LoadAsync<T>(iri, ct).ConfigureAwait(false);
    }

    private static async Task<bool> ExistsInTargetAsync<T>(
        IEntityStore store, string iri, string graphIri, CancellationToken ct)
        where T : class, IEntity
    {
        using var _ = BranchScope.Use(graphIri);
        return await store.LoadAsync<T>(iri, ct).ConfigureAwait(false) is not null;
    }

    // ── Operation factories ───────────────────────────────────────────────────

    private static readonly MethodInfo _makeCreateMethod =
        typeof(MergePlanner).GetMethod(nameof(MakeCreate), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _makeUpdateMethod =
        typeof(MergePlanner).GetMethod(nameof(MakeUpdate), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static TransactionOperation MakeCreateOperation(Type entityType, IEntity entity)
    {
        var method = _makeCreateMethod.MakeGenericMethod(entityType);
        return (TransactionOperation)method.Invoke(null, [entity])!;
    }

    private static TransactionOperation MakeUpdateOperation(Type entityType, IEntity entity)
    {
        var method = _makeUpdateMethod.MakeGenericMethod(entityType);
        return (TransactionOperation)method.Invoke(null, [entity])!;
    }

    private static TransactionOperation MakeCreate<T>(IEntity entity)
        where T : class, IEntity => new CreateOperation<T>((T)entity);

    private static TransactionOperation MakeUpdate<T>(IEntity entity)
        where T : class, IEntity => new UpdateOperation<T>((T)entity);
}
