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
    string RaceEditionPackIri);         // present only when date ≤ 2025

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
            RaceEditionPackIri:    raceEdition.Iri));
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
}
