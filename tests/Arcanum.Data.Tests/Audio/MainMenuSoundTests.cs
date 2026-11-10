using System.Text;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Audio;

/// <summary>ARC-DEV-332: the map and sector the game init opens before the main menu (synthetic map lists; no original data).</summary>
[TestClass]
public sealed class MainMenuSoundTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-menusound-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod", "rules"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private MapList Read(string text)
    {
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod", "rules", "maplist.mes"), Encoding.Latin1.GetBytes(text));
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return MapList.Read(stack);
    }

    [TestMethod]
    public void The_menu_stands_on_the_shopping_map_at_its_start_tile_sector()
    {
        var place = MainMenuSound.Choose(Read("{5000}{start, 92958, 82592, Type: START_MAP}\n{5001}{other, 1, 1}\n{5002}{shop, 31, 29, Type: SHOPPING_MAP}\n"))!;
        Assert.AreEqual("shop", place.Map.Name);
        Assert.AreEqual(new WorldSectorCoordinate(0, 0), place.Sector);
    }

    [TestMethod]
    public void Without_a_shopping_map_it_stands_on_the_start_map_and_a_far_tile_is_in_its_own_sector()
    {
        var place = MainMenuSound.Choose(Read("{5000}{other, 1, 1}\n{5001}{start, 92958, 82592, Type: START_MAP}\n"))!;
        Assert.AreEqual("start", place.Map.Name);
        Assert.AreEqual(new WorldSectorCoordinate(1452, 1290), place.Sector);
    }

    [TestMethod]
    public void A_row_without_a_start_tile_has_no_sector_and_a_list_without_either_type_has_no_place()
    {
        Assert.IsNull(MainMenuSound.Choose(Read("{5000}{shop, -1, 4, Type: SHOPPING_MAP}\n"))!.Sector);
        Assert.IsNull(MainMenuSound.Choose(Read("{5000}{plain, 1, 1}\n")));
        Assert.IsNull(MainMenuSound.Choose(Read("")));
    }

    /// <summary>Opt-in (DAT_ROOT = the game folder): the installed module's opening map is the shop map, whose one sector holds the pair 26/29,
    /// and scheme 26 loops the main theme all day; the map itself has no default pair.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_opens_the_shop_map_whose_start_sector_plays_the_main_menu_scheme()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var maps = MapList.Read(stack);
        var place = MainMenuSound.Choose(maps)!;
        Assert.AreEqual(MapListType.Shopping, place.Map.Type);
        Assert.AreEqual(new WorldSectorCoordinate(0, 0), place.Sector);
        var world = WorldCatalog.Open(stack);
        var map = world.Find(place.Map.Name)!;
        Assert.IsNull(map.Info?.MusicScheme, "no map default pair");
        var record = SoundCatalog.SectorSound(world.ReadSector(map, place.Sector!.Value).Sector!)!;
        Assert.AreEqual((26, 29, true), (record.MusicScheme, record.AmbientScheme, record.AppliesOnEntry));
        var catalog = SoundCatalog.Open(stack).Catalog!;
        var scheme = catalog.Scheme(26);
        Assert.AreEqual(SoundSchemeStatus.Loaded, scheme.Status);
        var song = scheme.Entries.Single();
        Assert.AreEqual((SoundSchemeEntryKind.LoopSong, "sound/music/arcanum.mp3"), (song.Kind, song.File.Key));
        Assert.IsTrue(!song.HasHourWindow || (song.HourStart, song.HourEnd) == (0, 23), "plays at every hour");
        Assert.AreEqual(SoundSchemeStatus.NoListBase, catalog.Scheme(29).Status, "the ambient id 29 loads nothing");
    }
}
