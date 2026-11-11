using System.Text;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-038 (ledger TP1–TP3): the teleport host over invented world-map data: the anti-teleport test measures an object on the start map from its own tile and on another map from its map's area; the area helper
/// finds the area by radius on the start map and by the map list elsewhere. Areas, zones and maps are invented.
/// </summary>
[TestClass]
public sealed class AreaSpellTeleportTests
{
    private const int Width = 64, StartMap = 1, Cave = 7, Hut = 8, Unlisted = 9;
    private const string Hero = "hero";

    // Area 0 unknown; area 1 at (1000, 1000) with a radius of 64 tiles; area 2 at (5000, 5000) with the table's default 320.
    private static AreaWorldMapData Data(AntiTeleportZones? zones)
    {
        var types = TerrainTypeTable.Create(MesFile.Parse(Encoding.ASCII.GetBytes("{0}{grass}")).File!, _ => true).Table!;
        var grid = new WorldTerrainGrid(new(1.2f, false, Width, Width, 0), new ushort[Width * Width]);
        var map = new WorldMap("start", new(0, 0, Width * 64, Width * 64), grid.Header, null, null, [], [], [], null, []);
        var sectors = new WorldTravelSectors(map, grid, types);
        var areas = WorldAreaTable.Create(["0, 0, 0, 0 /None/x", "1000, 1000, 0, 0 /Camp/x./Radius:1", "5000, 5000, 0, 0 /Town/x"]);
        var size = (int)(Width / AreaWorldMap.PictureScale);
        // The cave belongs to area 1, the hut to area 0 (a row with no area), the unlisted map has no row.
        return new("start", StartMap, areas, sectors, Width, size, AreaWorldMap.Draw(Width, size, grid, types, sectors), null, zones,
            new Dictionary<int, int> { [StartMap] = 0, [Cave] = 1, [Hut] = 0 });
    }

    private static AntiTeleportZones Zones(params int[] disabled) => new([new(new(1000, 1000), 64)], disabled);

    private static AreaSpellTeleport Host(AreaWorldMapData? data, int map, CellCoord? hero) => new(() => data, () => map, id => id == Hero ? hero : null);

    [TestMethod]
    public void On_the_start_map_the_object_is_measured_from_its_own_tile()
    {
        var data = Data(Zones());
        Assert.IsFalse(Host(data, StartMap, new(1040, 1040)).Allowed(Hero, null), "inside the region");
        Assert.IsTrue(Host(data, StartMap, new(1064, 1000)).Allowed(Hero, null), "a distance equal to the radius is outside");
        Assert.IsTrue(Host(data, StartMap, null).Allowed(Hero, null), "an object with no tile is measured from (0, 0), which is outside the region here");
    }

    [TestMethod]
    public void On_another_map_the_object_is_measured_from_its_maps_area_and_a_disabled_or_unlisted_map_refuses()
    {
        var data = Data(Zones(Hut));
        Assert.IsFalse(Host(data, Cave, new(0, 0)).Allowed(Hero, null), "the cave's area 1 lies in the region");
        Assert.IsFalse(Host(data, Hut, new(0, 0)).Allowed(Hero, null), "the hut is a disabled map");
        Assert.IsFalse(Host(data, Unlisted, new(0, 0)).Allowed(Hero, null), "a map with no list row is refused");
        Assert.IsTrue(Host(Data(Zones()), Hut, new(0, 0)).Allowed(Hero, null), "area 0's location (0, 0) is outside the region");
    }

    [TestMethod]
    public void A_target_location_is_measured_against_the_regions_and_a_module_that_is_off_or_not_loaded_allows_all()
    {
        var data = Data(Zones());
        var host = Host(data, StartMap, new(3000, 3000));
        Assert.IsFalse(host.Allowed(Hero, new(1010, 1010)));
        Assert.IsTrue(host.Allowed(Hero, new(5000, 5000)));
        Assert.IsTrue(Host(Data(null), StartMap, new(1040, 1040)).Allowed(Hero, null), "no AntiTeleport.mes: the original allows every teleport");
        Assert.IsTrue(Host(null, StartMap, new(1040, 1040)).Allowed(Hero, null), "the data is still loading");
    }

    [TestMethod]
    public void The_area_helper_finds_the_area_by_radius_on_the_start_map_and_by_the_map_list_elsewhere()
    {
        var data = Data(Zones());
        // Area 2 (radius 320) covers (5100, 5000); the plan carries its location and the test's answer.
        var onStart = Host(data, StartMap, new(5100, 5000)).Portal(Hero);
        Assert.AreEqual((2, true), (onStart.Area, onStart.Allowed));
        Assert.AreEqual(new Arcanum.Simulation.Magic.SpellLocation(5000, 5000), onStart.Destination);
        Assert.AreEqual(0, Host(data, StartMap, new(3000, 3000)).Portal(Hero).Area, "no area covers the tile");
        var inCave = Host(data, Cave, new(0, 0)).Portal(Hero);
        Assert.AreEqual((1, false), (inCave.Area, inCave.Allowed), "the cave's area is 1, and its location lies in the region");
        Assert.AreEqual(0, Host(data, Hut, new(0, 0)).Portal(Hero).Area);
        Assert.AreEqual(0, Host(null, StartMap, new(5100, 5000)).Portal(Hero).Area, "not loaded");
    }
}
