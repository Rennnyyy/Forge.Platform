using Forge.Execution;

namespace Forge.Capability;

public interface ICapabilityHandler<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    ValueTask<ExecutionResult<TResponse>> HandleAsync(
        TCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default);
}
