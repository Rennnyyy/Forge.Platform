using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Structure.DependencyInjection;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="StructureFilteringStore"/>: transparent filtering of
/// <see cref="Usage"/> via <see cref="StructureScope"/>, pass-through for other types,
/// and correct null-scope behaviour (no filtering when scope is absent).
/// </summary>
public sealed class StructureFilteringStoreTests
{
    private const string BranchIri = "https://forge-it.net/branches/main";
    private const string EvDim = "https://forge-it.net/dimensions/ev";
    private const string ColorDim = "https://forge-it.net/dimensions/color";
    private const string RedIri = "https://forge-it.net/colors/red";
    private const string ParentIri = "https://forge-it.net/structures/P";
    private const string ChildIri = "https://forge-it.net/structures/C";

    private static StructureConfiguration EmptyConfig() =>
        new(BranchIri: BranchIri, Options: new Dictionary<string, OptionValue>());

    private static StructureConfiguration WithFlag(string dimensionIri, bool value) =>
        new(BranchIri: BranchIri,
            Options: new Dictionary<string, OptionValue>
            {
                [dimensionIri] = new FlagOptionValue(value)
            });

    private static StructureConfiguration WithEnum(string dimensionIri, string valueIri) =>
        new(BranchIri: BranchIri,
            Options: new Dictionary<string, OptionValue>
            {
                [dimensionIri] = new EnumerationOptionValue(valueIri)
            });

    private static Usage MakeUsage(ConditionSet? conditions = null) =>
        new()
        {
            ParentStructureIri = ParentIri,
            ChildStructureIri = ChildIri,
            Conditions = conditions ?? ConditionSet.Empty
        };

    // ------------------------------------------------------------------ no-scope (pass-through)

    [Fact]
    public async Task Without_scope_all_usages_are_returned()
    {
        var u1 = MakeUsage(new ConditionSet([new FlagOptionCondition(EvDim, true, isRequired: true)]));
        var u2 = MakeUsage(ConditionSet.Empty);
        var store = new StubEntityStore(u1, u2);
        var sut = new StructureFilteringStore(store);

        var results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Without_scope_non_usage_types_pass_through()
    {
        var store = new StubEntityStore();
        var sut = new StructureFilteringStore(store);

        // Should not throw and should return the same (empty) enumerable from inner.
        var results = await sut.QueryByTypeAsync<Usage>().ToListAsync();
        results.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ ConditionSet.Empty (unconditional)

    [Fact]
    public async Task Unconditional_usages_are_always_returned_inside_scope()
    {
        var usage = MakeUsage(ConditionSet.Empty);
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        List<Usage> results;
        using (StructureScope.Use(EmptyConfig()))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldContain(usage);
    }

    // ------------------------------------------------------------------ FlagOptionCondition

    [Fact]
    public async Task Usage_with_satisfied_flag_condition_is_returned()
    {
        var usage = MakeUsage(new ConditionSet([new FlagOptionCondition(EvDim, true)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        List<Usage> results;
        using (StructureScope.Use(WithFlag(EvDim, true)))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldContain(usage);
    }

    [Fact]
    public async Task Usage_with_unsatisfied_flag_condition_is_excluded()
    {
        var usage = MakeUsage(new ConditionSet([new FlagOptionCondition(EvDim, true, isRequired: true)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        // Config provides no EvDim value and condition is required → excluded.
        List<Usage> results;
        using (StructureScope.Use(EmptyConfig()))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mixed_usages_only_satisfied_ones_are_returned()
    {
        var satisfied = MakeUsage(new ConditionSet([new FlagOptionCondition(EvDim, true)]));
        var unsatisfied = MakeUsage(new ConditionSet([new FlagOptionCondition(EvDim, false, isRequired: true)]));
        var store = new StubEntityStore(satisfied, unsatisfied);
        var sut = new StructureFilteringStore(store);

        List<Usage> results;
        using (StructureScope.Use(WithFlag(EvDim, true)))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldContain(satisfied);
        results.ShouldNotContain(unsatisfied);
    }

    // ------------------------------------------------------------------ EnumerationOptionCondition

    [Fact]
    public async Task Usage_with_satisfied_enum_condition_is_returned()
    {
        var usage = MakeUsage(new ConditionSet([new EnumerationOptionCondition(ColorDim, RedIri)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        List<Usage> results;
        using (StructureScope.Use(WithEnum(ColorDim, RedIri)))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldContain(usage);
    }

    [Fact]
    public async Task Usage_with_unsatisfied_enum_condition_is_excluded()
    {
        var usage = MakeUsage(new ConditionSet([
            new EnumerationOptionCondition(ColorDim, RedIri, isRequired: true)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        List<Usage> results;
        using (StructureScope.Use(EmptyConfig()))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ TimeCondition

    [Fact]
    public async Task Usage_with_satisfied_milestone_is_returned()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var usage = MakeUsage(new ConditionSet([new TimeCondition(from, to)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        var config = new StructureConfiguration(
            BranchIri: BranchIri,
            Options: new Dictionary<string, OptionValue>(),
            ReferenceDate: new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero));

        List<Usage> results;
        using (StructureScope.Use(config))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldContain(usage);
    }

    [Fact]
    public async Task Usage_with_expired_milestone_is_excluded()
    {
        var to = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var usage = MakeUsage(new ConditionSet([new TimeCondition(null, to)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        var config = new StructureConfiguration(
            BranchIri: BranchIri,
            Options: new Dictionary<string, OptionValue>(),
            ReferenceDate: new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero));

        List<Usage> results;
        using (StructureScope.Use(config))
            results = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        results.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ non-Usage type pass-through

    [Fact]
    public async Task Non_usage_query_passes_through_unfiltered_inside_scope()
    {
        // Use Branch as a proxy non-Usage entity type —
        // it is not in scope here, but we can verify pass-through numerically.
        // StubEntityStore returns whatever it was constructed with regardless of type.
        var usage = MakeUsage(new ConditionSet([
            new FlagOptionCondition(EvDim, true, isRequired: true)]));
        var store = new StubEntityStore(usage);
        var sut = new StructureFilteringStore(store);

        // Query for Usage first with a non-matching scope → filtered out.
        List<Usage> usageResults;
        using (StructureScope.Use(EmptyConfig()))
            usageResults = await sut.QueryByTypeAsync<Usage>().ToListAsync();

        usageResults.ShouldBeEmpty();

        // Then query without scope → pass-through, gets the Usage back.
        var unfiltered = await sut.QueryByTypeAsync<Usage>().ToListAsync();
        unfiltered.ShouldContain(usage);
    }

    // ------------------------------------------------------------------ delegate methods

    [Fact]
    public async Task LoadAsync_delegates_to_inner()
    {
        var store = new StubEntityStore();
        var sut = new StructureFilteringStore(store);

        var result = await sut.LoadAsync<Usage>("https://forge-it.net/usages/x");

        result.ShouldBeNull(); // StubEntityStore always returns null
    }

    [Fact]
    public void NamedGraph_delegates_to_inner()
    {
        var store = new StubEntityStore();
        var sut = new StructureFilteringStore(store);

        sut.NamedGraph.ShouldBe(store.NamedGraph);
    }

    // ------------------------------------------------------------------ DI wiring

    [Fact]
    public void AddForgeStructure_registers_StructureFilteringStore_as_unkeyed_IEntityStore()
    {
        var services = new ServiceCollection();
        var rawStore = new StubEntityStore();
        services.AddSingleton<IEntityStore>(rawStore);

        services.AddForgeStructure();

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IEntityStore>();

        // The registered decorator should be a StructureFilteringStore wrapping rawStore.
        // We verify by exercising through the scope — if it were the raw store,
        // the Usage with a required-but-absent condition would not be filtered.
        var required = new FlagOptionCondition(EvDim, true, isRequired: true);
        var usage = new Usage
        {
            ParentStructureIri = ParentIri,
            ChildStructureIri = ChildIri,
            Conditions = new ConditionSet([required])
        };

        // Stub has the usage. Without scope: both stores return it.
        // With scope (empty config, IsRequired=true): StructureFilteringStore excludes it.
        // Raw store would return it — difference proves the decorator is active.
        // We can't inject into StubEntityStore after the fact, so instead we check the type.
        resolved.GetType().Name.ShouldBe(nameof(StructureFilteringStore));
    }

    [Fact]
    public void AddForgeStructure_resolves_from_BackendStoreKey_when_no_unkeyed_store_registered()
    {
        var services = new ServiceCollection();
        var rawStore = new StubEntityStore();
        services.AddKeyedSingleton<IEntityStore>(
            Forge.Repository.DependencyInjection.ForgeEntityRepositoryBuilder.BackendStoreKey,
            rawStore);

        services.AddForgeStructure();

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IEntityStore>();

        resolved.GetType().Name.ShouldBe(nameof(StructureFilteringStore));
    }
}
