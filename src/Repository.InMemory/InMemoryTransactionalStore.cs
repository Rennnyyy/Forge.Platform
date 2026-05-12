using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using VDS.RDF;

namespace Forge.Repository.InMemory;

/// <summary>
/// Implements <see cref="ITransactionalEntityStore"/> for <see cref="InMemoryEntityStore"/>
/// using a <see cref="SemaphoreSlim"/> for mutual exclusion and a triple-level snapshot for
/// rollback. See Entity.Repository.InMemory ADR-0001.
/// </summary>
public sealed partial class InMemoryEntityStore : ITransactionalEntityStore
{
    private readonly SemaphoreSlim _txLock = new(1, 1);

    /// <inheritdoc/>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0) return;

        await _txLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Snapshot the triple state for every targeted IRI before any mutation.
        var snapshot = TakeSnapshot(operations);
        try
        {
            foreach (var op in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplyOperation(op);
            }
        }
        catch
        {
            RestoreSnapshot(snapshot);
            throw;
        }
        finally
        {
            _txLock.Release();
        }
    }

    // ------------------------------------------------------------------ Operation dispatch

    private void ApplyOperation(TransactionOperation op)
    {
        var graph = CurrentGraph;
        switch (op)
        {
            case DropGraphOperation drop:
                DropCurrentGraph();
                break;

            case SeedGraphOperation seed:
                ApplySeed(seed);
                break;

            case DeleteOperation del:
                var delSubj = graph.CreateUriNode(UriFactory.Create(del.Iri));
                DeleteSubjectClosure(delSubj);
                break;

            case EntityWriteOperation write:
                var mapper = _registry.ForEntityType(write.Entity.GetType());
                var typeIri = mapper.ResolveTypeIri(_options);
                var sink = new CollectingTripleSink();
                mapper.ProjectEntity(write.Entity, sink, typeIri);

                var subj = graph.CreateUriNode(UriFactory.Create(write.Entity.Iri));
                if (write.Mode == WriteMode.Replace)
                    DeleteSubjectClosure(subj);
                else if (graph.GetTriplesWithSubject(subj).Any())
                    throw new EntityAlreadyExistsException(write.Entity.Iri);

                var blankCache = new Dictionary<string, IBlankNode>(StringComparer.Ordinal);
                foreach (var triple in sink.Triples)
                    graph.Assert(ToDotNetRdfTriple(triple, blankCache));
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported transaction operation type: {op.GetType().Name}");
        }
    }

    // ------------------------------------------------------------------ Snapshot helpers

    /// <summary>
    /// Copies entity triples from <see cref="SeedGraphOperation.SourceGraphIri"/> into
    /// <see cref="SeedGraphOperation.TargetGraphIri"/>. Both graphs are addressed directly
    /// by their IRIs in <see cref="_graphs"/>, independent of <see cref="BranchScope.Current"/>.
    /// Throws <see cref="SeedOperationMissingEntityException"/> if any requested entity IRI
    /// is absent from the source graph (consistent with the GraphDb backend behaviour).
    /// </summary>
    private void ApplySeed(SeedGraphOperation seed)
    {
        // Resolve source graph — must already exist.
        if (!_graphs.TryGetValue(seed.SourceGraphIri, out var sourceGraph))
            throw new SeedOperationMissingEntityException(seed.SourceGraphIri, seed.EntityIris);

        // Pre-validate: collect all subject-closures before touching the target graph.
        // This ensures we throw SeedOperationMissingEntityException without leaving partial
        // state in the target graph, keeping rollback simple (nothing to undo on failure).
        var missingIris = new List<string>();
        var closures = new Dictionary<string, List<Triple>>(StringComparer.Ordinal);
        foreach (var entityIri in seed.EntityIris)
        {
            var subj = sourceGraph.CreateUriNode(UriFactory.Create(entityIri));
            var triples = CollectSubjectClosure(sourceGraph, subj);
            if (triples.Count == 0)
                missingIris.Add(entityIri);
            else
                closures[entityIri] = triples;
        }

        if (missingIris.Count > 0)
            throw new SeedOperationMissingEntityException(seed.SourceGraphIri, missingIris);

        // All entities found — now write to target graph.
        if (!_graphs.TryGetValue(seed.TargetGraphIri, out var targetGraph))
        {
            targetGraph = new Graph();
            _graphs[seed.TargetGraphIri] = targetGraph;
        }

        foreach (var triples in closures.Values)
        {
            var blankCache = new Dictionary<string, IBlankNode>(StringComparer.Ordinal);
            foreach (var t in triples)
                targetGraph.Assert(RemapTriple(t, targetGraph, blankCache));
        }
    }

    private static List<Triple> CollectSubjectClosure(Graph graph, INode subject)
    {
        var result = new List<Triple>();
        var seen = new HashSet<INode>(new FastNodeComparer());
        var queue = new Queue<INode>();
        queue.Enqueue(subject);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node)) continue;
            foreach (var t in graph.GetTriplesWithSubject(node))
            {
                result.Add(t);
                if (t.Object is IBlankNode) queue.Enqueue(t.Object);
            }
        }
        return result;
    }

    private static Triple RemapTriple(
        Triple t, Graph target, Dictionary<string, IBlankNode> blanks) =>
        new(RemapNode(t.Subject, target, blanks),
            RemapNode(t.Predicate, target, blanks),
            RemapNode(t.Object, target, blanks));

    private static INode RemapNode(
        INode n, Graph target, Dictionary<string, IBlankNode> blanks) => n switch
        {
            IUriNode u => target.CreateUriNode(u.Uri),
            IBlankNode b => blanks.TryGetValue(b.InternalID, out var existing)
                ? existing
                : (blanks[b.InternalID] = target.CreateBlankNode(b.InternalID)),
            ILiteralNode l when l.Language is not null and not "" =>
                target.CreateLiteralNode(l.Value, l.Language),
            ILiteralNode l when l.DataType is not null =>
                target.CreateLiteralNode(l.Value, l.DataType),
            ILiteralNode l => target.CreateLiteralNode(l.Value),
            _ => throw new NotSupportedException($"Unsupported node type: {n?.NodeType}"),
        };

    // ------------------------------------------------------------------ Snapshot helpers

    /// <summary>
    /// Records the current triple state for every IRI targeted by the operations.
    /// Called while holding the semaphore, so no concurrent mutation can occur.
    /// </summary>
    private Dictionary<INode, List<Triple>> TakeSnapshot(IReadOnlyList<TransactionOperation> operations)
    {
        var result = new Dictionary<INode, List<Triple>>(new FastNodeComparer());
        var graph = CurrentGraph;
        foreach (var op in operations)
        {
            // SeedGraphOperation validates-then-writes with no partial state on failure;
            // no snapshot entry is needed.
            if (op is SeedGraphOperation)
                continue;

            if (op is DropGraphOperation)
            {
                // Capture every subject in the current graph so the DropGraph can be
                // rolled back when a later operation in the same transaction fails.
                foreach (var t in graph.Triples.ToList())
                {
                    if (!result.ContainsKey(t.Subject))
                        result[t.Subject] = graph.GetTriplesWithSubject(t.Subject).ToList();
                }
                continue;
            }

            var subj = graph.CreateUriNode(UriFactory.Create(op.EntityIri));
            if (!result.ContainsKey(subj))
                result[subj] = graph.GetTriplesWithSubject(subj).ToList();
        }
        return result;
    }

    /// <summary>
    /// Restores every affected subject to the state captured by <see cref="TakeSnapshot"/>.
    /// Deletes any triples the failed transaction may have written, then re-asserts the
    /// original triples.
    /// </summary>
    private void RestoreSnapshot(Dictionary<INode, List<Triple>> snapshot)
    {
        var graph = CurrentGraph;
        foreach (var (subj, originalTriples) in snapshot)
        {
            DeleteSubjectClosure(subj);
            foreach (var t in originalTriples)
                graph.Assert(t);
        }
    }
}
