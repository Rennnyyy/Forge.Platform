namespace Forge.Execution;

/// <summary>
/// Discriminated union representing the outcome of an execution: either a successful
/// response or a typed failure. Domain events emitted during execution are available
/// on both variants via <see cref="Events"/>.
/// See Execution ADR-0001.
/// </summary>
/// <typeparam name="TResponse">The successful response message type.</typeparam>
public abstract record ExecutionResult<TResponse> where TResponse : class
{
    /// <summary>
    /// Domain events emitted by the handler during execution.
    /// Present on both <see cref="Ok"/> and <see cref="Fail"/> variants.
    /// </summary>
    public IReadOnlyList<object> Events { get; init; } = [];

    /// <summary>Successful outcome carrying the handler response.</summary>
    public sealed record Ok(TResponse Response) : ExecutionResult<TResponse>;

    /// <summary>Failed outcome carrying a structured error.</summary>
    public sealed record Fail(ExecutionError Error) : ExecutionResult<TResponse>;
}
