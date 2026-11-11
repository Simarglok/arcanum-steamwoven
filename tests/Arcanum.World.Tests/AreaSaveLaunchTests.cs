using Arcanum.Data.Maps;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-DEV-387: the scene a save from the menu opens on, before any scene is loaded.</summary>
[TestClass]
public sealed class AreaSaveLaunchTests
{
    private static TerrainViewerArguments Launch(string? map = "Arcanum1-024-fixed") =>
        new(TerrainViewerMode.Area, "game/modules/Arcanum.dat", "game/arcanum2.dat", "test", null, null, null, "game/arcanum1.dat", null, null)
            { AreaMapName = map, MainMenu = true, AreaMapSector = new(3, 4), DataRules = Arcanum.Data.Fixes.DataFixProfile.Steamwoven };

    [TestMethod]
    public void A_save_opens_its_own_map_at_its_cell_without_a_hero_under_the_default_rules()
    {
        var result = AreaSaveLaunch.Build(Launch() with { Hero = null }, runtimeMap: 2, x: 700, y: 1301, "Arcanum1-043");
        Assert.IsNull(result.Refusal);
        var next = result.Arguments!;
        Assert.AreEqual("Arcanum1-043", next.AreaMapName);
        Assert.AreEqual(new WorldLocation(700, 1301), next.AreaMapArrival, "the saved cell is the arrival tile (a large map opens its window around it)");
        Assert.IsNull(next.AreaMapSector, "the launch's sector is not kept");
        Assert.IsNull(next.DataRules, "the save's session brings its own rules");
        Assert.IsNull(next.Hero);
        Assert.IsTrue(next.MainMenu, "the other launch arguments are kept");
        Assert.AreEqual(2, result.RuntimeMap);
    }

    [TestMethod]
    public void A_map_the_list_cannot_open_and_the_exterior_mode_are_refused_with_a_reason()
    {
        var unlisted = AreaSaveLaunch.Build(Launch(), runtimeMap: 99, x: 1, y: 1, mapName: null);
        Assert.IsNull(unlisted.Arguments);
        StringAssert.Contains(unlisted.Refusal, "runtime map 99");
        var exterior = AreaSaveLaunch.Build(Launch(map: null), runtimeMap: 1, x: 1, y: 1, "Arcanum1-024-fixed");
        Assert.IsNull(exterior.Arguments);
        StringAssert.Contains(exterior.Refusal, "exterior");
    }
}
