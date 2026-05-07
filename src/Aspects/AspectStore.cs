using Forge.Aspects.Abstractions;
using System.Collections.Concurrent;

namespace Forge.Aspects;

/// <summary>
/// Default <see cref="IAspectStore"/> implementation.
/// Thread-safe for registration (before sealing) and for resolution (after sealing).
/// </summary>
internal sealed class AspectStore : IAspectStore
{
    private readonly ConcurrentDictionary<string, IOperationAspect> _operations
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IQueryAspect> _queries
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IMessageAspect> _messages
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, CapabilityAspect> _capabilities
        = new(StringComparer.Ordinal);

    // 0 = open, 1 = sealed
    private int _sealed;

    private void EnsureNotSealed()
    {
        if (Volatile.Read(ref _sealed) != 0)
            throw new InvalidOperationException(
                "Cannot register aspects after the store has been sealed. " +
                "All registrations must complete before the first resolve call.");
    }

    private void Seal() => Interlocked.CompareExchange(ref _sealed, 1, 0);

    private static void AddToDict<T>(ConcurrentDictionary<string, T> dict, T aspect, string iri)
    {
        if (!dict.TryAdd(iri, aspect))
            throw new InvalidOperationException(
                $"An aspect with IRI '{iri}' is already registered.");
    }

    // ------------------------------------------------------------------ Registration

    public void RegisterOperation(IOperationAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        EnsureNotSealed();
        if (aspect.Iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved and cannot be registered.", nameof(aspect));
        AddToDict(_operations, aspect, aspect.Iri);
    }

    public void RegisterQuery(IQueryAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        EnsureNotSealed();
        if (aspect.Iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved and cannot be registered.", nameof(aspect));
        AddToDict(_queries, aspect, aspect.Iri);
    }

    public void RegisterMessage(IMessageAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        EnsureNotSealed();
        if (aspect.Iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved and cannot be registered.", nameof(aspect));
        AddToDict(_messages, aspect, aspect.Iri);
    }

    public void RegisterCapabilityAspect(CapabilityAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        EnsureNotSealed();
        if (aspect.Iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved and cannot be registered.", nameof(aspect));
        AddToDict(_capabilities, aspect, aspect.Iri);
    }

    // ------------------------------------------------------------------ Resolution (sealing)

    public IOperationAspect ResolveOperation(string iri)
    {
        Seal();
        return _operations.TryGetValue(iri, out var a)
            ? a : throw new AspectNotFoundException(iri, "operation");
    }

    public IQueryAspect ResolveQuery(string iri)
    {
        Seal();
        return _queries.TryGetValue(iri, out var a)
            ? a : throw new AspectNotFoundException(iri, "query");
    }

    public IMessageAspect ResolveMessage(string iri)
    {
        Seal();
        return _messages.TryGetValue(iri, out var a)
            ? a : throw new AspectNotFoundException(iri, "message");
    }

    public CapabilityAspect ResolveCapabilityAspect(string iri)
    {
        Seal();
        return _capabilities.TryGetValue(iri, out var a)
            ? a : throw new AspectNotFoundException(iri, "capability aspect");
    }

    public IOperationAspect? TryResolveOperation(string iri)
    {
        Seal();
        _operations.TryGetValue(iri, out var a);
        return a;
    }

    public IQueryAspect? TryResolveQuery(string iri)
    {
        Seal();
        _queries.TryGetValue(iri, out var a);
        return a;
    }

    public IMessageAspect? TryResolveMessage(string iri)
    {
        Seal();
        _messages.TryGetValue(iri, out var a);
        return a;
    }

    public CapabilityAspect? TryResolveCapabilityAspect(string iri)
    {
        Seal();
        _capabilities.TryGetValue(iri, out var a);
        return a;
    }

    // ------------------------------------------------------------------ Listing (non-sealing)

    public IReadOnlyCollection<string> OperationIris => _operations.Keys.ToArray();
    public IReadOnlyCollection<string> QueryIris => _queries.Keys.ToArray();
    public IReadOnlyCollection<string> MessageIris => _messages.Keys.ToArray();
    public IReadOnlyCollection<string> CapabilityAspectIris => _capabilities.Keys.ToArray();
}
