using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Tests.Area;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>Synthetic installations built as loose files in a module folder; no original data.</summary>
[TestClass]
public sealed class WorldCatalogTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-world-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Open_lists_map_folders_and_reads_their_small_files()
    {
        StandardMap("alpha");
        Write("maps/nomap/1.sec", AreaIdentityTests.Sector());
        Write("maps/alpha/notes.sbf", [1]);
        Write("maps/alpha/x1.sec", [1]);
        Write($"maps/{WorldCatalog.AggregateName("alpha")}", new byte[16]);

        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);

        Assert.AreEqual(1, catalog.Maps.Count);
        var map = catalog.Find("ALPHA")!;
        Assert.AreEqual("alpha", map.Name);
        Assert.AreEqual(new WorldMapProperties(2, 1, 128, 64), map.Properties);
        Assert.AreEqual(new WorldTerrainHeader(1.2f, false, 2, 1, 2), map.Terrain);
        Assert.AreEqual(new WorldLocation(77, 99), map.StartLocation);
        Assert.AreEqual(new WorldMapInfo(3, 1, 40, 1), map.Info);
        Assert.AreEqual(1, map.JumpPoints.Count);
        Assert.AreEqual(new WorldJumpPoint(5, 0, new(10, 20), 5001, 0, new(30, 40)), map.JumpPoints[0]);
        CollectionAssert.AreEqual(new[] { new WorldSectorCoordinate(0, 0) }, map.SectorFiles.ToArray());
        CollectionAssert.AreEqual(new[] { "maps/alpha/1.mob" }, map.EditorMobilePaths.ToArray());
        Assert.AreEqual($"maps/{WorldCatalog.AggregateName("alpha")}", map.AggregatePath);
        CollectionAssert.AreEquivalent(new[] { "maps/alpha/notes.sbf", "maps/alpha/x1.sec" },
            map.Issues.Where(i => i.Code == WorldIssueCode.UnrecognisedFile).Select(i => i.Path).ToArray());
        Assert.IsTrue(catalog.Issues.Any(i => i.Code == WorldIssueCode.Missing && i.Path == "maps/nomap/map.prp"));
        CollectionAssert.AreEqual(new[] { 7001 }, catalog.PrototypeNumbers.ToArray());
    }

    [TestMethod]
    public void A_map_with_malformed_small_files_still_opens_and_reports_each_file()
    {
        Write("maps/beta/map.prp", Properties());
        Write("maps/beta/terrain.tdf", new byte[8]);
        Write("maps/beta/startloc.txt", "seven\r\n"u8.ToArray());
        Write("maps/beta/mapinfo.txt", "LightScheme:x\r\nLightScheme:2\r\n"u8.ToArray());
        Write("maps/beta/map.jmp", new byte[] { 2, 0, 0, 0 });
        Write("maps/gamma/map.prp", new byte[23]);

        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);

        var map = catalog.Find("beta")!;
        Assert.IsNull(map.Terrain); Assert.IsNull(map.StartLocation);
        Assert.AreEqual(new WorldMapInfo(2, null, null, 0), map.Info);
        Assert.AreEqual(0, map.JumpPoints.Count);
        CollectionAssert.AreEquivalent(new[] { "maps/beta/terrain.tdf", "maps/beta/startloc.txt", "maps/beta/mapinfo.txt", "maps/beta/map.jmp" },
            map.Issues.Select(i => i.Path).ToArray());
        Assert.IsNull(catalog.Find("gamma"));
        Assert.IsTrue(catalog.Issues.Any(i => i.Code == WorldIssueCode.Malformed && i.Path == "maps/gamma/map.prp"));
    }

    [TestMethod]
    public void ReadSector_returns_raw_sections_and_objects_and_keeps_the_prefix_before_a_refused_record()
    {
        StandardMap("alpha");
        var good = AreaIdentityTests.Record(AreaIdentityTests.Id(2, 11), location: 5L | (6L << 32));
        var broken = AreaIdentityTests.Record(AreaIdentityTests.Id(2, 12)); broken[0] = 120;
        Write("maps/alpha/67108865.sec", AreaIdentityTests.Sector(good, broken));

        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var map = catalog.Find("alpha")!;

        var first = catalog.ReadSector(map, new(0, 0));
        Assert.AreEqual(WorldSectorStatus.Read, first.Status);
        var sector = first.Sector!;
        Assert.AreEqual(3, sector.Version);
        Assert.AreEqual(4096, sector.Tiles.Count);
        Assert.IsNull(sector.Roofs);
        Assert.AreEqual(0, sector.Lights.Count);
        Assert.IsNotNull(sector.SectorScript); Assert.IsNotNull(sector.MetadataAndSound); Assert.IsNull(sector.BlockMask);
        Assert.AreEqual(1, sector.Objects.Count);
        Assert.IsNull(sector.ObjectIssue);
        Assert.AreEqual(3, sector.Objects[0].Type);
        Assert.AreEqual(7001, sector.Objects[0].PrototypeNumber);
        Assert.AreEqual(new WorldLocation(5, 6), sector.Objects[0].Location);

        var second = catalog.ReadSector(map, new(1, 1)).Sector!;
        Assert.AreEqual(2, second.DeclaredObjects);
        Assert.AreEqual(1, second.Objects.Count);
        Assert.AreEqual(new WorldIssue(WorldIssueCode.ObjectRefused, "maps/alpha/67108865.sec", 1, "UnsupportedVersion"), second.ObjectIssue);

        Assert.AreEqual(WorldSectorStatus.NoFile, catalog.ReadSector(map, new(5, 5)).Status);
    }

    [TestMethod]
    public void ReadSector_refuses_a_file_that_does_not_frame_without_refusing_the_map()
    {
        StandardMap("alpha");
        Write("maps/alpha/67108865.sec", new byte[10]);
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var map = catalog.Find("alpha")!;
        var refused = catalog.ReadSector(map, new(1, 1));
        Assert.AreEqual(WorldSectorStatus.Refused, refused.Status);
        Assert.AreEqual(WorldIssueCode.Malformed, refused.Issue!.Code);
        Assert.AreEqual(WorldSectorStatus.Read, catalog.ReadSector(map, new(0, 0)).Status);
    }

    [TestMethod]
    public void ReadMobiles_prefers_the_aggregate_and_falls_back_to_editor_files()
    {
        StandardMap("alpha");
        StandardMap("delta");
        Write($"maps/{WorldCatalog.AggregateName("alpha")}",
            [.. new byte[16], .. AreaIdentityTests.Record(AreaIdentityTests.Id(2, 21)), .. AreaIdentityTests.Record(AreaIdentityTests.Id(2, 22))]);
        Write("maps/delta/2.mob", [.. AreaIdentityTests.Record(AreaIdentityTests.Id(2, 31)), 0]);

        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);

        var aggregate = catalog.ReadMobiles(catalog.Find("alpha")!);
        Assert.AreEqual(WorldMobileSource.Aggregate, aggregate.Source);
        Assert.AreEqual(2, aggregate.Objects.Count);
        Assert.AreEqual(0, aggregate.Issues.Count);

        var editor = catalog.ReadMobiles(catalog.Find("delta")!);
        Assert.AreEqual(WorldMobileSource.Editor, editor.Source);
        Assert.AreEqual(1, editor.Objects.Count);
        Assert.AreEqual(new WorldIssue(WorldIssueCode.Malformed, "maps/delta/2.mob", null, "mobile file has trailing bytes"), editor.Issues.Single());
    }

    [TestMethod]
    public void ReadMobiles_stops_an_aggregate_at_the_first_refused_record()
    {
        StandardMap("alpha");
        var broken = AreaIdentityTests.Record(AreaIdentityTests.Id(2, 22)); broken[0] = 1;
        Write($"maps/{WorldCatalog.AggregateName("alpha")}", [.. new byte[16], .. AreaIdentityTests.Record(AreaIdentityTests.Id(2, 21)), .. broken]);
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var result = catalog.ReadMobiles(catalog.Find("alpha")!);
        Assert.AreEqual(1, result.Objects.Count);
        Assert.AreEqual(1, result.Issues.Single().Index);
    }

    [TestMethod]
    public void ReadTerrain_decodes_raw_and_row_compressed_grids_and_refuses_a_broken_row()
    {
        StandardMap("alpha");
        Write("maps/zeta/map.prp", Properties());
        Write("maps/zeta/terrain.tdf", CompressedTerrain(3, 2, broken: false));
        Write("maps/eta/map.prp", Properties());
        Write("maps/eta/terrain.tdf", CompressedTerrain(3, 2, broken: true));

        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);

        var raw = catalog.ReadTerrain(catalog.Find("alpha")!).Grid!;
        Assert.AreEqual((ushort)0x2102, raw[0, 0]); Assert.AreEqual((ushort)0x0007, raw[1, 0]);
        var compressed = catalog.ReadTerrain(catalog.Find("zeta")!).Grid!;
        Assert.IsTrue(compressed.Header.RowCompressed);
        Assert.AreEqual((ushort)0, compressed[0, 0]); Assert.AreEqual((ushort)11, compressed[1, 1]);
        Assert.AreEqual((ushort)12, compressed[2, 1]);
        var broken = catalog.ReadTerrain(catalog.Find("eta")!);
        Assert.IsNull(broken.Grid);
        Assert.AreEqual(1, broken.Issue!.Index);
    }

    [TestMethod]
    public void ReadPrototype_reads_by_number_and_reports_a_missing_one()
    {
        StandardMap("alpha");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var prototype = catalog.ReadPrototype(7001);
        Assert.IsNull(prototype.Issue);
        Assert.AreEqual(3, prototype.Prototype!.Type);
        Assert.IsNull(prototype.Prototype.PrototypeNumber);
        Assert.AreEqual(WorldIssueCode.Missing, catalog.ReadPrototype(7002).Issue!.Code);
    }

    [TestMethod]
    public void The_module_folder_shadows_the_module_archive_through_the_stack()
    {
        StandardMap("alpha");
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), AreaIdentityTests.Dat(("maps\\alpha\\startloc.txt", "1\r\n2\r\n"u8.ToArray())));
        using var stack = Stack();
        Assert.AreEqual(new WorldLocation(77, 99), WorldCatalog.Open(stack).Find("alpha")!.StartLocation);
    }

    [TestMethod]
    public void Coordinates_names_and_keys_follow_the_original_packing()
    {
        Assert.AreEqual(67108865L, new WorldSectorCoordinate(1, 1).Id);
        Assert.IsTrue(WorldSectorCoordinate.TryFromId(86570436012, out var start));
        Assert.AreEqual(new WorldSectorCoordinate(1452, 1290), start);
        Assert.IsFalse(WorldSectorCoordinate.TryFromId(-1, out _));
        Assert.AreEqual(new WorldSectorCoordinate(1452, 1290), WorldSectorCoordinate.Containing(new(92958, 82592)));
        Assert.AreEqual(new WorldLocation(5, 6), WorldLocation.FromPacked(5L | (6L << 32)));
        Assert.AreEqual("onm-poN", WorldCatalog.AggregateName("Abc-xyz"));
        StandardMap("alpha");
        using var stack = Stack();
        CollectionAssert.AreEqual(new[] { "maps/alpha/0.sec", "maps/alpha/1.mob" }, stack.Keys("MAPS\\alpha\\").Where(k => k.EndsWith(".sec") || k.EndsWith(".mob")).ToArray());
        Assert.AreEqual(0, stack.Keys("maps/é").Count);
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, "modules", "Mod", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private void StandardMap(string name)
    {
        Write($"maps/{name}/map.prp", Properties());
        var tdf = new byte[32 + 4];
        BinaryPrimitives.WriteSingleLittleEndian(tdf, 1.2f); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(8), 2);
        BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(16), 1); BinaryPrimitives.WriteInt32LittleEndian(tdf.AsSpan(24), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(tdf.AsSpan(32), 0x2102); BinaryPrimitives.WriteUInt16LittleEndian(tdf.AsSpan(34), 0x0007);
        Write($"maps/{name}/terrain.tdf", tdf);
        Write($"maps/{name}/startloc.txt", "77\r\n99\r\n"u8.ToArray());
        Write($"maps/{name}/mapinfo.txt", "LightScheme:3\r\nSoundScheme:1,40\r\nOther:1\r\n"u8.ToArray());
        var jmp = new byte[4 + 32];
        BinaryPrimitives.WriteInt32LittleEndian(jmp, 1); BinaryPrimitives.WriteUInt32LittleEndian(jmp.AsSpan(4), 5);
        BinaryPrimitives.WriteInt64LittleEndian(jmp.AsSpan(12), 10L | (20L << 32)); BinaryPrimitives.WriteInt32LittleEndian(jmp.AsSpan(20), 5001);
        BinaryPrimitives.WriteInt64LittleEndian(jmp.AsSpan(28), 30L | (40L << 32));
        Write($"maps/{name}/map.jmp", jmp);
        Write($"maps/{name}/0.sec", AreaIdentityTests.Sector(AreaIdentityTests.Record(AreaIdentityTests.Id(2, 1), location: 5L | (6L << 32))));
        Write($"maps/{name}/1.mob", AreaIdentityTests.Record(AreaIdentityTests.Id(2, 2)));
        Write("proto/007001 - Thing.pro", AreaIdentityTests.Prototype());
    }

    private static byte[] Properties()
    {
        var prp = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(prp, 2); BinaryPrimitives.WriteInt32LittleEndian(prp.AsSpan(4), 1);
        BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), 128); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 64);
        return prp;
    }

    /// <summary>Word (x, y) = y * 10 + x; row 1 is cut short when <paramref name="broken"/>.</summary>
    private static byte[] CompressedTerrain(int width, int height, bool broken)
    {
        using var output = new MemoryStream();
        var header = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(header, 1.2f); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), width); BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), height);
        output.Write(header);
        for (var y = 0; y < height; y++)
        {
            var row = new byte[(broken && y == 1 ? width - 1 : width) * 2];
            for (var x = 0; x * 2 < row.Length; x++) BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(x * 2), (ushort)(y * 10 + x));
            using var packed = new MemoryStream();
            using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(row);
            var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, (int)packed.Length);
            output.Write(size); output.Write(packed.ToArray());
        }
        return output.ToArray();
    }
}
