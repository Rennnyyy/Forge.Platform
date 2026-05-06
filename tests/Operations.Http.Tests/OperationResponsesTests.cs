using Shouldly;

namespace Forge.Operations.Http.Tests;

/// <summary>
/// Structural tests for the response record types in <c>Forge.Operations.Http</c>.
/// </summary>
public sealed class OperationResponsesTests
{
    [Fact]
    public void OperationCreatedResponse_StoresIri()
    {
        var r = new OperationCreatedResponse("https://forge-it.net/widgets/1");
        r.Iri.ShouldBe("https://forge-it.net/widgets/1");
    }

    [Fact]
    public void OperationUpdatedResponse_StoresIri()
    {
        var r = new OperationUpdatedResponse("https://forge-it.net/widgets/2");
        r.Iri.ShouldBe("https://forge-it.net/widgets/2");
    }

    [Fact]
    public void OperationDeletedResponse_IsDefaultConstructible()
    {
        var r = new OperationDeletedResponse();
        r.ShouldNotBeNull();
    }

    [Fact]
    public void OperationListResponse_ExposesItems()
    {
        var items = new[] { "a", "b", "c" };
        var r = new OperationListResponse<string>(items);
        r.Items.ShouldBe(items);
    }

    [Fact]
    public void OperationListResponse_EmptyItems()
    {
        var r = new OperationListResponse<string>([]);
        r.Items.ShouldBeEmpty();
    }

    [Fact]
    public void ResponseRecords_SupportEquality()
    {
        new OperationCreatedResponse("iri:a").ShouldBe(new OperationCreatedResponse("iri:a"));
        new OperationCreatedResponse("iri:a").ShouldNotBe(new OperationCreatedResponse("iri:b"));
        new OperationUpdatedResponse("iri:a").ShouldBe(new OperationUpdatedResponse("iri:a"));
        new OperationDeletedResponse().ShouldBe(new OperationDeletedResponse());
    }
}
