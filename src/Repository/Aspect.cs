namespace Forge.Repository;

/// <summary>Well-known aspect singletons.</summary>
public static class Aspect
{
    /// <summary>
    /// The no-operation aspect. An operation that declares this aspect skips all validation
    /// and is applied directly. This is the default for all transaction operations.
    /// </summary>
    public static readonly IOperationAspect NoOp = NoOpAspect.Instance;

    /// <summary>Sealed internal sentinel — the engine fast-paths via reference equality.</summary>
    internal sealed class NoOpAspect : IOperationAspect
    {
        internal static readonly NoOpAspect Instance = new();
        public string Name => "noop";
        private NoOpAspect() { }
    }
}
