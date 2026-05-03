namespace Forge.Capability;

public abstract record CapabilityResult<TResponse> where TResponse : class
{
    public IReadOnlyList<object> Events { get; init; } = [];

    public sealed record Ok(TResponse Response) : CapabilityResult<TResponse>;
    public sealed record Fail(CapabilityError Error) : CapabilityResult<TResponse>;
}
