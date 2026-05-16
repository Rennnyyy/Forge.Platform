using Shouldly;
using Forge.Execution;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for <see cref="GetConfiguredTreeHandler"/>:
/// DFS traversal, cycle detection, and snapshot-branch annotation (Structure ADR-0006).
/// </summary>
public sealed class GetConfiguredTreeHandlerTests
{
    private const string MainBranch = "https://forge-it.net/branches/main";
    private const string SnapshotV1 = "https://forge-it.net/branches/geometry-v1";
    private const string SnapshotV2 = "https://forge-it.net/branches/geometry-v2";

    private const string Root = "https://forge-it.net/s/root";
    private const string Child = "https://forge-it.net/s/child";
    private const string Grandchild = "https://forge-it.net/s/grandchild";

    private static GetConfiguredTreeCommand Cmd(string headIri) =>
        new(headIri, MainBranch);

    private static async Task<GetConfiguredTreeResponse> RunAsync(
        string headIri, params Usage[] usages)
    {
        var store = new StubEntityStore(usages.Cast<object>().ToArray());
        var handler = new GetConfiguredTreeHandler(store);
        var result = await handler.HandleAsync(Cmd(headIri), null!, CancellationToken.None);
        return result.ShouldBeOfType<ExecutionResult<GetConfiguredTreeResponse>.Ok>().Response;
    }

    // ─── Basic traversal ──────────────────────────────────────────────────────

    [Fact]
    public async Task Root_with_no_children_returns_single_node()
    {
        var response = await RunAsync(Root);

        response.Root.Iri.ShouldBe(Root);
        response.Root.Children.ShouldBeEmpty();
        response.AllNodeIris.ShouldContain(Root);
    }

    [Fact]
    public async Task Single_edge_produces_root_with_one_child()
    {
        var usage = new Usage { ParentStructureIri = Root, ChildStructureIri = Child };
        var response = await RunAsync(Root, usage);

        response.Root.Children.Count.ShouldBe(1);
        response.Root.Children[0].Iri.ShouldBe(Child);
        response.AllNodeIris.ShouldContain(Root);
        response.AllNodeIris.ShouldContain(Child);
    }

    // ─── Root has no snapshot branch ─────────────────────────────────────────

    [Fact]
    public async Task Root_node_has_null_SnapshotBranchIri()
    {
        var usage = new Usage { ParentStructureIri = Root, ChildStructureIri = Child };
        var response = await RunAsync(Root, usage);

        response.Root.SnapshotBranchIri.ShouldBeNull();
    }

    // ─── Snapshot annotation on the incoming edge ─────────────────────────────

    [Fact]
    public async Task Child_connected_via_snapshot_usage_gets_SnapshotBranchIri()
    {
        var usage = new Usage
        {
            ParentStructureIri = Root,
            ChildStructureIri = Child,
            SnapshotIri = SnapshotV1,
        };
        var response = await RunAsync(Root, usage);

        var childDto = response.Root.Children.Single();
        childDto.Iri.ShouldBe(Child);
        childDto.SnapshotBranchIri.ShouldBe(SnapshotV1);
    }

    [Fact]
    public async Task Node_without_snapshot_usage_has_null_SnapshotBranchIri()
    {
        var usage = new Usage { ParentStructureIri = Root, ChildStructureIri = Child };
        var response = await RunAsync(Root, usage);

        response.Root.Children.Single().SnapshotBranchIri.ShouldBeNull();
    }

    // ─── Inheritance: snapshot propagates to descendants ─────────────────────

    [Fact]
    public async Task SnapshotBranchIri_propagates_to_grandchild_when_no_override()
    {
        // root →[snapshot=V1] child →[no snapshot] grandchild
        var u1 = new Usage
        {
            ParentStructureIri = Root,
            ChildStructureIri = Child,
            SnapshotIri = SnapshotV1,
        };
        var u2 = new Usage { ParentStructureIri = Child, ChildStructureIri = Grandchild };

        var response = await RunAsync(Root, u1, u2);

        var childDto = response.Root.Children.Single();
        var grandchildDto = childDto.Children.Single();

        childDto.SnapshotBranchIri.ShouldBe(SnapshotV1);
        grandchildDto.SnapshotBranchIri.ShouldBe(SnapshotV1); // inherited
    }

    // ─── Override: child edge overrides inherited snapshot ───────────────────

    [Fact]
    public async Task Child_edge_SnapshotIri_overrides_inherited_snapshot()
    {
        // root →[snapshot=V1] child →[snapshot=V2] grandchild
        var u1 = new Usage
        {
            ParentStructureIri = Root,
            ChildStructureIri = Child,
            SnapshotIri = SnapshotV1,
        };
        var u2 = new Usage
        {
            ParentStructureIri = Child,
            ChildStructureIri = Grandchild,
            SnapshotIri = SnapshotV2,
        };

        var response = await RunAsync(Root, u1, u2);

        var childDto = response.Root.Children.Single();
        var grandchildDto = childDto.Children.Single();

        childDto.SnapshotBranchIri.ShouldBe(SnapshotV1);
        grandchildDto.SnapshotBranchIri.ShouldBe(SnapshotV2); // explicit override
    }

    // ─── Cycle detection preserves snapshot annotation ────────────────────────

    [Fact]
    public async Task Cycle_sentinel_carries_inherited_SnapshotBranchIri()
    {
        // root →[snapshot=V1] child →[no snapshot] root (cycle)
        var u1 = new Usage
        {
            ParentStructureIri = Root,
            ChildStructureIri = Child,
            SnapshotIri = SnapshotV1,
        };
        var u2 = new Usage { ParentStructureIri = Child, ChildStructureIri = Root };

        var response = await RunAsync(Root, u1, u2);

        // root → child (V1) → root (cycle-sentinel, inherits V1)
        var childDto = response.Root.Children.Single();
        var cycleSentinel = childDto.Children.Single();

        childDto.SnapshotBranchIri.ShouldBe(SnapshotV1);
        cycleSentinel.Iri.ShouldBe(Root);
        cycleSentinel.Children.ShouldBeEmpty(); // sentinel has no children
        cycleSentinel.SnapshotBranchIri.ShouldBe(SnapshotV1); // inherits from path
    }

    // ─── AllNodeIris completeness ─────────────────────────────────────────────

    [Fact]
    public async Task AllNodeIris_contains_every_reachable_node_exactly_once()
    {
        var u1 = new Usage { ParentStructureIri = Root, ChildStructureIri = Child };
        var u2 = new Usage { ParentStructureIri = Child, ChildStructureIri = Grandchild };

        var response = await RunAsync(Root, u1, u2);

        response.AllNodeIris.Count.ShouldBe(3);
        response.AllNodeIris.ShouldContain(Root);
        response.AllNodeIris.ShouldContain(Child);
        response.AllNodeIris.ShouldContain(Grandchild);
    }
}
