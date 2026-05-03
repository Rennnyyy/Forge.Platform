namespace Forge.Aspects;

/// <summary>
/// Unified in-process registry for all aspect families (operation, query, message,
/// capability). All registrations must complete before the first <c>Resolve*</c> or
/// <c>TryResolve*</c> call — the store seals itself on first resolution.
/// The list properties (<c>*Iris</c>) do not seal the store and are safe to call at any time.
/// </summary>
public interface IAspectStore
{
    // ------------------------------------------------------------------ Registration

    /// <summary>
    /// Registers <paramref name="aspect"/> keyed by its <see cref="IAspect.Iri"/>.
    /// Throws <see cref="InvalidOperationException"/> if an aspect with the same IRI is already
    /// registered or if the store is sealed (first resolve was already called).
    /// </summary>
    void RegisterOperation(IOperationAspect aspect);

    /// <inheritdoc cref="RegisterOperation"/>
    void RegisterQuery(IQueryAspect aspect);

    /// <inheritdoc cref="RegisterOperation"/>
    void RegisterMessage(IMessageAspect aspect);

    /// <inheritdoc cref="RegisterOperation"/>
    void RegisterCapabilityAspect(CapabilityAspect aspect);

    // ------------------------------------------------------------------ Resolution (seals on first call)

    /// <summary>
    /// Returns the registered <see cref="IOperationAspect"/> for <paramref name="iri"/>.
    /// Seals the store on first call. Throws <see cref="AspectNotFoundException"/> on miss.
    /// </summary>
    IOperationAspect ResolveOperation(string iri);

    /// <inheritdoc cref="ResolveOperation"/>
    IQueryAspect ResolveQuery(string iri);

    /// <inheritdoc cref="ResolveOperation"/>
    IMessageAspect ResolveMessage(string iri);

    /// <inheritdoc cref="ResolveOperation"/>
    CapabilityAspect ResolveCapabilityAspect(string iri);

    /// <summary>
    /// Returns the registered <see cref="IOperationAspect"/> for <paramref name="iri"/>,
    /// or <c>null</c> on miss. Seals the store on first call.
    /// </summary>
    IOperationAspect? TryResolveOperation(string iri);

    /// <inheritdoc cref="TryResolveOperation"/>
    IQueryAspect? TryResolveQuery(string iri);

    /// <inheritdoc cref="TryResolveOperation"/>
    IMessageAspect? TryResolveMessage(string iri);

    /// <inheritdoc cref="TryResolveOperation"/>
    CapabilityAspect? TryResolveCapabilityAspect(string iri);

    // ------------------------------------------------------------------ Listing (non-sealing)

    /// <summary>All registered operation-aspect IRIs. Does not seal the store.</summary>
    IReadOnlyCollection<string> OperationIris { get; }

    /// <summary>All registered query-aspect IRIs. Does not seal the store.</summary>
    IReadOnlyCollection<string> QueryIris { get; }

    /// <summary>All registered message-aspect IRIs. Does not seal the store.</summary>
    IReadOnlyCollection<string> MessageIris { get; }

    /// <summary>All registered capability-aspect IRIs. Does not seal the store.</summary>
    IReadOnlyCollection<string> CapabilityAspectIris { get; }
}
