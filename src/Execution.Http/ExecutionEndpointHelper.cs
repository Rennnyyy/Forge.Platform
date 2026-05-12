using Forge.Aspects.Abstractions;
using Forge.Execution;
using Forge.Repository;
using Microsoft.AspNetCore.Http;

namespace Forge.Execution.Http;

/// <summary>
/// Provides a shared invocation wrapper for execution-layer HTTP endpoints.
/// Translates aspect violation exceptions into 422 Unprocessable Entity responses with
/// structured <see cref="ExecutionError"/> bodies, keeping endpoint lambdas free of
/// cross-cutting try/catch boilerplate.
/// See Execution ADR-0002.
/// </summary>
public static class ExecutionEndpointHelper
{
    /// <summary>
    /// Invokes <paramref name="handler"/> and translates known aspect violations into
    /// 422 responses:
    /// <list type="bullet">
    ///   <item><see cref="MessageAspectViolationException"/> → <c>SHACL_VIOLATION</c></item>
    ///   <item><see cref="AspectException"/> → <c>ENTITY_SHACL_VIOLATION</c> (for any aspect violation, including <c>AspectViolationException</c>)</item>
    ///   <item><see cref="EntityAlreadyExistsException"/> → <c>ENTITY_ALREADY_EXISTS</c> (409 Conflict)</item>
    /// </list>
    /// </summary>
    public static async ValueTask<IResult> InvokeAsync(Func<ValueTask<IResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        try
        {
            return await handler();
        }
        catch (MessageAspectViolationException ex)
        {
            return Results.UnprocessableEntity(
                new ExecutionError("SHACL_VIOLATION", ex.Message));
        }
        catch (AspectException ex)
        {
            return Results.UnprocessableEntity(
                new ExecutionError("ENTITY_SHACL_VIOLATION", ex.Message));
        }
        catch (EntityAlreadyExistsException ex)
        {
            return Results.Conflict(
                new ExecutionError("ENTITY_ALREADY_EXISTS", ex.Message));
        }
    }
}
