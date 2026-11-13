using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-SCRIPT-1780 opt-in (<c>DAT_ROOT</c>, the game folder): the census behind ledger AN16 and AN17. Does any run of the script action change art number (59) reach a unique NPC (art class 13)? The first test counts the art
/// classes of the NPC prototypes and of the placed NPC rows (the 80 whole maps and the main map: static sector rows and the mobile aggregate; the instance's art field over its prototype's). The second reads every installed script
/// that uses the action and names the object each use writes: a looped object (the loop's kind), the attachee (where the script is placed), the extra object (the names it is tested for) or the triggerer; numbers only.
/// </summary>
[TestClass]
public sealed class ObjectArtCensusRealDataTests
{
    private const int ArtSlot = 0, NameSlot = 21, ScriptsSlot = 31, ChangeArtNumber = 59, LoopFor = 18, ChangeScript = 99;

    /// <summary>The pinned result of the installed 1.0.7.4 data (2026-11-14): NPC prototypes by art class (2, 12, 13), the maps and sector files read and the placed NPC rows by art class.</summary>
    private static readonly int[] ProtoClasses = [232, 184, 81];
    private const int PlacedMaps = 81, PlacedSectorFiles = 1680, PlacedRows = 3792, PlacedClass2 = 1009, PlacedClass4 = 21, PlacedClass12 = 1975, PlacedClass13 = 787;
    /// <summary>The action's uses: scripts and clauses.</summary>
    private const int UserScripts = 51, UserClauses = 63;

    private static (ContentStack Stack, WorldCatalog Catalog) Open()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var stack = ContentStack.Open(discovery.Plan!).Stack!;
        return (stack, WorldCatalog.Open(stack));
    }

    private static int ArtClass(WorldFieldValue? art) => art is { Number: { } aid } ? (int)(unchecked((uint)aid) >> 28) : -1;

    [TestMethod, TestCategory("RealData")]
    public void Unique_NPC_art_is_common_among_the_installed_NPCs()
    {
        var (stack, catalog) = Open();
        using var _ = stack;
        var prototypes = new Dictionary<int, WorldObject?>();
        var protoClasses = new SortedDictionary<int, int>();
        foreach (var number in catalog.PrototypeNumbers.OrderBy(n => n))
        {
            var prototype = catalog.ReadPrototype(number).Prototype;
            prototypes[number] = prototype;
            if (prototype is not { Type: AreaNpcRouteReader.NpcType }) continue;
            var cls = ArtClass(prototype.Fields.GetValueOrDefault(ArtSlot));
            protoClasses[cls] = protoClasses.GetValueOrDefault(cls) + 1;
        }
        Console.WriteLine($"ARC-SCRIPT-1780 NPC prototype art classes: {string.Join(", ", protoClasses.Select(p => $"{p.Key}={p.Value}"))}");
        CollectionAssert.AreEqual(new[] { 2, 12, 13 }, protoClasses.Keys.ToArray(), "the NPC prototype art classes changed");
        CollectionAssert.AreEqual(ProtoClasses, protoClasses.Values.ToArray(), "the NPC prototype art class counts changed");

        WorldFieldValue? Effective(WorldObject item, int slot)
        {
            if (item.Fields.TryGetValue(slot, out var own)) return own;
            if (item.PrototypeNumber is not { } number) return null;
            if (!prototypes.TryGetValue(number, out var prototype)) prototypes[number] = prototype = catalog.ReadPrototype(number).Prototype;
            return prototype is not null && prototype.Fields.TryGetValue(slot, out var held) ? held : null;
        }
        var placed = new SortedDictionary<int, int>();
        int maps = 0, files = 0, refused = 0, rows = 0;
        void Count(WorldObject item)
        {
            if (item.Type != AreaNpcRouteReader.NpcType) return;
            rows++;
            var cls = ArtClass(Effective(item, ArtSlot));
            placed[cls] = placed.GetValueOrDefault(cls) + 1;
        }
        foreach (var map in catalog.Maps)
        {
            maps++;
            foreach (var coordinate in map.SectorFiles)
            {
                var read = catalog.ReadSector(map, coordinate);
                if (read.Sector is not { } sector || sector.ObjectIssue is not null) { refused++; continue; }
                files++;
                foreach (var item in sector.Objects) Count(item);
            }
            foreach (var item in catalog.ReadMobiles(map).Objects) Count(item);
        }
        Console.WriteLine($"ARC-SCRIPT-1780 maps={maps} sector-files={files} refused={refused} npc-rows={rows}; placed NPC art classes: {string.Join(", ", placed.Select(p => $"{p.Key}={p.Value}"))}");
        Assert.AreEqual(0, refused, "a sector file was refused: the census would be short");
        Assert.AreEqual((PlacedMaps, PlacedSectorFiles, PlacedRows), (maps, files, rows), "the installed data's NPC rows changed");
        Assert.AreEqual((PlacedClass2, PlacedClass4, PlacedClass12, PlacedClass13), (placed.GetValueOrDefault(2), placed.GetValueOrDefault(4), placed.GetValueOrDefault(12), placed.GetValueOrDefault(13)), "the placed NPC rows' art classes changed");
    }

    [TestMethod, TestCategory("RealData")]
    public void No_installed_change_art_number_run_names_a_unique_NPC()
    {
        var (stack, catalog) = Open();
        using var _ = stack;
        var content = ScriptContent.Over(stack);
        var programs = content.Names.Names.Select(n => (n.Number, Program: content.Script(n.Number).Program)).Where(p => p.Program is not null).ToDictionary(p => p.Number, p => p.Program!);

        // every use: its script, line and the object operand's kind
        var uses = new List<(int Script, int Line, int Kind)>();
        foreach (var (number, program) in programs)
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                    if (clause.Type == ChangeArtNumber) uses.Add((number, line.Number, clause.Kind(0)));
        var users = uses.Select(u => u.Script).ToHashSet();
        Console.WriteLine($"ARC-SCRIPT-1780 uses: scripts={users.Count} clauses={uses.Count} by kind: {string.Join(", ", uses.GroupBy(u => u.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
        Assert.AreEqual((UserScripts, UserClauses), (users.Count, uses.Count), "the installed uses of change art number changed");
        CollectionAssert.AreEqual(new[] { 0, 1, 10, 12 }, uses.Select(u => u.Kind).Distinct().Order().ToArray(), "a use names its object by another kind");

        // the looped uses: the nearest loop that starts above the line (the current looped object stays set after a loop ends or breaks, as in script 2561) runs over scenery (15, any scenery 16) or containers (17), never over critters (8)
        var loopKinds = new SortedSet<int>();
        foreach (var use in uses.Where(u => u.Kind == 10))
        {
            var lines = programs[use.Script].Lines;
            var open = lines.Where(l => l.Number < use.Line && l.OnTrue.Type == LoopFor).Select(l => l.Number).DefaultIfEmpty(-1).Max();
            Assert.AreNotEqual(-1, open, $"script {use.Script} line {use.Line}: a looped object with no loop");
            loopKinds.Add(lines[open].OnTrue.Kind(0));
        }
        CollectionAssert.AreEqual(new[] { 15, 16, 17 }, loopKinds.ToArray(), "a looped use runs over another kind of object");

        // no script that changes art is attached to an object by another script's change script (99)
        foreach (var (number, program) in programs)
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                    if (clause.Type == ChangeScript && clause.Kind(2) == 3) Assert.IsFalse(users.Contains(clause.Value(2)), $"script {number} line {line.Number} attaches the art script {clause.Value(2)}");

        // where the scripts sit (placed objects and prototypes; tile and sector scripts have no attachee) and which objects carry the names the extra-object uses test
        var prototypes = new Dictionary<int, WorldObject?>();
        WorldFieldValue? Effective(WorldObject item, int slot)
        {
            if (item.Fields.TryGetValue(slot, out var own)) return own;
            if (item.PrototypeNumber is not { } number) return null;
            if (!prototypes.TryGetValue(number, out var prototype)) prototypes[number] = prototype = catalog.ReadPrototype(number).Prototype;
            return prototype is not null && prototype.Fields.TryGetValue(slot, out var held) ? held : null;
        }
        var holderTypes = new Dictionary<int, SortedSet<int>>();
        var names = new HashSet<int> { 1216, 1219, 1671 };
        var namedTypes = new SortedSet<int>();
        void Look(WorldObject item)
        {
            if (Effective(item, ScriptsSlot) is { Presence: WorldFieldPresence.Array } scripts)
                foreach (var element in scripts.Elements)
                    if (element.Words.Count == 3 && users.Contains((int)element.Words[2]))
                    {
                        if (!holderTypes.TryGetValue((int)element.Words[2], out var set)) holderTypes[(int)element.Words[2]] = set = [];
                        set.Add(item.Type);
                    }
            if (Effective(item, NameSlot) is { Number: { } name } && names.Contains((int)name)) namedTypes.Add(item.Type);
        }
        foreach (var number in catalog.PrototypeNumbers) { if (catalog.ReadPrototype(number).Prototype is { } p) Look(p); }
        foreach (var map in catalog.Maps)
        {
            foreach (var coordinate in map.SectorFiles)
                if (catalog.ReadSector(map, coordinate).Sector is { } sector) foreach (var item in sector.Objects) Look(item);
            foreach (var item in catalog.ReadMobiles(map).Objects) Look(item);
        }
        var attachee = uses.Where(u => u.Kind == 1).Select(u => u.Script).Distinct().Order().ToArray();
        var unplaced = attachee.Where(s => !holderTypes.ContainsKey(s)).ToArray();
        var attacheeTypes = attachee.Where(holderTypes.ContainsKey).SelectMany(s => holderTypes[s]).Distinct().Order().ToArray();
        Console.WriteLine($"ARC-SCRIPT-1780 attachee uses: scripts={string.Join(",", attachee)}; holder object types={string.Join(",", attacheeTypes)}; scripts placed nowhere={string.Join(",", unplaced)}; names tested by the extra-object uses sit on object types={string.Join(",", namedTypes)}");
        // 2 container, 3 scenery; a script placed nowhere is called by another (1337 by the scenery scripts 1314, 1332, 1333, 2854-2856 with their attachee; 2206 by the container script 2208 and the item script 2978 with a looped
        // scenery) or is unplaced (1193, 2205)
        CollectionAssert.AreEqual(new[] { 2, 3 }, attacheeTypes, "an attachee use sits on another kind of object");
        CollectionAssert.AreEqual(new[] { 1193, 1337, 2205, 2206 }, unplaced, "the attachee uses placed nowhere changed");
        // the extra object is a container (2) or a scenery (3), never an NPC (16)
        CollectionAssert.AreEqual(new[] { 2, 3 }, namedTypes.ToArray(), "the names the extra-object uses test sit on another kind of object");
    }
}
