using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Message;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Tests for <see cref="IMessageAspectEngine"/> / <see cref="MessageAspectEngine"/>.
/// Covers Trunk 04 acceptance criteria.
/// </summary>
public sealed class MessageAspectEngineTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────────

    // A minimal message with a required Name property.
    private sealed class NamedMessage
    {
        public string? Name { get; init; }
        public int Count { get; init; }
    }

    // Full-namespace string used in SHACL: must match MessageAspectEngine.ClassIri.
    private static readonly string NamedMessageClassIri =
        $"urn:{typeof(NamedMessage).FullName}";

    private static readonly string ForgeNs = "https://forge-it.net/";

    private static IMessageAspectEngine BuildEngine()
    {
        var services = new ServiceCollection();
        services.Configure<Forge.Repository.EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeAspects();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IMessageAspectEngine>();
    }

    // A SHACL shape that requires sh:minCount 1 on the Name predicate.
    private static string RequiredNameShape() => $@"
@prefix sh:  <http://www.w3.org/ns/shacl#> .
@prefix forge: <{ForgeNs}> .

<urn:shape:named-message-name-required>
    a sh:NodeShape ;
    sh:targetClass <{NamedMessageClassIri}> ;
    sh:property [
        sh:path forge:Name ;
        sh:minCount 1 ;
        sh:message ""Name is required."" ;
    ] .
";

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Null aspect → no-op
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_null_aspect_is_noop()
    {
        var engine = BuildEngine();
        await engine.ValidateAsync(new NamedMessage(), aspect: null);
        // no exception — test passes
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Aspect with null ShapeTtl → no-op
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_aspect_with_null_ShapeTtl_is_noop()
    {
        var engine = BuildEngine();
        var aspect = new InlineTtlMessageAspect("no-shape", shapeTtl: null);
        await engine.ValidateAsync(new NamedMessage(), aspect);
        // no exception — test passes
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Conforming message → no exception
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_conforming_message_does_not_throw()
    {
        var engine = BuildEngine();
        var aspect = new InlineTtlMessageAspect("name-required", RequiredNameShape());

        // Name property is present → conforms
        var message = new NamedMessage { Name = "Alice", Count = 3 };
        await engine.ValidateAsync(message, aspect);
        // no exception — test passes
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Missing required property → MessageAspectViolationException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_missing_required_property_throws_MessageAspectViolationException()
    {
        var engine = BuildEngine();
        var aspect = new InlineTtlMessageAspect("name-required", RequiredNameShape());

        // Name is null → not projected → sh:minCount 1 violated
        var message = new NamedMessage { Name = null };
        await Should.ThrowAsync<MessageAspectViolationException>(
            () => engine.ValidateAsync(message, aspect).AsTask());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Exception carries correct AspectName and non-empty Violations
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_exception_carries_AspectName_and_Violations()
    {
        var engine = BuildEngine();
        const string aspectName = "name-required";
        var aspect = new InlineTtlMessageAspect(aspectName, RequiredNameShape());

        var message = new NamedMessage { Name = null };
        var ex = await Should.ThrowAsync<MessageAspectViolationException>(
            () => engine.ValidateAsync(message, aspect).AsTask());

        ex.AspectName.ShouldBe(aspectName);
        ex.Violations.ShouldNotBeEmpty();
        ex.MessageType.ShouldBe(typeof(NamedMessage));
    }
}
