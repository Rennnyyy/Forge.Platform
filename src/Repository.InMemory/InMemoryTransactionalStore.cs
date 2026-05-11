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
                    throw new InvalidOperationException(
                        $"Entity '{write.Entity.Iri}' already exists; WriteMode is Create.");

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
    /// Records the current triple state for every IRI targeted by the operations.
    /// Called while holding the semaphore, so no concurrent mutation can occur.
    /// </summary>
    private Dictionary<INode, List<Triple>> TakeSnapshot(IReadOnlyList<TransactionOperation> operations)
    {
        var result = new Dictionary<INode, List<Triple>>(new FastNodeComparer());
        var graph = CurrentGraph;
        foreach (var op in operations)
        {
            if (op is DropGraphOperation)
                continue;

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
