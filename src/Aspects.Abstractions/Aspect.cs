namespace Forge.Aspects.Abstractions;

/// <summary>Well-known aspect constants and the no-operation sentinel.</summary>
public static class Aspect
{
    /// <summary>
    /// The IRI of the no-operation aspect. Operations carrying this IRI bypass all
    /// validation. This is the default for <c>TransactionOperation.AspectIri</c>.
    /// </summary>
    public const string NoOpIri = "https://forge-it.net/aspects/noop";

    /// <summary>
    /// The no-operation aspect singleton. Carries <see cref="NoOpIri"/> as its identity.
    /// The engine fast-paths any operation whose <c>AspectIri</c> equals <see cref="NoOpIri"/>.
    /// </summary>
    public static readonly IAspect NoOp = NoOpAspect.Instance;

    private sealed class NoOpAspect : IAspect
    {
        public static readonly NoOpAspect Instance = new();
        public string Iri => NoOpIri;
        private NoOpAspect() { }
    }
}
