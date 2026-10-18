using System.Buffers.Binary;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-032 window plans on synthetic fixtures: the window's own sectors from the module archive, its generated
/// sectors from terrain template inputs, and the owner's refusals of an inconsistent window.</summary>
[TestClass]
public sealed class AreaWindowTests
{
    private const long Own = 67108865; // sector (1, 1)
    private static readonly AreaInputKey Template = new(AreaSourceRole.TerrainTemplate);

    [TestMethod]
    public void A_window_loads_its_own_sectors_and_moves_a_template_into_a_generated_one()
    {
        // The template is its folder's sector (1, 1): its record lies at tile (70, 80), its light at (65, 66).
        var template = SectorWithLight(65, 66, Record(Id(0), location: 70L | (80L << 32)));
        var window = new AreaWindow([new(new(1, 1), AreaScope.Core, null), new(new(0, 0), AreaScope.Context, Template)]);
        using var f = new Fixture([(Own, [])], selection: AreaSectorSelection.Window, window: window, extraInputs: [(Template, template)],
            limits: new() { MaxTerrainTemplates = 1 }, resourceEntries: Resources());
        var snapshot = f.Owner.LoadStrict();

        Assert.AreEqual("arc-data-032-window-v1", snapshot.Profile);
        var own = snapshot.Sectors.Single(s => s.Coordinate == new AreaSectorCoordinate(1, 1));
        Assert.AreEqual((AreaScope.Core, true, (AreaInputKey?)null), (own.Scope, own.Present, own.Template));
        Assert.IsNotNull(own.DatOrdinal);
        var generated = snapshot.Sectors.Single(s => s.Coordinate == new AreaSectorCoordinate(0, 0));
        Assert.AreEqual((AreaScope.Context, true, (AreaInputKey?)Template, (int?)null), (generated.Scope, generated.Present, generated.Template, generated.DatOrdinal));
        Assert.AreEqual(Hash(template), generated.PayloadSha256);
        // Tile (70, 80) of the template slot is tile (6, 16) of sector (0, 0); the light moves the same way.
        var placed = snapshot.Objects.Single(o => o.Key.Sector == new AreaSectorCoordinate(0, 0).Packed);
        Assert.AreEqual((6L, 16L, AreaScope.Context), (placed.WorldX!.Value, placed.WorldY!.Value, placed.Scope));
        Assert.AreEqual((1, 2), (generated.Lights[0][2], generated.Lights[0][3]));
        Assert.AreEqual(2 * 4096, snapshot.Terrain.Count);
        Assert.AreEqual((1, 1, 0), (snapshot.Reconciliation.CoreSectors, snapshot.Reconciliation.ContextPresent, snapshot.Reconciliation.ContextMissing));
    }

    [TestMethod]
    public void The_owner_refuses_an_inconsistent_window()
    {
        var template = Sector();
        var window = new AreaWindow([new(new(1, 1), AreaScope.Core, null), new(new(0, 0), AreaScope.Context, Template)]);
        AreaErrorCode Refused(AreaSectorSelection selection, AreaWindow? plan, (AreaInputKey, byte[])[]? inputs, int templates = 1) =>
            Throws<AreaException>(() => new Fixture([(Own, [])], selection: selection, window: plan, extraInputs: inputs,
                limits: new() { MaxTerrainTemplates = templates }).Dispose()).Issue.Code;

        // A window without the window selection, the selection without a window, a template input without a window.
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.AllSectors, window, [(Template, template)]));
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.Window, null, null));
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.TownmapComponent, null, [(Template, template)]));
        // A window template that is no input, a template input the window does not use, no core sector, a repeated sector.
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.Window, window, null));
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.Window, new([new(new(1, 1), AreaScope.Core, null)]), [(Template, template)]));
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.Window, new([new(new(0, 0), AreaScope.Context, Template)]), [(Template, template)]));
        Assert.AreEqual(AreaErrorCode.InvalidSelection, Refused(AreaSectorSelection.Window,
            new([new(new(1, 1), AreaScope.Core, null), new(new(1, 1), AreaScope.Context, Template)]), [(Template, template)]));
        // More templates than the limit admits.
        Assert.AreEqual(AreaErrorCode.BudgetExceeded, Refused(AreaSectorSelection.Window, window, [(Template, template)], templates: 0));
    }

    [TestMethod]
    public void A_window_sector_the_module_does_not_hold_stops_the_load()
    {
        var window = new AreaWindow([new(new(1, 1), AreaScope.Core, null), new(new(2, 2), AreaScope.Core, null)]);
        using var f = new Fixture([(Own, [])], selection: AreaSectorSelection.Window, window: window);
        Assert.IsTrue(f.Owner.Inspect().Issues.Any(i => i.Code == AreaErrorCode.MissingSource && i.Stage == AreaStage.Selection));
    }

    /// <summary>The fixture's resources with tile names as the universal rules read them (a name ends at '/').</summary>
    private static (string, byte[])[] Resources() =>
        [("tile.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()), ("facade.mes", "{0}{fixture}"u8.ToArray()),
         ("scenery.mes", "{0}{fixture.art}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art())];

    private static byte[] SectorWithLight(int x, int y, params byte[][] records)
    {
        var tail = Sector(records).AsSpan(4).ToArray();
        var bytes = new byte[4 + 48 + tail.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + 8), x); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + 12), y);
        tail.CopyTo(bytes.AsSpan(52));
        return bytes;
    }
}
