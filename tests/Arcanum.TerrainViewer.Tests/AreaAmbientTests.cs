using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-RENDER-010. Literal ART ids and hand-computed hours; no production helper supplies expectations.
[TestClass]
public sealed class AreaAmbientTests
{
    [TestMethod]
    public void Channel_follows_art_type_and_tile_or_facade_type_bit()
    {
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0x00000000));   // tile, type bit 0
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0x00000100));  // tile, type bit 1
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0x000000C1));   // tile flip bits only
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0xB0000000));   // facade, type bit 0
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0xB2000000));  // facade, type bit 25
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0xB0000100));   // tile bit is not the facade bit
        foreach (var type in new uint[] { 1, 2, 3, 4, 7, 10, 12, 13 })
            Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(type << 28), $"type {type}");
        foreach (var type in new uint[] { 5, 8, 9, 14, 15 })
            Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(type << 28), $"type {type}");
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0x60000000));  // item, ground disposition
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(0x60001000));    // item, inventory
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(0x60003000));    // item, schematic
    }

    [TestMethod]
    public void Plan_channels_cover_every_terrain_and_body_command()
    {
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var plan = AreaScenePlan.Create([], [body], [visual]);
        // The composition fixture body ART is scenery (0x4...): outdoor.
        var channels = AreaAmbientPolicy.Channels(plan, [], [visual]);
        Assert.AreEqual(1, channels.Count);
        Assert.AreEqual(AreaAmbientChannel.Outdoor, channels[visual.Key]);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaAmbientPolicy.Channels(plan, [], []));
    }

    private static string Rows(Func<int, string> row) => string.Concat(Enumerable.Range(0, 24).Select(h => $"{{{h}}}{{{row(h)}}}\n"));
    private static LightSchemeTable Table(int id, Func<int, string> row)
    {
        Assert.AreEqual(LightingFailure.None, LightSchemeParser.TryParse(id, Encoding.ASCII.GetBytes(Rows(row)), out var table));
        return table!;
    }
    private static readonly string Sha = new('b', 64);
    private static readonly long West = new AreaSectorCoordinate(1451, 1290).Packed, East = new AreaSectorCoordinate(1452, 1290).Packed;

    private static AreaLightingSource Source(int startHour) => new(Sha, 5000, 1, Sha,
        new Dictionary<long, int> { [West] = 0, [East] = 3 },
        new Dictionary<int, LightSchemeTable> { [1] = Table(1, h => $"{h},{h},{h},{100 + h},0,0"), [3] = Table(3, h => $"0,{h},0,0,{200 + h},0") },
        new StartupClockDeclaration(1885, startHour, Sha));

    [TestMethod]
    public void Driver_starts_at_declared_hour_and_applies_whole_hours_and_sector_changes()
    {
        using var driver = AreaAmbientDriver.Create(Source(15), West);
        Assert.AreEqual(54_000_000, driver.Clock.GameTimestampMs);
        Assert.AreEqual((15, true), (driver.Clock.Hour, driver.Clock.IsDay));
        Assert.AreEqual(new AmbientSelection(1, true, West, 15, new(new(15, 15, 15), new(115, 0, 0))), driver.Selection);
        Assert.AreEqual(1, driver.Revision);
        Assert.AreEqual(driver.Selection.Pair, driver.Effective);

        Assert.IsFalse(driver.Apply(West)); Assert.AreEqual(1, driver.Revision);
        Assert.IsTrue(driver.Apply(East)); Assert.AreEqual(2, driver.Revision);
        Assert.AreEqual(new AmbientSelection(3, false, East, 15, new(new(0, 15, 0), new(0, 215, 0))), driver.Selection);
        Assert.AreEqual(15, driver.Clock.Hour, "a sector change keeps the game hour");

        Assert.IsTrue(driver.AdvanceHours(4, East));
        Assert.AreEqual((19, false, 68_400_000L), (driver.Clock.Hour, driver.Clock.IsDay, driver.Clock.GameTimestampMs));
        Assert.AreEqual(new AmbientPair(new(0, 19, 0), new(0, 219, 0)), driver.Selection.Pair);
        Assert.IsTrue(driver.AdvanceHours(6, West));
        Assert.AreEqual((1, 1L), (driver.Clock.Hour, driver.Clock.DayIndex));
        Assert.AreEqual(new AmbientSelection(1, true, West, 1, new(new(1, 1, 1), new(101, 0, 0))), driver.Selection);
        Assert.IsFalse(driver.AdvanceHours(24, West), "a whole day returns to the same hour and scheme");
        Assert.AreEqual((1, 2L), (driver.Clock.Hour, driver.Clock.DayIndex));
        Assert.AreEqual(4, driver.Revision);

        driver.Enabled = false;
        Assert.AreEqual(AmbientPair.Neutral, driver.Effective);
        Assert.AreEqual(new AmbientPair(new(1, 1, 1), new(101, 0, 0)), driver.Selection.Pair);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => driver.AdvanceHours(0, West));
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Apply(new AreaSectorCoordinate(1, 1).Packed));
        var clock = driver.Clock; var selection = driver.Selection;
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.AdvanceHours(1, new AreaSectorCoordinate(1, 1).Packed));
        Assert.AreEqual(clock, driver.Clock, "a refused advance leaves the clock unchanged");
        Assert.AreEqual(selection, driver.Selection);
    }

    [TestMethod]
    public void Sector_of_world_cell_uses_sixty_four_cell_sectors()
    {
        Assert.AreEqual(new AreaSectorCoordinate(1452, 1290).Packed, AreaAmbientDriver.SectorOf(92928, 82560));
        Assert.AreEqual(new AreaSectorCoordinate(1452, 1290).Packed, AreaAmbientDriver.SectorOf(92991, 82623));
        Assert.AreEqual(new AreaSectorCoordinate(1453, 1291).Packed, AreaAmbientDriver.SectorOf(92992, 82624));
    }
}
