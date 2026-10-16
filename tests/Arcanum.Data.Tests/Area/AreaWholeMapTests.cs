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

    [TestMethod]
    public void A_whole_map_draws_doors_and_windows_from_the_portal_names()
    {
        const uint door = (3u << 28) | (55u << 19) | (3u << 11) | (1u << 10) | 1u; // door 55, rotation 3, mirrored
        foreach (var (aid, file) in new[] { (door, "art/portal/door1.art"), (door | 0x200, "art/portal/dooD1.art"), ((3u << 28) | (54u << 19), "art/portal/wind1.art") })
        {
            var resources = new (string, byte[])[] { ("tile.mes", Tile), ("facade.mes", Facade), ("scenery.mes", Scenery),
                ("portal.mes", "2028 door light wood\r\n{55}{door1.art 2028}\r\n{1055}{wind1.art 2036}\r\n"u8.ToArray()),
                ("art/tile/stonebse0a.art", Art()), (file, Art()) };
            using var f = new Fixture([(Start, [TypedRecord(1)])], extraPrototype: PortalPrototype(aid), resourceEntries: resources,
                mappingRoles: [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, AreaMappingRole.Portal], selection: AreaSectorSelection.AllSectors);
            var snapshot = Complete(f);
            var portal = snapshot.Objects.Single(o => o.Type == 1);
            var visual = snapshot.Visuals.Single(v => v.Key.Object == portal.Key);
            Assert.AreEqual(0, visual.PhysicalGroup);
            Assert.AreEqual((aid & 1) != 0, visual.Mirrored);
        }
        // A pinned-style plan (townmap selection) does not admit portals.
        var plain = new (string, byte[])[] { ("tile.mes", Tile), ("facade.mes", Facade), ("scenery.mes", Scenery),
            ("portal.mes", "{55}{door1.art 2028}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()), ("art/portal/door1.art", Art()) };
        using var townmap = new Fixture([(Start, [TypedRecord(1)])], extraPrototype: PortalPrototype(door), resourceEntries: plain,
            mappingRoles: [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, AreaMappingRole.Portal]);
        Assert.IsTrue(townmap.Owner.Inspect().Issues.Any(i => i.Code == AreaErrorCode.UnsupportedVersionOrSchema));
    }

    [TestMethod]
    public void A_hidden_trap_needs_no_visual_on_a_whole_map()
    {
        using var f = new Fixture([(Start, [TypedRecord(17)])], extraPrototype: HiddenTrapPrototype(), resourceEntries: Resources(),
            selection: AreaSectorSelection.AllSectors);
        var snapshot = Complete(f);
        var trap = snapshot.Objects.Single(o => o.Type == 17);
        Assert.AreEqual(AreaDisposition.BodySuppressedBySerializedState, trap.Disposition);
        Assert.IsFalse(snapshot.Visuals.Any(v => v.Key.Object == trap.Key));
    }

    private static AreaSnapshot Complete(Fixture fixture)
    {
        var report = fixture.Owner.Inspect();
        Assert.IsTrue(report.IsComplete, string.Join("; ", report.Issues));
        return report.Snapshot!;
    }

    /// <summary>A type-1 prototype: 36 common slots then the portal group (six numbers, two arrays).</summary>
    private static byte[] PortalPrototype(uint aid) => TypedPrototype(1, 44, [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 42, 43], aid, flags: 0);
    /// <summary>A type-17 trap prototype (36 common slots, short group of three numbers and two
    /// arrays) whose object flags hide it (OF_OFF).</summary>
    private static byte[] HiddenTrapPrototype() => TypedPrototype(17, 41, [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 39, 40], 0xE0000000, flags: 0x1);

    /// <summary>An instance of prototype 7002 that overrides only its location (slot 1).</summary>
    private static byte[] TypedRecord(int type)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1, 7002)); s.Write(Id(2, 11)); I32(s, type);
        s.Write(new byte[] { 1, 0 }); I32(s, 2); I32(s, 0); I32(s, 0); s.WriteByte(1); I64(s, 0); return s.ToArray();
    }

    private static byte[] TypedPrototype(int type, int fields, int[] optional, uint aid, uint flags)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1)); s.Write(Id(1, 7002)); I32(s, type);
        for (var word = 0; word < 3; word++) I32(s, 0);
        for (var field = 0; field < fields; field++)
        {
            if (optional.Contains(field)) s.WriteByte(0);
            else I32(s, field == 0 ? unchecked((int)aid) : field == 11 ? 100 : field == 18 ? (int)flags : 0);
        }
        return s.ToArray();
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
