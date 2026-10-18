using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>ARC-DATA-031: terrain types, the template path rule and generated sectors on synthetic data only.</summary>
[TestClass]
public sealed class TerrainTemplateTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-terrain-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void A_terrain_word_holds_base_second_code_and_variant()
    {
        var word = new TerrainWord(Word(19, 2, 11, 3));
        Assert.AreEqual((19, 2, 11, 3), (word.Base, word.Second, word.Transition, word.Variant));
        var outside = new TerrainWord(TerrainWord.OutsideGrid);
        Assert.AreEqual((31, 31, 15, 3), (outside.Base, outside.Second, outside.Transition, outside.Variant));
    }

    [TestMethod]
    public void Types_are_the_consecutive_messages_from_zero_with_the_blocked_mark_cut()
    {
        var load = Table("{0}{Green Grass}\n{1}{Deep Water \t /b anything}\n{2}{Hills/b}\n{4}{Gap}\n");
        Assert.AreEqual(TerrainTypeStatus.Ready, load.Status);
        CollectionAssert.AreEqual(new[] { new TerrainType(0, "Green Grass", false), new TerrainType(1, "Deep Water", true), new TerrainType(2, "Hills", true) },
            load.Table!.Types.ToArray());
        Assert.AreEqual("Hills", load.Table.Name(2));
        Assert.IsNull(load.Table.Name(3));
        Assert.IsNull(load.Table.Name(-1));

        var many = Table(string.Concat(Enumerable.Range(0, 40).Select(n => $"{{{n}}}{{T{n}}}\n")));
        Assert.AreEqual(TerrainTypeTable.MaxTypes, many.Table!.Types.Count);
    }

    [TestMethod]
    public void Terrain_initialisation_failures_are_typed()
    {
        Assert.AreEqual(TerrainTypeStatus.NoTypes, Table("{1}{Grass}\n").Status);
        Assert.AreEqual(TerrainTypeStatus.MalformedName, Table("{0}{Grass}\n{1}{ \t/b}\n").Status);
        var missing = Table("{0}{Grass}\n", exists: _ => false);
        Assert.AreEqual(TerrainTypeStatus.FirstTypeWithoutTemplate, missing.Status);
        Assert.AreEqual("terrain/grass/0.sec", missing.Detail);
        string? asked = null;
        Table("{0}{Desert Island}\n", exists: path => { asked = path; return true; });
        Assert.AreEqual("terrain/desert island/0.sec", asked);
    }

    [TestMethod]
    public void A_plain_word_names_the_base_folder_and_its_variant_slot()
    {
        var table = Table("{0}{Grass}\n{1}{Desert Island}\n").Table!;
        long[] ids = [0, 1, 67108864, 67108865];
        for (var variant = 0; variant < 4; variant++)
        {
            var path = table.Resolve(new(Word(1, 1, 0, variant)));
            Assert.AreEqual(new TerrainTemplatePath(TerrainTemplateKind.Plain, $"terrain/desert island/{ids[variant]}.sec", 1, 1,
                new WorldSectorCoordinate(variant % 2, variant / 2)), path);
        }
        // Code 0 is plain whatever the second type; base = second is plain whatever the code.
        Assert.AreEqual("terrain/grass/1.sec", table.Resolve(new(Word(0, 1, 0, 1))).Path);
        Assert.AreEqual("terrain/grass/0.sec", table.Resolve(new(Word(0, 0, 9, 0))).Path);
    }

    [TestMethod]
    public void A_transition_word_names_the_pair_folder_and_the_slot_of_its_code()
    {
        var table = Table("{0}{Grass}\n{1}{Water}\n").Table!;
        // Code → (swapped, slot id); the swap uses the slot of code 15 − c.
        var expected = new Dictionary<int, (bool Swapped, long Slot)>
        {
            [1] = (false, 1), [2] = (false, 134217729), [3] = (false, 67108865), [4] = (false, 134217731), [5] = (false, 0),
            [6] = (false, 134217730), [7] = (true, 3), [8] = (false, 3), [9] = (false, 2), [10] = (true, 0), [11] = (true, 134217731),
            [12] = (false, 67108867), [13] = (true, 134217729), [14] = (true, 1),
        };
        foreach (var (code, (swapped, slot)) in expected)
        {
            var path = table.Resolve(new(Word(0, 1, code, 2)));
            var folder = swapped ? "water to grass" : "grass to water";
            Assert.AreEqual(TerrainTemplateKind.Transition, path.Kind, $"code {code}");
            Assert.AreEqual($"terrain/{folder}/{slot}.sec", path.Path, $"code {code}");
            Assert.AreEqual(swapped ? (1, 0) : (0, 1), (path.From, path.To), $"code {code}");
            Assert.AreEqual(slot, path.Slot!.Value.Id, $"code {code}");
        }
        Assert.AreEqual(new TerrainTemplatePath(TerrainTemplateKind.NoSlot, "terrain/water to grass/-1.sec", 1, 0, null), table.Resolve(new(Word(0, 1, 15, 0))));
    }

    [TestMethod]
    public void A_type_without_a_name_is_unnamed_where_the_original_faults()
    {
        var table = Table("{0}{Grass}\n{1}{Water}\n").Table!;
        Assert.AreEqual(TerrainTemplateKind.UnnamedType, table.Resolve(new(Word(2, 2, 0, 0))).Kind);
        Assert.AreEqual(TerrainTemplateKind.UnnamedType, table.Resolve(new(Word(0, 5, 1, 0))).Kind);
        Assert.AreEqual(TerrainTemplateKind.UnnamedType, table.Resolve(new(TerrainWord.OutsideGrid)).Kind);
    }

    [TestMethod]
    public void The_catalogue_resolves_and_reads_own_template_fill_and_refused_sectors()
    {
        WriteBase("terrain/terrain.mes", "{0}{Grass}\n{1}{Water /b}\n"u8.ToArray());
        WriteBase("terrain/grass/0.sec", AreaIdentityTests.Sector());
        // Variant 3 of grass is the template folder's sector (1, 1): its records lie at tiles 64..127.
        WriteBase("terrain/grass/67108865.sec", SectorWithLights([Light(65, 66)],
            AreaIdentityTests.Record(AreaIdentityTests.Id(0), location: 70L | (80L << 32))));
        Write("terrain/grass to water/1.sec", AreaIdentityTests.Sector());
        // Five sectors wide by map.prp, four by the grid.
        Write("maps/alpha/map.prp", Properties(5 * 64, 64));
        Write("maps/alpha/terrain.tdf", Grid(4, 1, Word(0, 0, 0, 0), Word(0, 0, 0, 3), Word(1, 0, 14, 0), Word(0, 1, 15, 0)));
        Write("maps/alpha/0.sec", AreaIdentityTests.Sector(AreaIdentityTests.Record(AreaIdentityTests.Id(2, 1), location: 5L | (6L << 32))));
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        Assert.AreEqual(TerrainTypeStatus.Ready, catalog.TerrainTypes.Status);
        var map = catalog.Find("alpha")!;
        var grid = catalog.ReadTerrain(map).Grid!;

        var own = catalog.ResolveSector(map, new(0, 0), grid);
        Assert.AreEqual(new WorldSectorSource(new(0, 0), WorldSectorSourceKind.Own, "maps/alpha/0.sec", null, null), own);
        var ownRead = catalog.ReadResolvedSector(map, own).Sector!;
        Assert.AreEqual(WorldSectorSourceKind.Own, ownRead.SourceKind);
        Assert.AreEqual(new WorldLocation(5, 6), ownRead.Objects[0].Location);

        var plain = catalog.ResolveSector(map, new(1, 0), grid);
        Assert.AreEqual(WorldSectorSourceKind.Template, plain.Kind);
        Assert.AreEqual("terrain/grass/67108865.sec", plain.Path);
        var generated = catalog.ReadResolvedSector(map, plain).Sector!;
        Assert.AreEqual(WorldSectorSourceKind.Template, generated.SourceKind);
        Assert.AreEqual(new WorldSectorCoordinate(1, 0), generated.Coordinate);
        Assert.AreEqual("terrain/grass/67108865.sec", generated.Path);
        // Object (70, 80) of slot (1, 1) moves to sector (1, 0): (64 + 6, 0 + 16); the light (65, 66) to (65, 2).
        Assert.AreEqual(new WorldLocation(70, 16), generated.Objects[0].Location);
        Assert.AreEqual((65, 2), (generated.Lights[0][2], generated.Lights[0][3]));
        CollectionAssert.AreEqual(Light(65, 66)[4..], generated.Lights[0].Skip(4).ToArray(), "only the location moves");

        var transition = catalog.ResolveSector(map, new(2, 0), grid);
        Assert.AreEqual(WorldSectorSourceKind.Template, transition.Kind);
        Assert.AreEqual("terrain/grass to water/1.sec", transition.Path);
        Assert.AreEqual(TerrainTemplateKind.Transition, transition.Template!.Kind);

        var fill = catalog.ResolveSector(map, new(3, 0), grid);
        Assert.AreEqual(WorldSectorSourceKind.Fill, fill.Kind);
        Assert.AreEqual(TerrainTemplateKind.NoSlot, fill.Template!.Kind);
        Assert.AreEqual(WorldSectorStatus.NoFile, catalog.ReadResolvedSector(map, fill).Status);

        // Inside map.prp but outside the grid: the word reads 0xFFFF, whose types have no name.
        var beyondGrid = catalog.ResolveSector(map, new(4, 0), grid);
        Assert.AreEqual(WorldSectorSourceKind.Unresolved, beyondGrid.Kind);
        Assert.AreEqual(new TerrainWord(TerrainWord.OutsideGrid), beyondGrid.Word);
        Assert.AreEqual(WorldSectorSourceKind.OutsideMap, catalog.ResolveSector(map, new(5, 0), grid).Kind);
        Assert.AreEqual(WorldSectorSourceKind.OutsideMap, catalog.ResolveSector(map, new(0, 1), grid).Kind);
        Assert.AreEqual(WorldSectorSourceKind.Unresolved, catalog.ResolveSector(map, new(1, 0), null).Kind);
    }

    [TestMethod]
    public void Terrain_types_come_from_the_base_sources_only()
    {
        WriteBase("terrain/terrain.mes", "{0}{Grass}\n"u8.ToArray());
        WriteBase("terrain/grass/0.sec", AreaIdentityTests.Sector());
        Write("terrain/terrain.mes", "{0}{Sand}\n"u8.ToArray());
        Write("terrain/sand/0.sec", AreaIdentityTests.Sector());
        using var stack = Stack();
        Assert.AreEqual("Grass", TerrainTypeTable.Open(stack).Table!.Name(0));

        Directory.Delete(Path.Combine(_root, "data"), recursive: true);
        using var moduleOnly = Stack();
        Assert.AreEqual(TerrainTypeStatus.Missing, TerrainTypeTable.Open(moduleOnly).Status);
    }

    [TestMethod]
    public void Relocation_keeps_the_tile_inside_the_slot()
    {
        Assert.AreEqual(1452L * 64 + 3 | (1290L * 64 + 63) << 32, WorldCatalog.RelocateLocation(67L | (127L << 32), new(1452, 1290)));
        var light = WorldCatalog.RelocateLight([0, 0, 64, 0, 7, 8, 9, 10, 11, 12, 13, 14], new(2, 3));
        CollectionAssert.AreEqual(new[] { 0, 0, 128, 192, 7, 8, 9, 10, 11, 12, 13, 14 }, light.ToArray());
    }

    private static TerrainTypeLoad Table(string mes, Func<string, bool>? exists = null)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(mes));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return TerrainTypeTable.Create(parsed.File!, exists ?? (_ => true));
    }

    private static ushort Word(int baseType, int second, int code, int variant) => (ushort)(baseType << 11 | second << 6 | code << 2 | variant);

    private static int[] Light(int x, int y) => [0, 0, x, y, 0, 0, 0, 0, 0, 0, 0, 0];

    private static byte[] SectorWithLights(int[][] lights, params byte[][] records)
    {
        var tail = AreaIdentityTests.Sector(records).AsSpan(4).ToArray();
        using var s = new MemoryStream();
        var word = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(word, lights.Length); s.Write(word);
        foreach (var light in lights)
            foreach (var w in light) { BinaryPrimitives.WriteInt32LittleEndian(word, w); s.Write(word); }
        s.Write(tail);
        return s.ToArray();
    }

    private static byte[] Properties(long width, long height)
    {
        var prp = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), width); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), height);
        return prp;
    }

    private static byte[] Grid(int width, int height, params ushort[] words)
    {
        var tdf = new byte[32 + words.Length * 2];
        BinaryPrimitives.WriteSingleLittleEndian(tdf, 1.2f);
        BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(8), width); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(16), height);
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(tdf.AsSpan(32 + i * 2), words[i]);
        return tdf;
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Write(string relative, byte[] bytes) => WriteAt(Path.Combine("modules", "Mod"), relative, bytes);
    private void WriteBase(string relative, byte[] bytes) => WriteAt("data", relative, bytes);

    private void WriteAt(string folder, string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
