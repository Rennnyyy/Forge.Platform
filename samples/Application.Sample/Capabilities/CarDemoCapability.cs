using Forge.Capability;
using Forge.Execution;
using Forge.Repository;
using Forge.Structure;

namespace Forge.Application.Sample;

// ── Well-known enumeration-value IRIs ─────────────────────────────────────────
// These are plain string identifiers — NOT entity IRIs. They are placed in
// EnumerationOptionCondition.EnumerationValueIri and passed back by callers in
// GetConfiguredTreeCommand.EnumerationOptions.
// Bruno scripts hard-code these constants directly; the PopulateCarDemoResponse
// echoes them so each test request can assert which values were populated.
internal static class CarDemoValueIris
{
    public const string DrivetrainEv  = "urn:forge:car:drivetrain/ev";
    public const string DrivetrainIce = "urn:forge:car:drivetrain/ice";

    public const string TrimBase    = "urn:forge:car:trim/base";
    public const string TrimSport   = "urn:forge:car:trim/sport";
    public const string TrimLuxury  = "urn:forge:car:trim/luxury";
}

// ── Time-era boundaries ──────────────────────────────────────────────────────
// "initial" era  : 2025-01-01 — 2025-12-31   (Race Edition Pack, no update-1 additions)
// "update-1" era : 2026-01-01 — open end     (Aluminium Frame, AWD, Turbocharger, …)
internal static class CarDemoEras
{
    public static readonly DateTimeOffset InitialEnd =
        new(2025, 12, 31, 23, 59, 59, TimeSpan.Zero);

    public static readonly DateTimeOffset UpdateOneStart =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

// ── Command / Response ────────────────────────────────────────────────────────

/// <summary>
/// Populates the Demo Car product-structure tree in one capability call.
/// <para>
/// The tree uses real <see cref="ConditionSet"/> entries to model two orthogonal
/// axes of variance:
/// <list type="table">
///   <item>
///     <term>Time effectivity (milestone eras)</term>
///     <description>
///       <see cref="TimeCondition"/> edges express the transition from the
///       <em>initial</em> era (2025) to <em>update-1</em> era (2026+).
///       The Race Edition Pack is exclusive to the initial era; the Aluminium Frame,
///       Thermal Management, Rear Motor, Turbocharger, transmission alternatives,
///       and Luxury Interior become available in the update-1 era.
///     </description>
///   </item>
///   <item>
///     <term>Feature variants</term>
///     <description>
///       <see cref="EnumerationOptionCondition"/> edges model mutually-exclusive
///       drivetrain choices (<c>ev</c> vs <c>ice</c>) and interior trim grades
///       (<c>base</c>, <c>sport</c>, <c>luxury</c>).
///       Both dimensions use <c>IsRequired = false</c> (open-world): omitting the
///       dimension yields the full 150&nbsp;% view for that axis.
///     </description>
///   </item>
/// </list>
/// Passing no options and a 2025 reference date returns 19 (initial-era 150&nbsp;%) nodes.
/// Passing no options and a 2026 reference date returns 26 (update-1-era 150&nbsp;%) nodes.
/// See sample ADR-0013.
/// </para>
/// </summary>
public sealed record PopulateCarDemoCommand(
    string BranchIri = "https://forge-it.net/branches/main");

/// <summary>
/// Contains the entity IRIs produced by <see cref="PopulateCarDemoHandler"/>.
/// <para>
/// For <c>structure.configured-tree.get</c> queries, callers must supply:
/// <list type="bullet">
///   <item><description><see cref="DrivetrainDimensionIri"/> as the key in <c>enumerationOptions</c>
///         with value <c>"urn:forge:car:drivetrain/ev"</c> or <c>"urn:forge:car:drivetrain/ice"</c>.
///   </description></item>
///   <item><description><see cref="TrimDimensionIri"/> as the key in <c>enumerationOptions</c>
///         with value <c>"urn:forge:car:trim/base"</c>, <c>"…/sport"</c>, or <c>"…/luxury"</c>.
///   </description></item>
///   <item><description><c>referenceDate</c> — use <c>2025-06-01T00:00:00Z</c> for initial era,
///         <c>2026-06-01T00:00:00Z</c> for update-1 era.
///   </description></item>
/// </list>
/// Omitting a dimension in the options dictionary activates open-world semantics:
/// all values for that axis are included in the result (150&nbsp;% for that dimension).
/// </para>
/// See sample ADR-0013.
/// </summary>
public sealed record PopulateCarDemoResponse(
    string BranchIri,
    string CarIri,
    // ── Dimension entity IRIs (use as enumerationOptions keys) ───────────────
    string DrivetrainDimensionIri,
    string TrimDimensionIri,
    // ── Landmark node IRIs for Bruno assertions ──────────────────────────────
    string EvPackageIri,
    string IcePackageIri,
    string AluminiumFrameIri,           // present only when date ≥ 2026
    string ThermalManagementIri,        // present only when date ≥ 2026
    string RearMotorIri,                // present only when date ≥ 2026
    string TurbochargerIri,             // present only when date ≥ 2026
    string LuxuryInteriorIri,           // present only when date ≥ 2026 AND trim=luxury
    string RaceEditionPackIri,          // present only when date ≤ 2025
    // ── Geometry layer summary ───────────────────────────────────────────────
    int GeometryNodeCount,              // total number of Geometry nodes seeded
    int GeometryUsageCount,             // total number of GeometryUsage edges seeded
    // ── 3D geometry layer summary ──────────────────────────────────
    int Geometry3dNodeCount,            // total number of Geometry3D nodes seeded
    int Geometry3dUsageCount);          // total number of GeometryUsage3D edges seeded

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Builds the complete Demo Car product-structure tree. 28 nodes, 27 Usage edges.
/// All condition types are exercised: <see cref="TimeCondition"/>,
/// <see cref="EnumerationOptionCondition"/>, and combined AND sets.
/// </summary>
/// <remarks>
/// Route: <c>POST /api/capabilities/car/demo/populate</c>.
/// Calling this handler twice seeds a second independent tree (not idempotent).
/// </remarks>
[Capability("car.demo.populate")]
public sealed class PopulateCarDemoHandler
    : ICapabilityHandler<PopulateCarDemoCommand, PopulateCarDemoResponse>
{
    private readonly IEntityStore _store;

    public PopulateCarDemoHandler(IEntityStore store) => _store = store;

    public async ValueTask<ExecutionResult<PopulateCarDemoResponse>> HandleAsync(
        PopulateCarDemoCommand command,
        CapabilityContext        context,
        CancellationToken        cancellationToken = default)
    {
        // ── ERA condition helpers (no captured locals) ────────────────────────
        // Edges only active from 2026 onward (update-1 additions)
        static ConditionSet FromUpdateOne() =>
            new([new TimeCondition(CarDemoEras.UpdateOneStart, null)]);

        // Edges only active until end of 2025 (initial-era exclusives, e.g. Race Edition)
        static ConditionSet UntilInitialEnd() =>
            new([new TimeCondition(null, CarDemoEras.InitialEnd)]);

        // ── Dimension entities ────────────────────────────────────────────────
        // Must be created before the variant condition helpers, which capture the Iri.
        var drivetrainDim = await SaveDimensionAsync(
            "Drivetrain",
            DimensionType.Enumeration,
            "Selects the powertrain: 'ev' for EV Package, 'ice' for ICE Package.",
            cancellationToken);

        var trimDim = await SaveDimensionAsync(
            "Interior Trim",
            DimensionType.Enumeration,
            "Selects the interior grade: 'base', 'sport', or 'luxury' (luxury requires update-1 era).",
            cancellationToken);

        // ── VARIANT condition helpers (capture dimension IRIs) ────────────────
        // Open-world (IsRequired=false): omitting the dimension key includes all values.
        ConditionSet Drivetrain(string valueIri) =>
            new([new EnumerationOptionCondition(drivetrainDim.Iri, valueIri)]);

        ConditionSet Trim(string valueIri) =>
            new([new EnumerationOptionCondition(trimDim.Iri, valueIri)]);

        // COMBINED AND: Luxury Interior requires luxury trim AND update-1 era.
        ConditionSet TrimLuxuryFromUpdateOne() =>
            new([
                new EnumerationOptionCondition(trimDim.Iri, CarDemoValueIris.TrimLuxury),
                new TimeCondition(CarDemoEras.UpdateOneStart, null),
            ]);

        // ── Car root ──────────────────────────────────────────────────────────
        var car = await SaveNodeAsync("Demo Car",
            "Root of the car demo product structure tree. Demonstrates TimeCondition + EnumerationOptionCondition. See sample ADR-0013.",
            cancellationToken);

        // ── Chassis ───────────────────────────────────────────────────────────
        var chassis     = await SaveNodeAsync("Chassis",              "Structural body.",                                                           cancellationToken);
        var steelFrame  = await SaveNodeAsync("Steel Frame",          "Standard steel monocoque frame (both eras).",                                cancellationToken);
        var alumFrame   = await SaveNodeAsync("Aluminium Space Frame","Lightweight multi-cell aluminium frame. Available from update-1 era only.", cancellationToken);

        await LinkAsync(car,      chassis,    ConditionSet.Empty,  cancellationToken);
        await LinkAsync(chassis,  steelFrame, ConditionSet.Empty,  cancellationToken);
        await LinkAsync(chassis,  alumFrame,  FromUpdateOne(),     cancellationToken); // ★ update-1+

        // ── Powertrain ────────────────────────────────────────────────────────
        var powertrain = await SaveNodeAsync("Powertrain",
            "Drivetrain assembly. Two structural alternatives: EV Package (drivetrain=ev) or ICE Package (drivetrain=ice).",
            cancellationToken);
        await LinkAsync(car, powertrain, ConditionSet.Empty, cancellationToken);

        // EV Package sub-tree
        var evPackage   = await SaveNodeAsync("EV Package",               "Electric drivetrain. Selected via drivetrain=ev.",                        cancellationToken);
        var battPack    = await SaveNodeAsync("Battery Pack",             "High-voltage traction battery pack.",                                    cancellationToken);
        var bms         = await SaveNodeAsync("Battery Management System","Per-cell voltage/temperature monitoring.",                               cancellationToken);
        var thermalMgmt = await SaveNodeAsync("Thermal Management",       "Active cooling/heating for battery. Available from update-1 era only.", cancellationToken);
        var eMachine    = await SaveNodeAsync("E-Machine",                "Electric motor assembly.",                                               cancellationToken);
        var frontMotor  = await SaveNodeAsync("Front Motor",              "Front-axle drive unit (both eras).",                                    cancellationToken);
        var rearMotor   = await SaveNodeAsync("Rear Motor",               "Rear-axle drive unit. Available from update-1 era only. Enables AWD.", cancellationToken);

        await LinkAsync(powertrain, evPackage,   Drivetrain(CarDemoValueIris.DrivetrainEv), cancellationToken); // ★ drivetrain=ev
        await LinkAsync(evPackage,  battPack,    ConditionSet.Empty,  cancellationToken);
        await LinkAsync(battPack,   bms,         ConditionSet.Empty,  cancellationToken);
        await LinkAsync(battPack,   thermalMgmt, FromUpdateOne(),     cancellationToken); // ★ update-1+
        await LinkAsync(evPackage,  eMachine,    ConditionSet.Empty,  cancellationToken);
        await LinkAsync(eMachine,   frontMotor,  ConditionSet.Empty,  cancellationToken);
        await LinkAsync(eMachine,   rearMotor,   FromUpdateOne(),     cancellationToken); // ★ update-1+

        // ICE Package sub-tree
        var icePackage  = await SaveNodeAsync("ICE Package",           "Internal combustion drivetrain. Selected via drivetrain=ice.",            cancellationToken);
        var combEngine  = await SaveNodeAsync("Combustion Engine",     "2.0 L petrol engine.",                                                    cancellationToken);
        var turbo       = await SaveNodeAsync("Turbocharger",          "Twin-scroll turbocharger. Available from update-1 era only.",             cancellationToken);
        var gearbox     = await SaveNodeAsync("Gearbox",               "Transmission assembly.",                                                  cancellationToken);
        var manClutch   = await SaveNodeAsync("Manual Clutch",         "6-speed manual clutch. Available from update-1 era only.",               cancellationToken);
        var autoTrans   = await SaveNodeAsync("Automatic Transmission","8-speed torque-converter auto. Available from update-1 era only.",       cancellationToken);

        await LinkAsync(powertrain, icePackage, Drivetrain(CarDemoValueIris.DrivetrainIce), cancellationToken); // ★ drivetrain=ice
        await LinkAsync(icePackage, combEngine, ConditionSet.Empty, cancellationToken);
        await LinkAsync(combEngine, turbo,      FromUpdateOne(),    cancellationToken); // ★ update-1+
        await LinkAsync(icePackage, gearbox,    ConditionSet.Empty, cancellationToken);
        await LinkAsync(gearbox,    manClutch,  FromUpdateOne(),    cancellationToken); // ★ update-1+
        await LinkAsync(gearbox,    autoTrans,  FromUpdateOne(),    cancellationToken); // ★ update-1+

        // ── Interior ──────────────────────────────────────────────────────────
        var interior     = await SaveNodeAsync("Interior",        "Cabin assembly. Three trim grades: base, sport, luxury (luxury: update-1 era only).", cancellationToken);
        var baseInterior = await SaveNodeAsync("Base Interior",   "Entry-level interior. Selected via trim=base.",                                       cancellationToken);
        var clothSeats   = await SaveNodeAsync("Cloth Seats",     "Standard cloth seat set.",                                                            cancellationToken);
        var sportInterior= await SaveNodeAsync("Sport Interior",  "Sport-tuned interior. Selected via trim=sport.",                                      cancellationToken);
        var sportSeats   = await SaveNodeAsync("Sport Seats",     "Bolstered bucket sport seats.",                                                       cancellationToken);
        var luxInterior  = await SaveNodeAsync("Luxury Interior", "Premium interior. Selected via trim=luxury. Available from update-1 era only.",        cancellationToken);
        var leatherSeats = await SaveNodeAsync("Leather Seats",   "Full-grain leather seat set.",                                                         cancellationToken);
        var panoramaRoof = await SaveNodeAsync("Panorama Roof",   "Full-width glass panorama roof.",                                                      cancellationToken);

        await LinkAsync(car,          interior,      ConditionSet.Empty,           cancellationToken);
        await LinkAsync(interior,     baseInterior,  Trim(CarDemoValueIris.TrimBase),  cancellationToken); // ★ trim=base
        await LinkAsync(baseInterior, clothSeats,    ConditionSet.Empty,           cancellationToken);
        await LinkAsync(interior,     sportInterior, Trim(CarDemoValueIris.TrimSport), cancellationToken); // ★ trim=sport
        await LinkAsync(sportInterior,sportSeats,    ConditionSet.Empty,           cancellationToken);
        await LinkAsync(interior,     luxInterior,   TrimLuxuryFromUpdateOne(),    cancellationToken); // ★ trim=luxury AND 2026+
        await LinkAsync(luxInterior,  leatherSeats,  ConditionSet.Empty,           cancellationToken);
        await LinkAsync(luxInterior,  panoramaRoof,  ConditionSet.Empty,           cancellationToken);

        // ── Race Edition (initial era exclusive) ──────────────────────────────
        var raceEdition = await SaveNodeAsync("Race Edition Pack","Track-focused option pack. Available during initial era only (discontinued after 2025).", cancellationToken);
        var rollCage    = await SaveNodeAsync("Roll Cage",        "FIA-approved roll cage. Sub-component of Race Edition Pack.",                            cancellationToken);

        await LinkAsync(car,         raceEdition, UntilInitialEnd(), cancellationToken); // ★ until 2025-12-31
        await LinkAsync(raceEdition, rollCage,    ConditionSet.Empty, cancellationToken);

        // ── Geometry layer ────────────────────────────────────────────────────
        // Geometry nodes hold 2D SVG fragments in local coordinate space.
        // GeometryUsage edges carry a 2D affine matrix [a,b,c,d,e,f] that positions
        // the child geometry in the parent's space (Structure→Geometry or Geometry→Geometry).
        // The assembled diagram uses viewBox="0 0 560 230".
        // Car body local space: 540×200, placed at absolute canvas offset (10,20).

        // ── SVG fragments (local coordinate space, stroke="currentColor") ────
        //   Car body outline — shared shape for steel and alum frames.
        //   Axle dashes at local x=90 (rear) and x=450 (front).
        const string SvgBodyBase =
            """<rect x="0" y="0" width="540" height="200" rx="30" fill="none" stroke="currentColor" stroke-width="3"/>"""
            + """<line x1="90" y1="4" x2="90" y2="196" stroke="currentColor" stroke-width="1" stroke-dasharray="6,4"/>"""
            + """<line x1="450" y1="4" x2="450" y2="196" stroke="currentColor" stroke-width="1" stroke-dasharray="6,4"/>""";

        const string SvgBodyAlum =
            """<rect x="0" y="0" width="540" height="200" rx="30" fill="none" stroke="currentColor" stroke-width="3" stroke-dasharray="14,4"/>"""
            + """<line x1="90" y1="4" x2="90" y2="196" stroke="currentColor" stroke-width="1" stroke-dasharray="6,4"/>"""
            + """<line x1="450" y1="4" x2="450" y2="196" stroke="currentColor" stroke-width="1" stroke-dasharray="6,4"/>""";

        //   Wheel pair (top-down): two ellipses + axle shaft, local 60×200.
        //   Placed relative to car body at [1,0,0,1,420,-18] (front) or [1,0,0,1,60,-18] (rear).
        const string SvgWheels =
            """<ellipse cx="30" cy="20" rx="28" ry="18" fill="none" stroke="currentColor" stroke-width="2"/>"""
            + """<ellipse cx="30" cy="20" rx="12" ry="8" fill="none" stroke="currentColor" stroke-width="1"/>"""
            + """<line x1="30" y1="38" x2="30" y2="162" stroke="currentColor" stroke-width="2.5"/>"""
            + """<ellipse cx="30" cy="180" rx="28" ry="18" fill="none" stroke="currentColor" stroke-width="2"/>"""
            + """<ellipse cx="30" cy="180" rx="12" ry="8" fill="none" stroke="currentColor" stroke-width="1"/>""";

        //   Battery pack (floor-mounted, EV), local 240×90.
        const string SvgBattery =
            """<rect x="0" y="0" width="240" height="90" rx="6" fill="none" stroke="currentColor" stroke-width="2"/>"""
            + """<line x1="48" y1="0" x2="48" y2="90" stroke="currentColor" stroke-width="1"/>"""
            + """<line x1="96" y1="0" x2="96" y2="90" stroke="currentColor" stroke-width="1"/>"""
            + """<line x1="144" y1="0" x2="144" y2="90" stroke="currentColor" stroke-width="1"/>"""
            + """<line x1="192" y1="0" x2="192" y2="90" stroke="currentColor" stroke-width="1"/>"""
            + """<line x1="0" y1="45" x2="240" y2="45" stroke="currentColor" stroke-width="1"/>""";

        //   Inline-4 combustion engine (top view), local 80×140.
        const string SvgEngine =
            """<rect x="0" y="0" width="80" height="140" rx="5" fill="none" stroke="currentColor" stroke-width="2"/>"""
            + """<rect x="10" y="10" width="60" height="24" rx="3" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="10" y="42" width="60" height="24" rx="3" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="10" y="74" width="60" height="24" rx="3" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="10" y="106" width="60" height="24" rx="3" fill="none" stroke="currentColor" stroke-width="1.5"/>""";

        //   Passenger cabin with four seat outlines, local 240×190.
        const string SvgCabin =
            """<rect x="0" y="0" width="240" height="190" rx="5" fill="none" stroke="currentColor" stroke-width="1.5" stroke-dasharray="8,4"/>"""
            + """<rect x="12" y="15" width="40" height="55" rx="8" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="12" y="80" width="40" height="55" rx="8" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="105" y="10" width="125" height="80" rx="8" fill="none" stroke="currentColor" stroke-width="1.5"/>"""
            + """<rect x="105" y="100" width="125" height="80" rx="8" fill="none" stroke="currentColor" stroke-width="1.5"/>""";

        //   FIA roll cage: outer rect + X-brace + vertical pillars, local 240×190.
        const string SvgRollCage =
            """<rect x="0" y="0" width="240" height="190" rx="5" fill="none" stroke="currentColor" stroke-width="2"/>"""
            + """<line x1="0" y1="0" x2="240" y2="190" stroke="currentColor" stroke-width="1.5"/>"""
            + """<line x1="240" y1="0" x2="0" y2="190" stroke="currentColor" stroke-width="1.5"/>"""
            + """<line x1="60" y1="0" x2="60" y2="190" stroke="currentColor" stroke-width="2"/>"""
            + """<line x1="180" y1="0" x2="180" y2="190" stroke="currentColor" stroke-width="2"/>""";

        // ── Geometry nodes ────────────────────────────────────────────────────
        var geoSteelBody = await SaveGeometryAsync(
            "Car Body Outline (Steel)",
            "Top-down silhouette of the steel monocoque car body. Axle dashes at local x=90/450.",
            SvgBodyBase, cancellationToken);

        var geoAlumBody = await SaveGeometryAsync(
            "Car Body Outline (Aluminium)",
            "Top-down silhouette of the aluminium space-frame car body. Outer stroke dashed.",
            SvgBodyAlum, cancellationToken);

        // Wheel pairs — four separate entities, same SVG, different G→G placements.
        var geoFWheelsSt = await SaveGeometryAsync(
            "Front Wheel Pair (Steel)",
            "Front axle wheel pair sub-geometry, relative to the steel car body outline.",
            SvgWheels, cancellationToken);
        var geoRWheelsSt = await SaveGeometryAsync(
            "Rear Wheel Pair (Steel)",
            "Rear axle wheel pair sub-geometry, relative to the steel car body outline.",
            SvgWheels, cancellationToken);
        var geoFWheelsAl = await SaveGeometryAsync(
            "Front Wheel Pair (Aluminium)",
            "Front axle wheel pair sub-geometry, relative to the aluminium car body outline.",
            SvgWheels, cancellationToken);
        var geoRWheelsAl = await SaveGeometryAsync(
            "Rear Wheel Pair (Aluminium)",
            "Rear axle wheel pair sub-geometry, relative to the aluminium car body outline.",
            SvgWheels, cancellationToken);

        var geoBattery = await SaveGeometryAsync(
            "Floor Battery Pack",
            "Top-down view of the EV floor-mounted high-voltage battery. Placed in car centre.",
            SvgBattery, cancellationToken);

        var geoEngine = await SaveGeometryAsync(
            "Combustion Engine Block",
            "Top-down view of the inline-4 ICE engine block. Placed in the front engine bay.",
            SvgEngine, cancellationToken);

        var geoCabin = await SaveGeometryAsync(
            "Passenger Cabin",
            "Top-down seat layout of the passenger cabin. Dashed border = cabin boundary.",
            SvgCabin, cancellationToken);

        var geoRollCage = await SaveGeometryAsync(
            "Roll Cage Frame",
            "FIA roll cage: outer rect + X-brace + main hoop pillars over the cabin area.",
            SvgRollCage, cancellationToken);

        // ── GeometryUsage edges ───────────────────────────────────────────────
        // S→G: structure node → geometry node (absolute canvas placement).
        await PlaceGeometryAsync(steelFrame.Iri, geoSteelBody,  [1, 0, 0, 1,  10, 20], cancellationToken);
        await PlaceGeometryAsync(alumFrame.Iri,  geoAlumBody,   [1, 0, 0, 1,  10, 20], cancellationToken);
        await PlaceGeometryAsync(battPack.Iri,   geoBattery,    [1, 0, 0, 1, 150, 70], cancellationToken);
        await PlaceGeometryAsync(combEngine.Iri, geoEngine,     [1, 0, 0, 1, 455, 30], cancellationToken);
        await PlaceGeometryAsync(interior.Iri,   geoCabin,      [1, 0, 0, 1, 135, 15], cancellationToken);
        await PlaceGeometryAsync(rollCage.Iri,   geoRollCage,   [1, 0, 0, 1, 135, 15], cancellationToken);

        // G→G: geometry node → sub-geometry (placement relative to parent geometry local space).
        // Front axle: wheel center aligns with car body local x=450 → place at [1,0,0,1,420,-18].
        // Rear axle:  wheel center aligns with car body local x=90  → place at [1,0,0,1,60,-18].
        await PlaceGeometryAsync(geoSteelBody.Iri, geoFWheelsSt, [1, 0, 0, 1, 420, -18], cancellationToken);
        await PlaceGeometryAsync(geoSteelBody.Iri, geoRWheelsSt, [1, 0, 0, 1,  60, -18], cancellationToken);
        await PlaceGeometryAsync(geoAlumBody.Iri,  geoFWheelsAl, [1, 0, 0, 1, 420, -18], cancellationToken);
        await PlaceGeometryAsync(geoAlumBody.Iri,  geoRWheelsAl, [1, 0, 0, 1,  60, -18], cancellationToken);
        // ── Geometry3D layer ───────────────────────────────────────────────
        // 3 geometry3d nodes: shared shapes (car body, wheel disc, battery)
        var geo3dBody = await SaveGeometry3dAsync(
            "Car Body 3D",
            "Simplified car body: box 3.0×0.55×1.2 m (X=length, Y=height, Z=width).",
            ObjCarBody, cancellationToken);
        var geo3dWheel = await SaveGeometry3dAsync(
            "Wheel Disc 3D",
            "Wheel: short cylinder r=0.35 h=0.28 m, axis along Z, 8 segments.",
            ObjWheelDisc, cancellationToken);
        var geo3dBattery = await SaveGeometry3dAsync(
            "Battery Pack 3D",
            "EV battery pack: box 1.2×0.15×0.8 m, embedded in the car floor.",
            ObjBatteryPack, cancellationToken);
        var geo3dAlumBody = await SaveGeometry3dAsync(
            "Aluminium Chassis Body 3D",
            "Update-1 era aluminium space-frame body: 3.2×0.50×1.25 m (longer, lower, wider than steel).",
            ObjAlumBody, cancellationToken);

        // 6 geometry3d usage edges
        // S→G3D: steelFrame → carBody3d  (identity — body centred on frame)
        await PlaceGeometry3dAsync(steelFrame.Iri, geo3dBody,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1], cancellationToken);
        // G3D→G3D: body → four wheel instances (translation only; cylinder axis = Z = wheel axis)
        await PlaceGeometry3dAsync(geo3dBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, -1.1,-0.2,-0.70,1], cancellationToken); // FL
        await PlaceGeometry3dAsync(geo3dBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, -1.1,-0.2, 0.70,1], cancellationToken); // FR
        await PlaceGeometry3dAsync(geo3dBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0,  1.1,-0.2,-0.70,1], cancellationToken); // RL
        await PlaceGeometry3dAsync(geo3dBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0,  1.1,-0.2, 0.70,1], cancellationToken); // RR
        // G3D→G3D: body → battery pack (embedded in the floor, slightly rear-biased)
        await PlaceGeometry3dAsync(geo3dBody.Iri, geo3dBattery,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0.3,0.0,0.0,1], cancellationToken);
        // S→G3D: alumFrame → alum body (update-1 era only — appears when alumFrame is reachable)
        await PlaceGeometry3dAsync(alumFrame.Iri, geo3dAlumBody,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1], cancellationToken);
        // G3D→G3D: alum body → four wheels (wider track: ±0.75 vs steel ±0.70)
        await PlaceGeometry3dAsync(geo3dAlumBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, -1.2,-0.2,-0.75,1], cancellationToken); // FL
        await PlaceGeometry3dAsync(geo3dAlumBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, -1.2,-0.2, 0.75,1], cancellationToken); // FR
        await PlaceGeometry3dAsync(geo3dAlumBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0,  1.2,-0.2,-0.75,1], cancellationToken); // RL
        await PlaceGeometry3dAsync(geo3dAlumBody.Iri, geo3dWheel,
            [1,0,0,0, 0,1,0,0, 0,0,1,0,  1.2,-0.2, 0.75,1], cancellationToken); // RR
        // S→G3D: combEngine → exhaust pipe (ICE only — appears when drivetrain=ice)
        var geo3dExhaust = await SaveGeometry3dAsync(
            "Exhaust Pipe 3D",
            "Single right-side rear exhaust: rectangular main run (0.05×0.04 m, 1.28 m long) with a flared exit tip. Only visible on ICE drivetrain.",
            ObjExhaustPipe, cancellationToken);
        await PlaceGeometry3dAsync(combEngine.Iri, geo3dExhaust,
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1], cancellationToken);
        // ── Return ────────────────────────────────────────────────────────────
        return new ExecutionResult<PopulateCarDemoResponse>.Ok(new PopulateCarDemoResponse(
            BranchIri:             command.BranchIri,
            CarIri:                car.Iri,
            DrivetrainDimensionIri: drivetrainDim.Iri,
            TrimDimensionIri:      trimDim.Iri,
            EvPackageIri:          evPackage.Iri,
            IcePackageIri:         icePackage.Iri,
            AluminiumFrameIri:     alumFrame.Iri,
            ThermalManagementIri:  thermalMgmt.Iri,
            RearMotorIri:          rearMotor.Iri,
            TurbochargerIri:       turbo.Iri,
            LuxuryInteriorIri:     luxInterior.Iri,
            RaceEditionPackIri:    raceEdition.Iri,
            GeometryNodeCount:     10,
            GeometryUsageCount:    10,
            Geometry3dNodeCount:   5,
            Geometry3dUsageCount:  12));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async ValueTask<Node> SaveNodeAsync(string name, string? description,
        CancellationToken cancellationToken)
    {
        var node = new Node { Name = name, Description = description };
        await _store.SaveAsync(node, cancellationToken: cancellationToken);
        return node;
    }

    private async ValueTask<Dimension> SaveDimensionAsync(string name,
        DimensionType type, string? description, CancellationToken cancellationToken)
    {
        var dim = new Dimension { Name = name, Type = type, Description = description };
        await _store.SaveAsync(dim, cancellationToken: cancellationToken);
        return dim;
    }

    private async ValueTask LinkAsync(Node parent, Node child,
        ConditionSet conditions, CancellationToken cancellationToken)
    {
        var usage = new Usage
        {
            ParentStructureIri = parent.Iri,
            ChildStructureIri  = child.Iri,
            Conditions         = conditions,
        };
        await _store.SaveAsync(usage, cancellationToken: cancellationToken);
    }

    private async ValueTask<Geometry> SaveGeometryAsync(
        string name, string? description, string? svgContent,
        CancellationToken cancellationToken)
    {
        var geo = new Geometry { Name = name, Description = description, SvgContent = svgContent };
        await _store.SaveAsync(geo, cancellationToken: cancellationToken);
        return geo;
    }

    private async ValueTask PlaceGeometryAsync(
        string parentIri, Geometry child, double[] matrix,
        CancellationToken cancellationToken)
    {
        var usage = new GeometryUsage
        {
            ParentIri        = parentIri,
            ChildGeometryIri = child.Iri,
            Matrix           = matrix,
        };
        await _store.SaveAsync(usage, cancellationToken: cancellationToken);
    }

    private async ValueTask<Geometry3D> SaveGeometry3dAsync(
        string name, string? description, string? objContent,
        CancellationToken cancellationToken)
    {
        var geo = new Geometry3D { Name = name, Description = description, ObjContent = objContent };
        await _store.SaveAsync(geo, cancellationToken: cancellationToken);
        return geo;
    }

    private async ValueTask PlaceGeometry3dAsync(
        string parentIri, Geometry3D child, double[] matrix3d,
        CancellationToken cancellationToken)
    {
        var usage = new GeometryUsage3D
        {
            ParentIri           = parentIri,
            ChildGeometry3dIri  = child.Iri,
            Matrix3d            = matrix3d,
        };
        await _store.SaveAsync(usage, cancellationToken: cancellationToken);
    }

    // ── OBJ shape constants (Wavefront OBJ — plain ASCII, text-editable) ─────────────────
    // Convention: centred at local origin, X=length, Y=height, Z=width.

    private const string ObjCarBody = """
        # Forge 3D — car body (3.0 × 0.55 × 1.2 m, centred at origin)
        # Vertices: bottom ring then top ring
        v -1.500 -0.275 -0.600
        v  1.500 -0.275 -0.600
        v  1.500 -0.275  0.600
        v -1.500 -0.275  0.600
        v -1.500  0.275 -0.600
        v  1.500  0.275 -0.600
        v  1.500  0.275  0.600
        v -1.500  0.275  0.600
        # Faces (quads, outward normals)
        f 1 4 3 2
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;

    private const string ObjWheelDisc = """
        # Forge 3D — wheel disc (r=0.35 h=0.28 m, axis=Z, 8 segments)
        # Bottom ring (z = -0.140)
        v  0.350  0.000 -0.140
        v  0.247  0.247 -0.140
        v  0.000  0.350 -0.140
        v -0.247  0.247 -0.140
        v -0.350  0.000 -0.140
        v -0.247 -0.247 -0.140
        v  0.000 -0.350 -0.140
        v  0.247 -0.247 -0.140
        # Top ring (z = +0.140)
        v  0.350  0.000  0.140
        v  0.247  0.247  0.140
        v  0.000  0.350  0.140
        v -0.247  0.247  0.140
        v -0.350  0.000  0.140
        v -0.247 -0.247  0.140
        v  0.000 -0.350  0.140
        v  0.247 -0.247  0.140
        # Centres
        v  0.000  0.000 -0.140
        v  0.000  0.000  0.140
        # Side quads (b0 b1 t1 t0)
        f  2  1  9 10
        f  3  2 10 11
        f  4  3 11 12
        f  5  4 12 13
        f  6  5 13 14
        f  7  6 14 15
        f  8  7 15 16
        f  1  8 16  9
        # Bottom cap (fan from vertex 17)
        f 17  1  2
        f 17  2  3
        f 17  3  4
        f 17  4  5
        f 17  5  6
        f 17  6  7
        f 17  7  8
        f 17  8  1
        # Top cap (fan from vertex 18)
        f 18 10  9
        f 18 11 10
        f 18 12 11
        f 18 13 12
        f 18 14 13
        f 18 15 14
        f 18 16 15
        f 18  9 16
        """;

    private const string ObjBatteryPack = """
        # Forge 3D — EV battery pack (1.2 × 0.15 × 0.8 m, centred at origin)
        v -0.600 -0.075 -0.400
        v  0.600 -0.075 -0.400
        v  0.600 -0.075  0.400
        v -0.600 -0.075  0.400
        v -0.600  0.075 -0.400
        v  0.600  0.075 -0.400
        v  0.600  0.075  0.400
        v -0.600  0.075  0.400
        f 1 4 3 2
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;

    private const string ObjExhaustPipe = """
        # Forge 3D — ICE single right-side exhaust pipe, rear exit
        # Main pipe: 0.05 × 0.04 m cross-section, runs X [+0.20, +1.48] under right body sill
        # Exit flare: slightly wider (0.07 × 0.06 m), extends to X+1.65 past rear bumper
        # Coords: X = car length (rear = +1.5), Y = up, Z = width (right side = +Z)
        v  0.200 -0.225  0.520
        v  1.480 -0.225  0.520
        v  1.480 -0.225  0.570
        v  0.200 -0.225  0.570
        v  0.200 -0.265  0.520
        v  1.480 -0.265  0.520
        v  1.480 -0.265  0.570
        v  0.200 -0.265  0.570
        f 1 2 3 4
        f 8 7 6 5
        f 1 5 6 2
        f 2 6 7 3
        f 3 7 8 4
        f 4 8 5 1
        v  1.480 -0.215  0.510
        v  1.650 -0.215  0.510
        v  1.650 -0.215  0.580
        v  1.480 -0.215  0.580
        v  1.480 -0.275  0.510
        v  1.650 -0.275  0.510
        v  1.650 -0.275  0.580
        v  1.480 -0.275  0.580
        f  9 10 11 12
        f 16 15 14 13
        f  9 13 14 10
        f 10 14 15 11
        f 11 15 16 12
        f 12 16 13  9
        """;

    private const string ObjAlumBody = """
        # Forge 3D — aluminium chassis body (3.2 × 0.50 × 1.25 m, centred at origin)
        # Longer, lower and wider than the steel body — visible when update-1 era is active.
        v -1.600 -0.250 -0.625
        v  1.600 -0.250 -0.625
        v  1.600 -0.250  0.625
        v -1.600 -0.250  0.625
        v -1.600  0.250 -0.625
        v  1.600  0.250 -0.625
        v  1.600  0.250  0.625
        v -1.600  0.250  0.625
        f 1 4 3 2
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;
}
