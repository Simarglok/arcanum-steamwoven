using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-018: whole-map resources resolve in content-stack order, on synthetic
/// installations (plans from <see cref="AreaMapPlans"/>) and on owner plans with an explicit order.</summary>
[TestClass]
public sealed class AreaStackOrderTests
{
    private const long Start = 67108865;
    private static readonly byte[] Tile = "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray();
    private static readonly byte[] Table = "{0}{fixture}"u8.ToArray();
    private static readonly byte[] Scenery = "{0}{fixture.art}"u8.ToArray();
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-stackorder-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void A_plan_reads_each_resource_from_the_source_that_wins_it()
    {
        // arcanum1 holds everything; Arcanum4 (above it) overrides the scenery ART; the module
        // folder (top of the stack) overrides the tile ART with a loose file; tig.dat (bottom)
        // holds an ART of its own; arcanum2 wins nothing and is no input.
        var tailed = (byte[])[.. Art(), 0];
        Install(arcanum1: Base(("art/scenery/fixture.art", Art())), arcanum4: Dat(("art/scenery/fixture.art", tailed)), tig: Dat(("art/blank.art", Art())));
        Loose("modules/Mod/art/tile/stonebse0a.art", tailed);
        using var stack = Stack();
        var result = AreaMapPlans.Create(stack, WorldCatalog.Open(stack), "test");
        Assert.AreEqual(AreaMapPlanStatus.Ready, result.Status, result.Detail);
        var plan = result.Plan!;
        var order = plan.Plan.ResourceOrder;
        var arcanum4 = order.Single(s => s.Key.Role == AreaSourceRole.Archive && plan.Files.Single(f => f.Key == s.Key).Path.EndsWith("Arcanum4.dat", StringComparison.Ordinal)).Key;
        var tig = order.Single(s => s.Key.Role == AreaSourceRole.Archive && s.Key != arcanum4).Key;
        CollectionAssert.AreEqual(new[] { new AreaResourceSource(new(AreaSourceRole.Loose, 1), "art/tile/stonebse0a.art"), new(arcanum4), new(new(AreaSourceRole.Resource1)), new(tig) }, order.ToArray());
        Assert.IsFalse(plan.Files.Any(f => f.Key.Role == AreaSourceRole.Resource2));
        Assert.AreEqual(3, plan.Limits.MaxResourceInputs); // Arcanum4, tig.dat and the loose tile

        using var owner = plan.Open();
        var snapshot = owner.LoadStrict();
        Assert.IsTrue(snapshot.Terrain.Count > 0);
        Assert.IsTrue(snapshot.Visuals.Where(v => v.Key.Kind == AreaSourceKind.Terrain).All(v => v.Entry.Source == new AreaInputKey(AreaSourceRole.Loose, 1)));
        var scenery = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(arcanum4, scenery.Entry.Source);
        Assert.AreEqual(Hash(tailed), scenery.PayloadSha256);
        // A selected frame is read again from the loose input it names.
        using var lease = owner.AcquireSelectedFrame(snapshot, snapshot.Visuals.First(v => v.Key.Kind == AreaSourceKind.Terrain).Key);
        Assert.IsTrue(lease.PixelByteCount > 0);
    }

    [TestMethod]
    public void Name_tables_prototypes_and_the_aggregate_come_from_their_winners()
    {
        // The scenery names are a loose file in data/, above every base archive; the prototype is
        // an archive member of Arcanum4.dat, read from memory when the plan opens.
        Install(arcanum1: Base(("art/scenery/other.art", Art())), arcanum4: Dat(("proto/007001 - test.pro", Prototype())), prototypeLoose: false);
        Loose("data/art/scenery/scenery.mes", "{0}{other.art}"u8.ToArray());
        using var stack = Stack();
        var result = AreaMapPlans.Create(stack, WorldCatalog.Open(stack), "test");
        Assert.AreEqual(AreaMapPlanStatus.Ready, result.Status, result.Detail);
        var plan = result.Plan!;
        var names = plan.Plan.Mappings.Single(m => m.Role == AreaMappingRole.Scenery);
        Assert.AreEqual(AreaSourceRole.Loose, names.Entry.Source.Role);
        Assert.IsFalse(plan.Plan.Mappings.Any(m => m.Role == AreaMappingRole.MissingPolicy)); // absent: the masks are rebuilt (N3)
        var prototype = plan.Files.Single(f => f.Key == new AreaInputKey(AreaSourceRole.Prototype, 7001));
        Assert.IsNotNull(prototype.Member);
        Assert.IsTrue(prototype.Path.EndsWith("Arcanum4.dat", StringComparison.Ordinal));
        using var owner = plan.Open();
        var snapshot = owner.LoadStrict();
        Assert.AreEqual(Hash(Prototype()), snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Prototype).Sha256);
        Assert.AreEqual(1, snapshot.Visuals.Count(v => v.Key.Kind == AreaSourceKind.Static));
    }

    [TestMethod]
    public void A_sector_or_editor_mobile_overridden_outside_the_module_archive_is_refused()
    {
        foreach (var file in new[] { $"maps/test/{Start}.sec", "maps/test/0.mob" })
        {
            Directory.CreateDirectory(_root); Install(arcanum1: Base());
            Loose("modules/Mod/" + file, [0]);
            using (var stack = Stack())
            {
                var result = AreaMapPlans.Create(stack, WorldCatalog.Open(stack), "test");
                Assert.AreEqual(AreaMapPlanStatus.UnsupportedSource, result.Status, file);
            }
            Directory.Delete(_root, recursive: true);
        }
        Directory.CreateDirectory(_root);
    }

    [TestMethod]
    public void Too_many_loose_resources_are_refused()
    {
        Install(arcanum1: Base());
        for (var i = 0; i <= AreaMapPlans.MaxLooseResources; i++) Loose($"data/art/extra/{i}.art", [0]);
        using var stack = Stack();
        Assert.AreEqual(AreaMapPlanStatus.TooLarge, AreaMapPlans.Create(stack, WorldCatalog.Open(stack), "test").Status);
    }

    [TestMethod]
    public void The_first_source_in_order_wins_and_records_that_hold_no_file_are_skipped()
    {
        var upper = Art(); upper[1184] = 9;
        foreach (var (flags, wins) in new[] { (1u, true), (0x400u, false), (0x800u, false) })
        {
            using var f = new OrderFixture([new(new(AreaSourceRole.Archive, 3)), new(new(AreaSourceRole.Resource1))],
                (new(AreaSourceRole.Archive, 3), FlaggedDat(("art/scenery/fixture.art", upper, flags))));
            var visual = f.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(wins ? new AreaInputKey(AreaSourceRole.Archive, 3) : new AreaInputKey(AreaSourceRole.Resource1), visual.Entry.Source, flags.ToString());
        }
        // The same two archives in the other order: the lower archive's copy is never read.
        using var reversed = new OrderFixture([new(new(AreaSourceRole.Resource1)), new(new(AreaSourceRole.Archive, 3))],
            (new(AreaSourceRole.Archive, 3), FlaggedDat(("art/scenery/fixture.art", upper, 1))));
        Assert.AreEqual(new AreaInputKey(AreaSourceRole.Resource1), reversed.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static).Entry.Source);
    }

    [TestMethod]
    public void A_loose_input_holds_its_one_path()
    {
        var loose = Art(); loose[1184] = 7;
        var key = new AreaInputKey(AreaSourceRole.Loose, 1);
        using var f = new OrderFixture([new(key, "art/scenery/fixture.art"), new(new(AreaSourceRole.Resource1))], (key, loose));
        var snapshot = f.Owner.LoadStrict();
        var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(AreaDataOwner.LooseIdentity(key, "art/scenery/fixture.art", loose.Length), visual.Entry);
        Assert.AreEqual(Hash(loose), visual.PayloadSha256);
        Assert.AreEqual(new AreaInputKey(AreaSourceRole.Resource1), snapshot.Visuals.First(v => v.Key.Kind == AreaSourceKind.Terrain).Entry.Source);
    }

    [TestMethod]
    public void A_stack_order_must_name_every_resource_input_within_the_limit()
    {
        var extra = (new AreaInputKey(AreaSourceRole.Archive, 3), Dat(("art/x.art", Art())));
        // Listed but over the limit, then admitted, then unlisted.
        var budget = Throws<AreaException>(() => new OrderFixture([new(extra.Item1), new(new(AreaSourceRole.Resource1))], 0, extra).Dispose());
        Assert.AreEqual(AreaErrorCode.BudgetExceeded, budget.Issue.Code);
        using (new OrderFixture([new(extra.Item1), new(new(AreaSourceRole.Resource1))], 1, extra)) { }
        var unlisted = Throws<AreaException>(() => new OrderFixture([new(new(AreaSourceRole.Resource1))], 1, extra).Dispose());
        Assert.AreEqual((AreaErrorCode.InvalidSelection, AreaStage.Input), (unlisted.Issue.Code, unlisted.Issue.Stage));
    }

    [TestMethod]
    public void A_stack_order_names_plan_inputs_of_resource_roles_and_normalized_loose_paths()
    {
        var identities = new AreaInputIdentity[] { new(new(AreaSourceRole.Module), 1, new string('a', 64)), new(new(AreaSourceRole.Loose, 1), 1, new string('b', 64)),
            new(new(AreaSourceRole.Prototype, 1), 1, new string('c', 64)) };
        AreaSourcePlan Plan(params AreaResourceSource[] order) => new(1, 1, new(1, 1), 0, "maps/test", identities, []) { ResourceOrder = order };
        Assert.AreEqual(2, Plan(new(new(AreaSourceRole.Loose, 1), "art/a.art"), new(new(AreaSourceRole.Module))).ResourceOrder.Count);
        foreach (var order in new AreaResourceSource[][]
        {
            [new(new(AreaSourceRole.Archive, 9))], // not an input
            [new(new(AreaSourceRole.Prototype, 1))], // not a resource role
            [new(new(AreaSourceRole.Module), "art/a.art")], // an archive names no path
            [new(new(AreaSourceRole.Loose, 1))], [new(new(AreaSourceRole.Loose, 1), "art/A.art")], [new(new(AreaSourceRole.Loose, 1), "art\\a.art")],
            [new(new(AreaSourceRole.Loose, 1), "art//a.art")], [new(new(AreaSourceRole.Loose, 1), "art/../a.art")],
            [new(new(AreaSourceRole.Module)), new(new(AreaSourceRole.Module))],
        })
            Assert.ThrowsExactly<ArgumentException>(() => Plan(order), string.Join(", ", order.Select(s => $"{s.Key} {s.Path}")));
    }

    /// <summary>A one-sector whole map with one scenery object, owner-level, whose resource archive
    /// (Resource1) holds every name; extra inputs and the stack order come from the test.</summary>
    private sealed class OrderFixture : IDisposable
    {
        internal OrderFixture(AreaResourceSource[] order, params (AreaInputKey Key, byte[] Bytes)[] extra) : this(order, extra.Length, extra) { }
        internal OrderFixture(AreaResourceSource[] order, int maxResources, params (AreaInputKey Key, byte[] Bytes)[] extra)
        {
            var resource = Dat(("art/tile/tilename.mes", Tile), ("art/facade/facadename.mes", Table), ("art/scenery/scenery.mes", Scenery),
                ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art()));
            var files = new List<(AreaInputKey Key, byte[] Bytes)> { (new(AreaSourceRole.Module), Dat(($"maps/test/{Start}.sec", Sector(Record(Id(2, 11)))))),
                (new(AreaSourceRole.Resource1), resource), (new(AreaSourceRole.Prototype, 7001), Prototype()) };
            files.AddRange(extra);
            using var archive = DatArchive.Open(new MemoryStream(resource));
            var mappings = new[] { AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery }.Select((role, i) =>
            {
                var e = archive.Entries[i];
                return new AreaMappingBinding(role, new(new(AreaSourceRole.Resource1), i, 1, e.StoredSize, e.DeclaredOutputSize, e.Offset, Hash(e.RawNameBytes.ToArray())), Hash(archive.ReadEntry(e)));
            }).ToArray();
            var plan = new AreaSourcePlan(1, 1, new(1, 1), 2, "maps/test", files.Select(f => new AreaInputIdentity(f.Key, f.Bytes.Length, Hash(f.Bytes))), mappings)
            { Selection = AreaSectorSelection.AllSectors, AllowArtTrailingBytes = true, ResourceOrder = order };
            Owner = AreaDataOwner.Open(new(files.Select(f => new AreaInput(f.Key, new MemoryStream(f.Bytes)))), plan, new() { MaxResourceInputs = maxResources });
        }
        internal AreaDataOwner Owner { get; }
        public void Dispose() => Owner.Dispose();
    }

    /// <summary>The synthetic installation: tig.dat, the base archives given, module <c>Mod</c>
    /// (one map "test" with one scenery object of prototype 7001) and the prototype as a loose file.</summary>
    private void Install(byte[] arcanum1, byte[]? arcanum4 = null, byte[]? tig = null, bool prototypeLoose = true)
    {
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), tig ?? Dat());
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), arcanum1);
        File.WriteAllBytes(Path.Combine(_root, "arcanum2.dat"), Dat(("rules/other.mes", "{0}{x}"u8.ToArray())));
        if (arcanum4 is not null) File.WriteAllBytes(Path.Combine(_root, "Arcanum4.dat"), arcanum4);
        Directory.CreateDirectory(Path.Combine(_root, "modules"));
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(($"maps/test/{Start}.sec", Sector(Record(Id(2, 11)))), ("maps/test/map.prp", Properties()),
            ("rules/maplist.mes", "{5000}{test, 64, 64}"u8.ToArray())));
        if (prototypeLoose) Loose("data/proto/007001 - test.pro", Prototype());
    }

    /// <summary>arcanum1.dat with every name table a whole-map plan binds, the tile ART and the given entries.</summary>
    private static byte[] Base(params (string Name, byte[] Payload)[] extra)
    {
        var entries = new List<(string, byte[])>
        {
            ("art/container/container.mes", Table), ("art/facade/facadename.mes", Table), ("art/item/item_ground.mes", Table),
            ("art/monster/monster.mes", Table), ("art/portal/portal.mes", Table), ("art/scenery/scenery.mes", Scenery),
            ("art/tile/stonebse0a.art", Art()), ("art/tile/tilename.mes", Tile), ("art/unique_npc/unique_npc.mes", Table),
            ("art/wall/structure.mes", Table), ("art/wall/wallname.mes", Table),
        };
        entries.AddRange(extra);
        return Dat([.. entries.OrderBy(e => e.Item1, StringComparer.Ordinal)]);
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Loose(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] Properties()
    {
        var prp = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(prp, 2); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), 64); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 64);
        return prp;
    }

    /// <summary>As <see cref="AreaIdentityTests.Dat"/> with each entry's record flags.</summary>
    private static byte[] FlaggedDat(params (string Name, byte[] Payload, uint Flags)[] entries)
    {
        using var s = new MemoryStream(); var offsets = new List<int>();
        foreach (var e in entries) { offsets.Add((int)s.Position); s.Write(e.Payload); }
        var start = s.Position; I32(s, entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Encoding.ASCII.GetBytes(entries[i].Name + "\0"); I32(s, name.Length); s.Write(name);
            I32(s, 0); I32(s, (int)entries[i].Flags); I32(s, entries[i].Payload.Length); I32(s, entries[i].Payload.Length); I32(s, offsets[i]);
        }
        var length = s.Position - start; s.Write(new byte[16]); s.Write("1TAD"u8); I32(s, 0); I32(s, checked((int)length + 28)); return s.ToArray();
    }
}
