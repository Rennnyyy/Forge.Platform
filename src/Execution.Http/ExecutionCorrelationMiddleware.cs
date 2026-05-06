using Microsoft.AspNetCore.Http;

namespace Forge.Execution.Http;

/// <summary>
/// Middleware that establishes an ambient <see cref="ExecutionScope"/> for every HTTP request.
/// Reads the <c>X-Forge-Correlation-ID</c> request header as <see cref="ExecutionCorrelation.CallerCorrelationId"/>
/// and generates a new <see cref="ExecutionCorrelation.ExecutionId"/>.
/// On response start, writes the <c>X-Forge-Execution-ID</c> header.
/// See Execution ADR-0002.
/// </summary>
internal sealed class ExecutionCorrelationMiddleware
{
    internal const string CorrelationRequestHeader = "X-Forge-Correlation-ID";
    internal const string ExecutionResponseHeader = "X-Forge-Execution-ID";

    private readonly RequestDelegate _next;

    public ExecutionCorrelationMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var callerCorrelationId = context.Request.Headers[CorrelationRequestHeader]
            .FirstOrDefault();
        Guid? parsedCallerId = Guid.TryParse(callerCorrelationId?.Trim(), out var g) ? g : null;

        var correlation = new ExecutionCorrelation
        {
            CallerCorrelationId = parsedCallerId,
        };

        using (ExecutionScope.Use(correlation))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[ExecutionResponseHeader] =
                    correlation.ExecutionId.ToString();
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
