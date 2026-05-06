using Forge.Execution;
using Forge.Execution.Http;
using Forge.Execution.Http.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Forge.Execution.Http.Tests;

/// <summary>
/// Tests for <see cref="ExecutionCorrelationMiddleware"/>.
/// </summary>
public sealed class ExecutionCorrelationMiddlewareTests
{
    private static IHost BuildHost()
        => new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.Configure(app =>
                {
                    app.UseExecutionCorrelation();
                    app.Run(async ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        var id = ExecutionScope.Current?.ExecutionId.ToString() ?? "none";
                        await ctx.Response.WriteAsync(id);
                    });
                });
            })
            .Build();

    [Fact]
    public async Task Response_contains_X_Forge_Execution_ID_header()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");

        response.Headers.ShouldContain(h => h.Key == ExecutionCorrelationMiddleware.ExecutionResponseHeader);
        var idHeader = response.Headers.GetValues(ExecutionCorrelationMiddleware.ExecutionResponseHeader).First();
        Guid.TryParse(idHeader, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task CallerCorrelationId_is_populated_from_request_header()
    {
        Guid? capturedCaller = null;
        var correlationGuid = Guid.NewGuid();
        using var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.Configure(app =>
                {
                    app.UseExecutionCorrelation();
                    app.Run(ctx =>
                    {
                        capturedCaller = ExecutionScope.Current?.CallerCorrelationId;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(ExecutionCorrelationMiddleware.CorrelationRequestHeader, correlationGuid.ToString());
        await client.SendAsync(request);

        capturedCaller.ShouldBe(correlationGuid);
    }

    [Fact]
    public async Task CallerCorrelationId_is_null_when_header_absent()
    {
        Guid? capturedCaller = Guid.NewGuid();
        using var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.Configure(app =>
                {
                    app.UseExecutionCorrelation();
                    app.Run(ctx =>
                    {
                        capturedCaller = ExecutionScope.Current?.CallerCorrelationId;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var client = host.GetTestClient();

        await client.GetAsync("/");

        capturedCaller.ShouldBeNull();
    }

    [Fact]
    public async Task ExecutionScope_is_cleared_after_request_completes()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        await client.GetAsync("/");

        // The AsyncLocal should be null outside the request pipeline.
        ExecutionScope.Current.ShouldBeNull();
    }
}
