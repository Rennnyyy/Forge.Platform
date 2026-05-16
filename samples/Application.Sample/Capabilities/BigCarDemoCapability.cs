using System.Text;
using Forge.Capability;
using Forge.Execution;
using Forge.ObjectStorage;
using Forge.Repository;
using Forge.Structure;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Application.Sample;

// ── Command / Response ────────────────────────────────────────────────────────

/// <summary>
/// Populates a large-scale Demo Car platform family tree in one capability call.
/// <para>
/// The tree has <b>7 levels</b>, <b>6 000 leaf nodes</b>, and <b>4 000 unique
/// <see cref="Geometry3D"/> nodes</b> placed via <b>10 000</b>
/// <see cref="GeometryUsage3D"/> edges. All <see cref="Usage"/> edges are
/// unconditional (<c>ConditionSet.Empty</c>); the purpose of this sample is to
/// demonstrate structural <em>scale</em>, not condition complexity (which is the
/// responsibility of the small-car demo and Bruno chapter 22).
/// </para>
/// <para>
/// After populating, use <c>GET api/objects/geometry3d-nodes/bundle</c> to download
/// all 4 000 OBJ blobs as a single ZIP archive instead of triggering 4 000 individual
/// downloads. See sample ADR-0015 and ADR-0016.
/// </para>
/// </summary>
/// <remarks>
/// Route: <c>POST /api/capabilities/car/demo/big/populate</c>.
/// Calling this handler twice seeds a second independent tree (not idempotent).
/// </remarks>
public sealed record PopulateBigCarDemoCommand(
    string BranchIri = "https://forge-it.net/branches/main");

/// <summary>
/// Summary produced by <see cref="PopulateBigCarDemoHandler"/>.
/// See sample ADR-0015.
/// </summary>
public sealed record PopulateBigCarDemoResponse(
    string BranchIri,
    string RootIri,
    int TreeNodeCount,
    int LeafNodeCount,
    int TreeEdgeCount,
    int Geometry3dNodeCount,
    int Geometry3dUsageCount);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Builds a 7-level product-structure tree (7 576 nodes, 6 000 leaf components)
/// together with 4 000 standard-part <see cref="Geometry3D"/> nodes and 10 000
/// <see cref="GeometryUsage3D"/> placements demonstrating library-scale geometry reuse.
/// </summary>
[Capability("car.demo.big.populate")]
public sealed class PopulateBigCarDemoHandler
    : ICapabilityHandler<PopulateBigCarDemoCommand, PopulateBigCarDemoResponse>
{
    private readonly IEntityStore _store;
    private readonly IObjectStoreProvider _objectStoreProvider;

    public PopulateBigCarDemoHandler(
        IEntityStore store,
        IObjectStoreProvider objectStoreProvider)
    {
        _store = store;
        _objectStoreProvider = objectStoreProvider;
    }

    // ── Tree taxonomy constants ───────────────────────────────────────────────

    private static readonly string[] PlatformNames =
        ["Platform Alpha", "Platform Beta", "Platform Gamma"];

    private static readonly string[] SegmentTypes =
        ["Sedan", "SUV", "Coupe", "Pickup"];

    private static readonly string[] SubsystemNames =
        ["Structural", "Powertrain", "Electrical", "Interior", "Chassis"];

    private static readonly string[] ModuleLabels = ["A", "B", "C", "D"];

    // ── Standard-part OBJ shapes ──────────────────────────────────────────────
    // Eight minimal box-variant shapes (≈220 bytes each). Each geometry node in
    // the pool of 4 000 cycles through these 8 shapes for visual variety.

    private static readonly string[] PartShapeNames =
        ["box", "plate", "beam", "bracket", "rod", "housing", "small-cube", "post"];

    // (width, height, depth) dimensions for each shape variant.
    private static readonly (double W, double H, double D)[] PartDimensions =
    [
        (1.00, 1.00, 1.00),  // box
        (2.00, 0.10, 1.00),  // plate
        (0.20, 0.20, 2.00),  // beam
        (0.30, 0.50, 0.30),  // bracket
        (0.10, 0.10, 1.50),  // rod
        (1.00, 0.50, 1.00),  // housing
        (0.50, 0.50, 0.50),  // small-cube
        (0.20, 1.00, 0.20),  // post
    ];

    // Identity 4×4 matrix (column-major).
    private static readonly double[] Identity4x4 =
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    // Offset matrix for secondary placements: +0.5 on X so parts do not overlap.
    private static readonly double[] OffsetX4x4 =
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.5, 0, 0, 1];

    // ── Entry point ────────────────────────────────────────────────────────────

    public async ValueTask<ExecutionResult<PopulateBigCarDemoResponse>> HandleAsync(
        PopulateBigCarDemoCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        // ── Phase 1: geometry pool (4 000 standard-part nodes) ────────────────
        var geoPool = new Geometry3D[4_000];
        for (int gi = 0; gi < 4_000; gi++)
        {
            var (w, h, d) = PartDimensions[gi % 8];
            geoPool[gi] = await SaveGeometry3dAsync(
                $"Standard Part {gi:D4}",
                $"Standard {PartShapeNames[gi % 8]} part — library node {gi}.",
                MakeBoxOBJ(w, h, d),
                cancellationToken);
        }

        // ── Phase 2: structure tree ───────────────────────────────────────────
        // All nodes are collected in creation order so that each can be assigned
        // a geometry from the pool using nodeSequence % 4 000 (round-robin reuse).

        var root = await SaveNodeAsync(
            "Big Car Platform Family",
            "Root of the big-scale car platform family tree. 7 levels, 6 000 leaf nodes, " +
            "4 000 geometry shapes. All edges are unconditional. See sample ADR-0015.",
            cancellationToken);

        var allNodes = new List<Node> { root };
        int edgeCount = 0;

        // ── Level 1: platform groups (3 nodes) ───────────────────────────────
        var platforms = new Node[3];
        for (int p = 0; p < 3; p++)
        {
            platforms[p] = await SaveNodeAsync(
                PlatformNames[p], $"Vehicle platform group — {PlatformNames[p]}.", cancellationToken);
            await LinkAsync(root, platforms[p], cancellationToken);
            edgeCount++;
        }
        allNodes.AddRange(platforms);

        // ── Level 2: vehicle segments (3 × 4 = 12 nodes) ─────────────────────
        var segments = new Node[12];
        for (int p = 0; p < 3; p++)
        {
            for (int s = 0; s < 4; s++)
            {
                int si = p * 4 + s;
                segments[si] = await SaveNodeAsync(
                    $"{PlatformNames[p]} {SegmentTypes[s]}",
                    $"Vehicle segment — {SegmentTypes[s]} on {PlatformNames[p]}.",
                    cancellationToken);
                await LinkAsync(platforms[p], segments[si], cancellationToken);
                edgeCount++;
            }
        }
        allNodes.AddRange(segments);

        // ── Level 3: build configurations (12 × 5 = 60 nodes) ────────────────
        var configs = new Node[60];
        for (int s = 0; s < 12; s++)
        {
            for (int c = 0; c < 5; c++)
            {
                int ci = s * 5 + c;
                configs[ci] = await SaveNodeAsync(
                    $"{segments[s].Name} Config {c + 1:D2}",
                    $"Build configuration {c + 1} of {segments[s].Name}.",
                    cancellationToken);
                await LinkAsync(segments[s], configs[ci], cancellationToken);
                edgeCount++;
            }
        }
        allNodes.AddRange(configs);

        // ── Level 4: subsystem groups (60 × 5 = 300 nodes) ───────────────────
        var subsystems = new Node[300];
        for (int c = 0; c < 60; c++)
        {
            for (int ss = 0; ss < 5; ss++)
            {
                int si = c * 5 + ss;
                subsystems[si] = await SaveNodeAsync(
                    $"{configs[c].Name} – {SubsystemNames[ss]}",
                    $"Subsystem group — {SubsystemNames[ss]} for config {configs[c].Name}.",
                    cancellationToken);
                await LinkAsync(configs[c], subsystems[si], cancellationToken);
                edgeCount++;
            }
        }
        allNodes.AddRange(subsystems);

        // ── Level 5: module groups (300 × 4 = 1 200 nodes) ───────────────────
        var modules = new Node[1_200];
        for (int ss = 0; ss < 300; ss++)
        {
            for (int m = 0; m < 4; m++)
            {
                int mi = ss * 4 + m;
                modules[mi] = await SaveNodeAsync(
                    $"{subsystems[ss].Name} Module-{ModuleLabels[m]}",
                    $"Module group {ModuleLabels[m]} within {subsystems[ss].Name}.",
                    cancellationToken);
                await LinkAsync(subsystems[ss], modules[mi], cancellationToken);
                edgeCount++;
            }
        }
        allNodes.AddRange(modules);

        // ── Level 6: leaf components (1 200 × 5 = 6 000 nodes) ───────────────
        var leaves = new Node[6_000];
        for (int m = 0; m < 1_200; m++)
        {
            for (int comp = 0; comp < 5; comp++)
            {
                int li = m * 5 + comp;
                leaves[li] = await SaveNodeAsync(
                    $"{modules[m].Name} Component-{comp + 1:D2}",
                    $"Leaf component {comp + 1} of module {modules[m].Name}.",
                    cancellationToken);
                await LinkAsync(modules[m], leaves[li], cancellationToken);
                edgeCount++;
            }
        }
        allNodes.AddRange(leaves);

        // ── Phase 3: geometry usages ─────────────────────────────────────────
        // Primary: one geo per structure node (7 576 usages, round-robin pool).
        int geoUsageCount = 0;
        for (int ni = 0; ni < allNodes.Count; ni++)
        {
            await PlaceGeometry3dAsync(
                allNodes[ni].Iri, geoPool[ni % 4_000], Identity4x4, cancellationToken);
            geoUsageCount++;
        }

        // Secondary: 2 424 leaf nodes each get a second part offset by +0.5 on X.
        // This brings total usages to exactly 10 000 and exercises multi-placement reuse.
        const int SecondaryCount = 2_424;
        for (int li = 0; li < SecondaryCount; li++)
        {
            await PlaceGeometry3dAsync(
                leaves[li].Iri, geoPool[(li + 1_000) % 4_000], OffsetX4x4, cancellationToken);
            geoUsageCount++;
        }

        return new ExecutionResult<PopulateBigCarDemoResponse>.Ok(
            new PopulateBigCarDemoResponse(
                BranchIri: command.BranchIri,
                RootIri: root.Iri,
                TreeNodeCount: allNodes.Count,      // 7 576
                LeafNodeCount: leaves.Length,       // 6 000
                TreeEdgeCount: edgeCount,           // 7 575
                Geometry3dNodeCount: geoPool.Length,      // 4 000
                Geometry3dUsageCount: geoUsageCount));     // 10 000
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async ValueTask<Node> SaveNodeAsync(
        string name, string? description, CancellationToken cancellationToken)
    {
        var node = new Node { Name = name, Description = description };
        await _store.SaveAsync(node, cancellationToken: cancellationToken);
        return node;
    }

    private async ValueTask LinkAsync(
        Node parent, Node child, CancellationToken cancellationToken)
    {
        var usage = new Usage
        {
            ParentStructureIri = parent.Iri,
            ChildStructureIri = child.Iri,
            Conditions = ConditionSet.Empty,
        };
        await _store.SaveAsync(usage, cancellationToken: cancellationToken);
    }

    private async ValueTask<Geometry3D> SaveGeometry3dAsync(
        string name, string? description, string objContent,
        CancellationToken cancellationToken)
    {
        var geo = new Geometry3D { Name = name, Description = description };

        var objectStore = _objectStoreProvider.GetStore(Geometry3D.ForgeObjectStoreKey);
        var key = $"Geometry3D/{Guid.CreateVersion7()}";
        var bytes = Encoding.UTF8.GetBytes(objContent);
        await objectStore.UploadAsync(key, new MemoryStream(bytes), "text/plain", cancellationToken)
                         .ConfigureAwait(false);
        geo.ObjectKey = key;
        geo.ContentType = "text/plain";

        await _store.SaveAsync(geo, cancellationToken: cancellationToken);
        return geo;
    }

    private async ValueTask PlaceGeometry3dAsync(
        string parentIri, Geometry3D child, double[] matrix3d,
        CancellationToken cancellationToken)
    {
        var usage = new GeometryUsage3D
        {
            ParentIri = parentIri,
            ChildGeometry3dIri = child.Iri,
            Matrix3d = matrix3d,
        };
        await _store.SaveAsync(usage, cancellationToken: cancellationToken);
    }

    // ── OBJ generator ────────────────────────────────────────────────────────
    // Creates a minimal axis-aligned box with the given half-extents.
    // 8 vertices, 12 triangles (2 per face × 6 faces), ≈220 bytes.
    private static string MakeBoxOBJ(double w, double h, double d)
    {
        double hw = w / 2, hh = h / 2, hd = d / 2;
        var sb = new StringBuilder(256);
        // Vertices
        sb.AppendLine(F(-hw, -hh, -hd)); sb.AppendLine(F(hw, -hh, -hd));
        sb.AppendLine(F(hw, hh, -hd)); sb.AppendLine(F(-hw, hh, -hd));
        sb.AppendLine(F(-hw, -hh, hd)); sb.AppendLine(F(hw, -hh, hd));
        sb.AppendLine(F(hw, hh, hd)); sb.AppendLine(F(-hw, hh, hd));
        // Faces (1-indexed)
        sb.AppendLine("f 1 3 2"); sb.AppendLine("f 1 4 3");
        sb.AppendLine("f 5 6 7"); sb.AppendLine("f 5 7 8");
        sb.AppendLine("f 1 2 6"); sb.AppendLine("f 1 6 5");
        sb.AppendLine("f 2 3 7"); sb.AppendLine("f 2 7 6");
        sb.AppendLine("f 3 4 8"); sb.AppendLine("f 3 8 7");
        sb.AppendLine("f 4 1 5"); sb.AppendLine("f 4 5 8");
        return sb.ToString();

        static string F(double x, double y, double z) =>
            FormattableString.Invariant($"v {x:F4} {y:F4} {z:F4}");
    }
}
