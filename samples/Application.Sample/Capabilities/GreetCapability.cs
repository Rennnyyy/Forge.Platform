using Forge.Capability;
using Forge.Execution;

namespace Forge.Application.Sample;

/// <summary>
/// Inbound command: ask the service to greet a person by name.
/// </summary>
public sealed record GreetCommand(string Name);


/// <summary>
/// Response returned by the greet capability.
/// </summary>
public sealed record GreetResponse(string Message);


/// <summary>
/// Demo capability handler that returns a greeting message.
/// Registered under the identity <c>demo.greet</c>; the HTTP transport
/// maps this to the route <c>POST /demo/greet</c>.
/// </summary>
[Capability("demo.greet")]
public sealed class GreetHandler : ICapabilityHandler<GreetCommand, GreetResponse>
{
    public ValueTask<ExecutionResult<GreetResponse>> HandleAsync(
        GreetCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var message = $"Hello, {command.Name}! The Forge platform is running.";
        return ValueTask.FromResult<ExecutionResult<GreetResponse>>(
            new ExecutionResult<GreetResponse>.Ok(new GreetResponse(message)));
    }
}
