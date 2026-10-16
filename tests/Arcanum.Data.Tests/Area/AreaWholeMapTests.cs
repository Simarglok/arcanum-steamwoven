using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-019 whole-map plans on synthetic fixtures: every sector file is core,
/// no context ring, roofs do not block, and ART is read with the original's leniency.</summary>
[TestClass]
public sealed class AreaWholeMapTests
{
    private const long Start = 67108865, Diagonal = 134217730, Far = 335544325;

    [TestMethod]
    public void All_sectors_selection_takes_every_sector_file_and_no_ring()
    {
        using var f = new Fixture([(Start, []), (Diagonal, []), (Far, [])], resourceEntries: Resources(), selection: AreaSectorSelection.AllSectors);
        var s = f.Owner.LoadStrict();
        Assert.AreEqual(3, s.Reconciliation.CoreSectors);
        Assert.AreEqual(0, s.Reconciliation.ContextPresent);
        Assert.AreEqual(0, s.Reconciliation.ContextMissing);
        Assert.IsTrue(s.Sectors.All(row => row.Scope == AreaScope.Core && row.Present));
        Assert.AreEqual("arc-data-019-whole-map-v1", s.Profile);
    }

    [TestMethod]
    public void A_roof_list_blocks_the_townmap_profile_but_not_a_whole_map()
    {
        var module = Dat(("maps/test/67108865.sec", RoofedSector()));
        using (var townmap = new Fixture([(Start, [])], moduleBytes: module))
        {
            var report = townmap.Owner.Inspect();
            Assert.IsFalse(report.IsComplete);
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.UnsupportedArtClassOrState && i.Stage == AreaStage.Sector));
        }
        using var whole = new Fixture([(Start, [])], moduleBytes: module, resourceEntries: Resources(), selection: AreaSectorSelection.AllSectors);
        var snapshot = whole.Owner.LoadStrict();
        Assert.AreEqual(0, snapshot.Sectors.Single().RoofDiscriminator);
    }

    [TestMethod]
    public void Art_with_a_tail_is_read_only_when_the_plan_allows_it()
    {
        var tailed = Resources(Art(), [.. Art(), 0]);
        var record = Record(Id(2, 11));
        using (var strict = new Fixture([(Start, [record])], resourceEntries: tailed, selection: AreaSectorSelection.AllSectors))
            Assert.IsFalse(strict.Owner.Inspect().IsComplete);
        using var lenient = new Fixture([(Start, [record])], resourceEntries: tailed, selection: AreaSectorSelection.AllSectors, allowArtTrailingBytes: true);
        var snapshot = lenient.Owner.LoadStrict();
        Assert.AreEqual(1, snapshot.Objects.Count);
        Assert.IsTrue(snapshot.Objects.Single().SchemaReady);
    }

    [TestMethod]
    public void Original_tile_names_take_three_characters_when_no_slash_ends_the_name()
    {
        var entries = new SortedDictionary<int, string> { [0] = "Dn1 3", [100] = "grs/b", [200] = "abc", [300] = "xyz", [400] = "end" };
        Assert.AreEqual("Dn1", Arcanum.Data.Terrain.TerrainLoader.TileNames.Parse(entries, originalNames: true).Name(0, true, true));
        Assert.AreEqual("grs", Arcanum.Data.Terrain.TerrainLoader.TileNames.Parse(entries, originalNames: true).Name(0, false, true));
        Assert.AreEqual("Dn1 3", Arcanum.Data.Terrain.TerrainLoader.TileNames.Parse(entries).Name(0, true, true));
        entries[0] = "ab";
        Assert.ThrowsExactly<Arcanum.Data.Terrain.TerrainFormatException>(() => Arcanum.Data.Terrain.TerrainLoader.TileNames.Parse(entries, originalNames: true));
    }

    private static readonly byte[] Tile = System.Text.Encoding.ASCII.GetBytes("{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}");
    private static readonly byte[] Facade = System.Text.Encoding.ASCII.GetBytes("{0}{fixture}");
    private static readonly byte[] Scenery = System.Text.Encoding.ASCII.GetBytes("{0}{fixture.art}");

    /// <summary>The fixture's resources with tile names written the way a whole map reads them
    /// (a name ends at '/'; without one only its first three characters count).</summary>
    private static (string, byte[])[] Resources(byte[]? tile = null, byte[]? scenery = null) =>
        [("tile.mes", Tile), ("facade.mes", Facade), ("scenery.mes", Scenery), ("art/tile/stonebse0a.art", tile ?? Art()), ("art/scenery/fixture.art", scenery ?? Art())];

    /// <summary>As <see cref="AreaIdentityTests.Sector"/> (format 3, townmap 2, no objects) with a roof list.</summary>
    private static byte[] RoofedSector()
    {
        using var s = new MemoryStream(); I32(s, 0); s.Write(new byte[16384]); I32(s, 0); s.Write(new byte[1024]); I32(s, 0xAA0003);
        I32(s, 0); s.Write(new byte[12]); I32(s, 2); s.Write(new byte[20]); I32(s, 0); return s.ToArray();
    }
}
