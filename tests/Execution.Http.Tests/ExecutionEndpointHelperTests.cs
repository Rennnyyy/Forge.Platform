using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Execution;
using Forge.Execution.Http;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Forge.Execution.Http.Tests;

public sealed class ExecutionEndpointHelperTests
{
    private sealed record TestResponse(string Value);

    [Fact]
    public async Task Returns_result_from_handler_on_success()
    {
        var expected = Results.Ok(new TestResponse("ok"));

        var result = await ExecutionEndpointHelper.InvokeAsync(() =>
            ValueTask.FromResult<IResult>(expected));

        result.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Catches_MessageAspectViolationException_and_returns_422()
    {
        var result = await ExecutionEndpointHelper.InvokeAsync(() =>
            throw new MessageAspectViolationException(
                typeof(TestResponse),
                "urn:test-aspect",
                [new AspectViolation("urn:node", null, "http://www.w3.org/ns/shacl#Violation", "shacl failed", null)]));

        result.ShouldBeOfType<Microsoft.AspNetCore.Http.HttpResults.UnprocessableEntity<ExecutionError>>();
    }

    [Fact]
    public async Task Catches_AspectViolationException_and_returns_422()
    {
        var result = await ExecutionEndpointHelper.InvokeAsync(() =>
            throw new AspectViolationException(
                [new AspectViolation("urn:node", null, "http://www.w3.org/ns/shacl#Violation", "entity shacl failed", null)],
                new DeleteOperation("urn:test-entity"),
                "urn:test-aspect"));

        result.ShouldBeOfType<Microsoft.AspNetCore.Http.HttpResults.UnprocessableEntity<ExecutionError>>();
    }

    [Fact]
    public async Task Does_not_catch_other_exceptions()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ExecutionEndpointHelper.InvokeAsync(() =>
                throw new InvalidOperationException("should propagate")));
    }

    [Fact]
    public async Task Throws_for_null_handler()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await ExecutionEndpointHelper.InvokeAsync(null!));
    }
}
