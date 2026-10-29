using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Scripts.ScrFixture;

namespace Arcanum.Data.Tests.Maps;

/// <summary>Synthetic module folders: a map list, two maps, scenery, a container, a mobile, tile and sector
/// scripts and SCR files written from the documented layouts. No original data.</summary>
[TestClass]
public sealed class MapTransitionsTests
{
    private static readonly Clause Always = Clause.Of(0), Nothing = Clause.Of(0);
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-transitions-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void The_map_list_numbers_rows_from_5000_until_the_first_gap()
    {
        Write("rules/maplist.mes", Encoding.Latin1.GetBytes("{5000}{Alpha, 10, 20, Type: START_MAP}\n{5001}{ beta ,x}\n{5002}{gamma,-1,4}\n{5004}{late,1,1}\n"));
        using var stack = Stack();
        var list = MapList.Read(stack);
        Assert.AreEqual(MapListStatus.Read, list.Status);
        CollectionAssert.AreEqual(new[] { new MapListEntry(5000, "Alpha", new(10, 20)) { Type = MapListType.Start }, new MapListEntry(5001, "beta", null),
            new MapListEntry(5002, "gamma", null) }, list.Entries.ToArray());
        Assert.AreEqual(5000, list.Find("ALPHA")!.Catalogue);
        Assert.AreEqual(2, list.Find("beta")!.Runtime);
        Assert.AreEqual("gamma", list.ByRuntime(3)!.Name);
        Assert.AreEqual("Alpha", list.ByCatalogue(5000)!.Name);
        Assert.IsNull(list.Find("late")); Assert.IsNull(list.ByCatalogue(5003)); Assert.IsNull(list.ByCatalogue(4999)); Assert.IsNull(list.ByRuntime(0));
        Assert.AreEqual((5001, 2), Arcanum.Data.Area.AreaMapPlans.CatalogueNumbers(stack, "Beta"));
    }

    /// <summary>ARC-UI-038: the type after the start (0x4125C0, 0x40FF50): the keyword is case-sensitive, the word is not, the last row
    /// of a type is that type's map and an unknown word or one with trailing white space names none.</summary>
    [TestMethod]
    public void The_map_list_reads_each_rows_type_and_the_last_row_of_a_type_is_its_map()
    {
        Write("rules/maplist.mes", Encoding.Latin1.GetBytes("{5000}{start, 10, 20, Type: START_MAP, WorldMap: 0}\n{5001}{shop, 31, 29,  Type:shopping_map}\n" +
            "{5002}{other, 1, 1, type: SHOPPING_MAP}\n{5003}{spaced, 1, 1, Type: SHOPPING_MAP }\n{5004}{market, 2, 2, Type: Shopping_Map}\n{5005}{none, 3, 3, Type: NONE}\n"));
        using var stack = Stack();
        var list = MapList.Read(stack);
        CollectionAssert.AreEqual(new[] { MapListType.Start, MapListType.Shopping, MapListType.None, MapListType.None, MapListType.Shopping, MapListType.None },
            list.Entries.Select(entry => entry.Type).ToArray());
        Assert.AreEqual(("start", "market"), (list.OfType(MapListType.Start)!.Name, list.OfType(MapListType.Shopping)!.Name));
        Assert.IsNull(list.OfType(MapListType.None));
    }

    /// <summary>ARC-SCRIPT-050: the area after the start (0x4125C0 at 0x4126FD, 0x40FFE0): Type, WorldMap and Area are tested in this order on
    /// one cursor, each optional; a value that matches none of the remaining keywords stops the reading, and the number is atol's.</summary>
    [TestMethod]
    public void The_map_list_reads_each_rows_area_after_its_optional_type_and_world_map()
    {
        Write("rules/maplist.mes", Encoding.Latin1.GetBytes("{5000}{start, 10, 20, Type: START_MAP, WorldMap: 0, Area: 7}\n{5001}{plain, 1, 1,Area: 12}\n" +
            "{5002}{world, 1, 1, WorldMap: 3,  Area:  -4x}\n{5003}{blocked, 1, 1, Foo, Area: 5}\n{5004}{late, 1, 1, Area: 6, Type: START_MAP}\n" +
            "{5005}{lower, 1, 1, area: 8}\n{5006}{typed, 1, 1, Type: NOWHERE, Area: 9}\n{5007}{none, 1, 1}\n{5008}{short}\n"));
        using var stack = Stack();
        var list = MapList.Read(stack);
        CollectionAssert.AreEqual(new[] { 7, 12, -4, 0, 6, 0, 9, 0, 0 }, list.Entries.Select(entry => entry.Area).ToArray());
        // 5004's type comes after its area: the type is tested only first, on its area, so the row has none.
        Assert.AreEqual(MapListType.None, list.ByRuntime(5)!.Type);
        Assert.AreEqual((7, 12, 0, 0), (list.AreaOfRuntime(1), list.AreaOfRuntime(2), list.AreaOfRuntime(0), list.AreaOfRuntime(10)));
    }

    [TestMethod]
    public void A_missing_or_unparsed_map_list_is_typed_and_empty()
    {
        using (var stack = Stack()) Assert.AreEqual(MapListStatus.Missing, MapList.Read(stack).Status);
        Write("rules/maplist.mes", "{5000}{unterminated"u8.ToArray());
        using (var stack = Stack())
        {
            var list = MapList.Read(stack);
            Assert.AreEqual(MapListStatus.Malformed, list.Status);
            Assert.AreEqual(0, list.Entries.Count);
        }
    }

    [TestMethod]
    public void Jump_points_resolve_runtime_maps_and_keep_the_current_map_for_minus_one()
    {
        Maps();
        Write("maps/alpha/map.jmp", Jumps((new(10, 20), 2, new(30, 40)), (new(11, 21), -1, new(31, 41)), (new(12, 22), 9, new(1, 1))));
        var set = Read("alpha");
        var jumps = set.Sites.Where(s => s.Trigger == MapTransitionTrigger.JumpPoint).ToArray();
        Assert.AreEqual(3, jumps.Length);
        Assert.AreEqual((new WorldLocation(10, 20), 5001, 2, new WorldLocation(30, 40), MapTransitionGuard.None),
            (jumps[0].Location, jumps[0].DestinationCatalogue, jumps[0].DestinationRuntime, jumps[0].Arrival, jumps[0].Guards));
        Assert.AreEqual((5000, 1), (jumps[1].DestinationCatalogue, jumps[1].DestinationRuntime));
        Assert.AreEqual(MapTransitionGuard.UnlistedDestination, jumps[2].Guards);
        Assert.IsTrue(jumps.All(j => j.Script == 0 && j.Object is null && j.Chain.Count == 0));
    }

    [TestMethod]
    public void Use_scripts_on_placed_objects_are_classified_by_their_path_and_object()
    {
        Maps();
        Script(10, Lines((Always, Teleport(0, 5001, 7, 8), Nothing)));                                  // unconditional
        Script(11, Lines((Clause.Of(43, N(5)), Teleport(0, 5001, 7, 8), Nothing)));                     // behind a global flag
        Script(12, Lines((Always, Clause.Of(102, N(1), N(0), N(0), (21, 0), N(5001), (4, 3), N(9)), Nothing))); // computed x
        Script(13, Lines((Always, Teleport(1, 5001, 7, 8), Nothing)));                                  // moves the attachee
        Script(14, Lines((Always, Clause.Of(7, N(10), N(0), (0, 0), (1, 0)), Nothing), (Always, Clause.Of(1), Nothing))); // literal call
        Script(15, Lines((Always, Clause.Of(7, N(10), N(0), (0, 0), (10, 0)), Nothing)));              // call on a looped object
        Script(16, Lines((Always, Teleport(21, 4998, 3, 4), Nothing), (Always, Clause.Of(5), Nothing))); // same map, removes itself
        Script(17, ScriptBytes(ScrCatalogue.FileFlag.MagicalTrap, (Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(18, Lines((Always, Clause.Of(18, (2, 0)), Nothing), (Always, Teleport(0, 5001, 7, 8), Nothing), (Always, Clause.Of(19), Nothing))); // loop body
        Script(19, Lines((Always, Clause.Of(54, N(10), N(0), (0, 0), (1, 0), N(1)), Nothing)));         // deferred call
        Write("scr/00020broken.scr", new byte[10]);
        Write("maps/alpha/0.sec", Sector([], null,
            Scenery(1, 5, 6, 0, 10), Scenery(2, 5, 6, 0x2, 10), Scenery(3, 5, 6, 0x800, 10), Scenery(4, 5, 6, 0, 10, type: 2),
            Scenery(5, 5, 6, 0, 11), Scenery(6, 5, 6, 0, 12), Scenery(7, 5, 6, 0, 13), Scenery(8, 5, 6, 0, 14), Scenery(9, 5, 6, 0, 15),
            Scenery(10, 5, 6, 0, 16), Scenery(11, 5, 6, 0, 17), Scenery(12, 5, 6, 0, 18), Scenery(13, 5, 6, 0, 19), Scenery(14, 5, 6, 0, 20),
            Scenery(15, 5, 6, 0, 99), Scenery(16, 5, 6, 0, null)));

        var set = Read("alpha");
        var use = set.Sites.Where(s => s.Trigger == MapTransitionTrigger.Use).ToDictionary(s => s.Object!.Index + 1);
        Assert.AreEqual(13, use.Count, "objects 14..16 hold no readable teleport");
        Assert.AreEqual(1, set.Issues.Count);
        Assert.AreEqual((WorldIssueCode.Unreadable, 20), (set.Issues[0].Code, set.Issues[0].Index));

        var first = use[1];
        Assert.IsTrue(first.Unconditional);
        Assert.AreEqual((new WorldLocation(5, 6), 10, 0, false, false, 5001, 2, new WorldLocation(7, 8)),
            (first.Location, first.Script, first.Line, first.Fade, first.OneShot, first.DestinationCatalogue, first.DestinationRuntime, first.Arrival));
        Assert.AreEqual(new MapTransitionObject(new(0, 0), 0, first.Object!.OwnIdSha256, 3, 7001), first.Object);
        CollectionAssert.AreEqual(new[] { 10 }, first.Chain.ToArray());
        Assert.AreEqual(MapTransitionGuard.ObjectOffOrDestroyed, use[2].Guards);
        Assert.AreEqual(MapTransitionGuard.ObjectNotClickable, use[3].Guards);
        Assert.AreEqual(MapTransitionGuard.ContainerOpenCheck, use[4].Guards);
        Assert.AreEqual(MapTransitionGuard.ConditionalPath, use[5].Guards);
        Assert.AreEqual(MapTransitionGuard.ComputedDestination, use[6].Guards);
        Assert.IsTrue(use[6].Fade);
        Assert.AreEqual(MapTransitionGuard.MovesOtherObject, use[7].Guards);
        Assert.IsTrue(use[8].Unconditional);
        CollectionAssert.AreEqual(new[] { 14, 10 }, use[8].Chain.ToArray());
        Assert.AreEqual(MapTransitionGuard.ConditionalPath, use[9].Guards);
        Assert.IsTrue(use[10].Unconditional && use[10].OneShot);
        Assert.AreEqual((5000, 1, new WorldLocation(3, 4)), (use[10].DestinationCatalogue, use[10].DestinationRuntime, use[10].Arrival));
        Assert.AreEqual(MapTransitionGuard.TrapScript, use[11].Guards);
        Assert.AreEqual(MapTransitionGuard.ConditionalPath, use[12].Guards);
        Assert.AreEqual(MapTransitionGuard.ConditionalPath, use[13].Guards);
        Assert.AreEqual(3, set.Unconditional.Count());
    }

    [TestMethod]
    public void Tile_sector_and_mobile_attachments_are_read_with_their_locations()
    {
        Maps();
        Script(10, Lines((Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(11, Lines((Always, Teleport(0, 5001, 7, 8), Nothing), (Always, Clause.Of(5), Nothing)));      // removes itself
        Write("maps/alpha/67108865.sec", Sector([(65 + 64 * 3, 10), (2, 11)], 10, Scenery(41, 5, 6, 0, 11)));
        Write($"maps/{WorldCatalog.AggregateName("alpha")}", [.. new byte[16], .. Scenery(31, 100, 200, 0, 10)]);

        var set = Read("alpha");
        Assert.AreEqual(0, set.Issues.Count, string.Join("; ", set.Issues));
        var tile = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.TileEntry && s.Script == 10);
        Assert.AreEqual((new WorldLocation(65, 68), (MapTransitionObject?)null, true), (tile.Location, tile.Object, tile.Unconditional));
        // A tile script's own changes are not kept by the original (ledger X3), so it never works only once.
        var removing = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.TileEntry && s.Script == 11);
        Assert.AreEqual((new WorldLocation(66, 64), false), (removing.Location, removing.OneShot));
        var sector = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.SectorEntry);
        Assert.AreEqual(new WorldLocation(64, 64), sector.Location);
        var placed = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.Use && s.Object!.Sector is not null);
        Assert.AreEqual((new WorldLocation(69, 70), new WorldSectorCoordinate(1, 1), true), (placed.Location, placed.Object!.Sector, placed.OneShot));
        var mobile = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.Use && s.Object!.Sector is null);
        Assert.AreEqual((new WorldLocation(100, 200), (WorldSectorCoordinate?)null, 0), (mobile.Location, mobile.Object!.Sector, mobile.Object.Index));
    }

    // ARC-SCRIPT-017: every tile and sector script is listed with its attachment, teleport or not.
    [TestMethod]
    public void Every_tile_and_sector_script_is_listed_with_its_attachment()
    {
        Maps();
        Script(10, Lines((Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(12, Lines((Always, Clause.Of(1), Nothing)));
        Write("maps/alpha/67108865.sec", Sector([(65 + 64 * 3, 10, 0, 0), (2, 12, 0x5, 0x0302), (7, 0, 0, 0)], (0x8, 0x01, 12)));

        var set = Read("alpha");
        Assert.AreEqual(0, set.Issues.Count, string.Join("; ", set.Issues));
        var sector = new WorldSectorCoordinate(1, 1);
        CollectionAssert.AreEqual(new[]
        {
            new MapPlacedScript(MapTransitionTrigger.TileEntry, sector, 65 + 64 * 3, new(65, 68), 0, 0, 10),
            new MapPlacedScript(MapTransitionTrigger.TileEntry, sector, 2, new(66, 64), 0x5, 0x0302, 12),
            new MapPlacedScript(MapTransitionTrigger.SectorEntry, sector, -1, new(64, 64), 0x8, 0x01, 12),
        }, set.Placed.ToArray(), "script 0 is no script; the non-teleporting ones are listed too");
        Assert.AreEqual(1, set.Sites.Count(s => s.Trigger == MapTransitionTrigger.TileEntry), "only the teleport is a site");
    }

    [TestMethod]
    public void Area_endpoints_are_the_unconditional_use_sites_of_static_objects_in_the_core()
    {
        // ARC-DATA-036 (AreaEndpoints.FromTransitions): a placed object's use site in a core sector is an endpoint; a mobile's
        // use site, tile and sector entries, a conditional use site and a sector outside the core are not.
        Maps();
        Script(10, Lines((Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(11, Lines((Clause.Of(43, N(5)), Teleport(0, 5001, 7, 8), Nothing)));
        Write("maps/alpha/67108865.sec", Sector([(65 + 64 * 3, 10)], 10, Scenery(41, 5, 6, 0, 10), Scenery(42, 7, 6, 0, 11)));
        Write("maps/alpha/0.sec", Sector([], null, Scenery(43, 5, 6, 0, 10)));
        Write($"maps/{WorldCatalog.AggregateName("alpha")}", [.. new byte[16], .. Scenery(31, 100, 200, 0, 10)]);
        var set = Read("alpha");
        Assert.AreEqual(0, set.Issues.Count, string.Join("; ", set.Issues));
        var placed = set.Sites.Single(s => s.Trigger == MapTransitionTrigger.Use && s.Object!.Sector == new WorldSectorCoordinate(1, 1) && s.Unconditional);
        var endpoints = Arcanum.Data.Area.AreaEndpoints.FromTransitions(set, sector => sector == new Arcanum.Data.Area.AreaSectorCoordinate(1, 1));
        Assert.AreEqual(new Arcanum.Data.Area.AreaEndpoint(new WorldSectorCoordinate(1, 1).Id, 0, 7001, Arcanum.Data.Area.AreaEndpoints.UseSap, 10, 5001, 2, 7, 8,
            placed.Object!.OwnIdSha256), endpoints.Single());
        Assert.AreEqual(2, Arcanum.Data.Area.AreaEndpoints.FromTransitions(set, _ => true).Count);
        Assert.AreEqual(0, Arcanum.Data.Area.AreaEndpoints.FromTransitions(set, _ => false).Count);
    }

    [TestMethod]
    public void An_unlisted_map_and_an_unlisted_destination_are_marked()
    {
        Maps();
        Script(10, Lines((Always, Teleport(0, 5009, 7, 8), Nothing)));
        Write("maps/gamma/map.prp", Properties());
        Write("maps/gamma/0.sec", Sector([], null, Scenery(1, 1, 1, 0, 10)));
        var set = Read("gamma");
        Assert.IsNull(set.Entry);
        Assert.AreEqual(MapTransitionGuard.UnlistedDestination, set.Sites.Single().Guards);
        Assert.AreEqual((0, 10), (set.Sites.Single().DestinationCatalogue, set.Sites.Single().DestinationRuntime));
    }

    [TestMethod]
    public void Calls_object_kinds_prototypes_and_jump_tiles_follow_the_policy()
    {
        Maps();
        Write("maps/alpha/map.jmp", Jumps((new(10, 20), 2, new(30, 40)), (new(10, 20), 1, new(31, 41))));
        Write("maps/alpha/notes.sbf", [1]);
        Script(10, Lines((Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(21, Lines((Always, Clause.Of(7, N(10), N(0), (1, 0), (1, 0)), Nothing)));                // the attachee triggers the callee
        Script(22, ScriptBytes(ScrCatalogue.FileFlag.AutoRemoving, (Always, Teleport(0, 5001, 7, 8), Nothing)));
        Script(23, Lines((Always, Clause.Of(99, (1, 0), (1, 0), N(10)), Nothing)));                   // change script: a later run
        Script(24, Lines((Always, Clause.Of(7, N(24), N(0), (0, 0), (1, 0)), Nothing), (Always, Teleport(0, 5001, 7, 8), Nothing))); // calls itself
        Script(25, Lines((Always, Clause.Of(3, (4, 1)), Nothing), (Always, Teleport(0, 5001, 7, 8), Nothing)));   // computed goto
        Write("maps/alpha/0.sec", Sector([], null, Scenery(1, 1, 1, 0, 10, type: 0), Scenery(2, 1, 1, 0, 21), Scenery(3, 1, 1, 0, 22),
            Scenery(4, 1, 1, 0, 23), Scenery(5, 1, 1, 0, 24), Scenery(6, 1, 1, 0, 25), Scenery(7, 1, 1, 0, 10, prototype: 7002)));

        var set = Read("alpha");
        var use = set.Sites.Where(s => s.Trigger == MapTransitionTrigger.Use).GroupBy(s => s.Object!.Index + 1).ToDictionary(g => g.Key, g => g.ToArray());
        Assert.AreEqual(MapTransitionGuard.UseRunsOtherAction, use[1].Single().Guards);
        Assert.AreEqual(MapTransitionGuard.CallChangesTriggerer, use[2].Single().Guards);
        Assert.IsTrue(use[3].Single().Unconditional && use[3].Single().OneShot);
        Assert.AreEqual(MapTransitionGuard.ConditionalPath, use[4].Single().Guards);
        Assert.IsTrue(use[5].Single().Unconditional);
        CollectionAssert.AreEqual(new[] { 24 }, use[5].Single().Chain.ToArray(), "the self call at the same entry is a cycle");
        Assert.IsFalse(use.ContainsKey(6), "a computed goto ends the walk");
        Assert.IsTrue(use[7].Single().Unconditional, "an instance with its own scripts keeps them without its prototype");
        Assert.AreEqual(1, set.Sites.Count(s => s.Trigger == MapTransitionTrigger.JumpPoint), "the original takes the first jump point of a tile");
        CollectionAssert.AreEquivalent(new[] { "UnrecognisedFile maps/alpha/notes.sbf", "Missing proto/007002" }, set.Issues.Select(i => $"{i.Code} {i.Path}").ToArray());
    }

    [TestMethod]
    public void A_call_graph_over_the_visit_limit_is_refused_for_its_script()
    {
        Maps();
        for (var level = 30; level < 35; level++)
            Script(level, Enumerable.Range(0, 20).Select(line => (Always, level == 34 ? Teleport(0, 5001, 7, 8) : Clause.Of(7, N(level + 1), N(line), (0, 0), (1, 0)), Nothing)).ToArray());
        Write("maps/alpha/0.sec", Sector([], null, Scenery(1, 1, 1, 0, 30)));
        var set = Read("alpha");
        Assert.AreEqual(0, set.Sites.Count);
        Assert.AreEqual((WorldIssueCode.LimitExceeded, 30), (set.Issues.Single().Code, set.Issues.Single().Index));
    }

    private MapTransitionSet Read(string name)
    {
        var catalog = Catalog(out var stack, out var scripts);
        using (stack) using (scripts) return MapTransitions.Read(catalog, MapList.Read(stack), catalog.Find(name)!, scripts);
    }

    private WorldCatalog Catalog(out ContentStack stack, out ScriptContent scripts)
    {
        stack = Stack();
        scripts = ScriptContent.Over(stack);
        return WorldCatalog.Open(stack);
    }

    private void Maps()
    {
        Write("rules/maplist.mes", "{5000}{alpha,1,2}\n{5001}{beta,3,4}\n"u8.ToArray());
        var tdf = new byte[32 + 8];
        BinaryPrimitives.WriteSingleLittleEndian(tdf, 1.2f); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(8), 2); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(16), 2);
        foreach (var name in new[] { "alpha", "beta" }) { Write($"maps/{name}/map.prp", Properties()); Write($"maps/{name}/terrain.tdf", tdf); }
        // Prototypes are read from the base sources only (ARC-DATA-026).
        var proto = Path.Combine(_root, "data", "proto");
        Directory.CreateDirectory(proto);
        File.WriteAllBytes(Path.Combine(proto, "007001 - Thing.pro"), AreaIdentityTests.Prototype());
    }

    private static Clause Teleport(byte focus, int map, int x, int y) => Clause.Of(44, (focus, 0), N(map), N(x), N(y));
    private static (Clause, Clause, Clause)[] Lines(params (Clause, Clause, Clause)[] lines) => lines;
    private static byte[] ScriptBytes(uint flags, params (Clause, Clause, Clause)[] lines) => Arcanum.Data.Tests.Scripts.ScrFixture.Script(0, 0, flags, lines.Length, lines);
    private void Script(int number, (Clause, Clause, Clause)[] lines) => Script(number, Arcanum.Data.Tests.Scripts.ScrFixture.Script(lines));
    private void Script(int number, byte[] bytes) => Write($"scr/{number:D5}s.scr", bytes);

    /// <summary>A v119 instance of prototype 7001: location (slot 1), flags (18), spell flags (19) and, with a
    /// script number, a script array (31) holding the use slot only.</summary>
    private static byte[] Scenery(int own, long x, long y, uint flags, int? use, int type = 3, int prototype = 7001)
    {
        using var s = new MemoryStream();
        AreaIdentityTests.I32(s, 119); s.Write(AreaIdentityTests.Id(1, prototype)); s.Write(AreaIdentityTests.Id(2, own)); AreaIdentityTests.I32(s, type);
        var mask = (1u << 1) | (1u << 18) | (1u << 19) | (use is null ? 0 : 1u << 31);
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(word, (short)BitOperations.PopCount(mask)); s.Write(word[..2]);
        U32(s, mask); U32(s, 0); U32(s, 0);
        s.WriteByte(1); AreaIdentityTests.I64(s, x | (y << 32));
        U32(s, flags); U32(s, 0);
        if (use is { } script)
        {
            s.WriteByte(1); AreaIdentityTests.I32(s, 12); AreaIdentityTests.I32(s, 1); AreaIdentityTests.I32(s, 0);
            U32(s, 0); U32(s, 0); AreaIdentityTests.I32(s, script);
            AreaIdentityTests.I32(s, 1); U32(s, 1u << 1);
        }
        return s.ToArray();
    }

    /// <summary>A version-3 sector: no lights, blank tiles, no roofs, then tile script nodes (flags, tile, header flags,
    /// counters, script, spare), the sector script (header flags, counters, script), metadata and the objects.</summary>
    private static byte[] Sector((int Tile, int Script)[] tiles, int? sectorScript, params byte[][] records) =>
        Sector(tiles.Select(t => (t.Tile, t.Script, 0, 0)).ToArray(), (0, 0, sectorScript ?? 0), records);

    private static byte[] Sector((int Tile, int Script, int Flags, int Counters)[] tiles, (int Flags, int Counters, int Script) sectorScript, params byte[][] records)
    {
        using var s = new MemoryStream();
        AreaIdentityTests.I32(s, 0); s.Write(new byte[16384]); AreaIdentityTests.I32(s, 1); AreaIdentityTests.I32(s, 0xAA0003);
        AreaIdentityTests.I32(s, tiles.Length);
        foreach (var (tile, script, flags, counters) in tiles)
        { AreaIdentityTests.I32(s, 0); AreaIdentityTests.I32(s, tile); AreaIdentityTests.I32(s, flags); AreaIdentityTests.I32(s, counters); AreaIdentityTests.I32(s, script); AreaIdentityTests.I32(s, 0); }
        AreaIdentityTests.I32(s, sectorScript.Flags); AreaIdentityTests.I32(s, sectorScript.Counters); AreaIdentityTests.I32(s, sectorScript.Script);
        AreaIdentityTests.I32(s, 2); s.Write(new byte[20]);
        foreach (var record in records) s.Write(record);
        AreaIdentityTests.I32(s, records.Length);
        return s.ToArray();
    }

    private static byte[] Jumps(params (WorldLocation From, int Map, WorldLocation To)[] jumps)
    {
        var bytes = new byte[4 + 32 * jumps.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, jumps.Length);
        for (var i = 0; i < jumps.Length; i++)
        {
            var at = bytes.AsSpan(4 + 32 * i);
            BinaryPrimitives.WriteInt64LittleEndian(at[8..], jumps[i].From.X | (jumps[i].From.Y << 32));
            BinaryPrimitives.WriteInt32LittleEndian(at[16..], jumps[i].Map);
            BinaryPrimitives.WriteInt64LittleEndian(at[24..], jumps[i].To.X | (jumps[i].To.Y << 32));
        }
        return bytes;
    }

    private static byte[] Properties()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2); BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), 128); BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), 128);
        return bytes;
    }

    private static void U32(Stream s, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); s.Write(b); }

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
}
