using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-010: the drop's path over an invented sector (0,0) with open terrain and the given objects.</summary>
[TestClass]
public sealed class AreaDropMapTests
{
    private const int Container = 2, Npc = 16, Trap = 17;

    private static AreaObjectKey Key(int n) => new("s", 1, AreaSourceKind.Static, 0, 0, n, "x", 1);

    private sealed record Thing(int N, int Type, int X, int Y, bool Blocks, uint Flags = 0, uint TrapFlags = 0)
    {
        public AreaObjectRecord Row => new(Key(N), 1, Type, 0, "", "", "", "", AreaScope.Core, null, X, Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
            [new(18, AreaPresence.Value, AreaValueOrigin.Instance, Flags, [], null), new(36, AreaPresence.Value, AreaValueOrigin.Instance, TrapFlags, [], null)],
            true, true, true);
    }

    private static readonly AreaObjectKey Window = new("s", 1, AreaSourceKind.Static, 0, 0, 999, "window", 1);

    private static AreaDropMap Map(Thing[] things, (int X, int Y)[]? windows = null, HashSet<AreaObjectKey>? gone = null, HashSet<AreaObjectKey>? dead = null,
        HashSet<CellCoord>? live = null, HashSet<AreaObjectKey>? moved = null)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var objects = things.GroupBy(t => new AreaStepCell(t.X, t.Y)).ToDictionary(g => g.Key, g => g.Select(t => new AreaStepObjectEvaluation(Key(t.N), g.Key, t.Type,
            CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable,
            t.Blocks ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open, "TEST")).ToArray());
        var walls = (windows ?? []).ToDictionary(w => new AreaStepCell(w.X, w.Y),
            w => new[] { new AreaStepWallEdge(Window, new(w.X, w.Y), 5, true, AreaStepEdgeRule.WindowJump, "WINDOW_PIECE_JUMP") });
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain, objects, walls);
        return new(steps, things.Select(t => t.Row), moved ?? [], live ?? [], row => gone?.Contains(row.Key) == true, row => dead?.Contains(row.Key) == true);
    }

    private static CellCoord Walk(AreaDropMap map, CellCoord from, DropPath path, int count)
    {
        var cell = from;
        for (var i = 0; i < count; i++) cell = map.Step(cell, path.Rotations[i])!.Value;
        return cell;
    }

    [TestMethod]
    public void RotationAndStepFollowTheOriginalTables()
    {
        Assert.AreEqual(5, AreaDropMap.Rotation(new(3, 3), new(3, 3)), "no delta");
        Assert.AreEqual(5, AreaDropMap.Rotation(new(0, 0), new(4, 1)), "twice the minor delta below the major one: straight");
        Assert.AreEqual(4, AreaDropMap.Rotation(new(0, 0), new(4, 2)), "twice the minor delta reaching it: diagonal");
        Assert.AreEqual(7, AreaDropMap.Rotation(new(5, 5), new(4, 2)));
        Assert.AreEqual(1, AreaDropMap.Rotation(new(5, 5), new(1, 5)));
        var map = Map([]);
        Assert.AreEqual(new CellCoord(4, 4), map.Step(new(4, 4), 8), "direction 8 stays");
        Assert.AreEqual(new CellCoord(3, 3), map.Step(new(4, 4), 0));
        Assert.IsNull(map.Step(new(0, 4), 1), "off the map");
        Assert.IsNull(map.Step(new(63, 4), 5));
    }

    [TestMethod]
    public void AnOpenLineIsTheDirectAttempt()
    {
        var path = Map([]).Path(new(5, 5), new(9, 7), DropOwner.Npc);
        Assert.AreEqual((4, 0), (path.Result, path.Partial));
        CollectionAssert.AreEqual(new[] { 4, 5, 4, 5 }, path.Rotations.Take(4).ToArray());
        Assert.AreEqual(DropPath.None, Map([]).Path(new(5, 5), new(5, 5), DropOwner.Npc), "the owner's own tile");
    }

    [TestMethod]
    public void TheDestinationsGateRefusesSolidObjectsAndLivingCritters()
    {
        CellCoord from = new(5, 5), to = new(7, 5);
        Assert.AreEqual(DropPath.None, Map([new(1, Container, 7, 5, true)]).Path(from, to, DropOwner.Npc), "a container");
        Assert.AreEqual(2, Map([new(1, Container, 7, 5, false, Flags: 0x400)]).Path(from, to, DropOwner.Npc).Result, "a container that does not block");
        Assert.AreEqual(2, Map([new(1, Container, 7, 5, true)], gone: [Key(1)]).Path(from, to, DropOwner.Npc).Result, "a destroyed container");
        Assert.AreEqual(DropPath.None, Map([new(1, Npc, 7, 5, false)]).Path(from, to, DropOwner.Npc), "a living NPC");
        Assert.AreEqual(2, Map([new(1, Npc, 7, 5, false)], dead: [Key(1)]).Path(from, to, DropOwner.Npc).Result, "a dead NPC");
        Assert.AreEqual(2, Map([new(1, Npc, 7, 5, false)], moved: [Key(1)]).Path(from, to, DropOwner.Npc).Result, "a companion's left record");
        Assert.AreEqual(DropPath.None, Map([], live: [to]).Path(from, to, DropOwner.Npc), "the hero or a companion");
        Assert.AreEqual(2, Map([new(1, Npc, 7, 5, false, Flags: 0x400)]).Path(from, to, DropOwner.Npc).Result, "a critter that does not block");
    }

    [TestMethod]
    public void ABlockedDirectAttemptKeepsItsStepsAndTheSearchGoesAround()
    {
        // A container two steps along: the direct attempt completes one step; the search finds a way round of the cheapest cost.
        var map = Map([new(1, Container, 7, 5, true)]);
        CellCoord from = new(5, 5), to = new(8, 5);
        var path = map.Path(from, to, DropOwner.Npc);
        Assert.AreEqual(1, path.Partial);
        Assert.IsTrue(path.Result is > 3 and <= 8, $"result {path.Result}");
        Assert.AreEqual(to, Walk(map, from, path, path.Result));
        Assert.IsFalse(Enumerable.Range(0, path.Result).Select(i => Walk(map, from, path, i + 1)).Contains(new CellCoord(7, 5)));
        // Critters never block the drop's steps: an NPC in the way is walked through.
        var through = Map([new(2, Npc, 6, 5, true)]).Path(from, to, DropOwner.Npc);
        Assert.AreEqual((3, 0), (through.Result, through.Partial));
    }

    [TestMethod]
    public void TrapsStopTheDirectAttemptOnlyForTheOwnersTheyConcern()
    {
        CellCoord from = new(5, 5), to = new(8, 5);
        var hidden = new Thing(1, Trap, 6, 5, false);
        bool Avoids(Thing[] things, DropOwner who)
        {
            var map = Map(things);
            var path = map.Path(from, to, who);
            return path.Result > 0 && !Enumerable.Range(0, path.Result).Select(i => Walk(map, from, path, i + 1)).Contains(new CellCoord(6, 5));
        }
        Assert.IsTrue(Avoids([hidden], DropOwner.Npc), "an NPC on its own avoids every trap");
        Assert.AreEqual((3, 0), (Map([hidden]).Path(from, to, DropOwner.PcSide).Result, Map([hidden]).Path(from, to, DropOwner.PcSide).Partial),
            "the PC side avoids only a spotted trap");
        Assert.IsTrue(Avoids([hidden with { TrapFlags = 0x1000000 }], DropOwner.PcSide), "spotted");
        Assert.AreEqual(3, Map([hidden with { TrapFlags = 0x1000000 }]).Path(from, to, DropOwner.Npc).Result, "a trap marked for NPCs to ignore");
        Assert.AreEqual(3, Map([hidden]).Path(from, to, DropOwner.Object).Result, "an object avoids none");
        // The search pays 90 for the trap tile and walks round it.
        var map = Map([hidden]);
        var round = map.Path(from, to, DropOwner.Npc);
        Assert.AreEqual(to, Walk(map, from, round, round.Result));
        Assert.IsFalse(Enumerable.Range(0, round.Result).Select(i => Walk(map, from, round, i + 1)).Contains(new CellCoord(6, 5)));
    }

    [TestMethod]
    public void AWindowStopsTheDirectAttempt()
    {
        var map = Map([], windows: [(6, 5)]);
        var path = map.Path(new(5, 5), new(8, 5), DropOwner.Npc);
        Assert.AreEqual(1, path.Partial, "the step across the window piece ends the direct attempt");
        Assert.AreEqual(new CellCoord(8, 5), Walk(map, new(5, 5), path, path.Result));
    }

    [TestMethod]
    public void CrittersOnATileIncludeTheDeadAndTheLiveActors()
    {
        var map = Map([new(1, Npc, 6, 5, false), new(2, Npc, 7, 5, false), new(3, Container, 8, 5, true)], dead: [Key(1)], moved: [Key(2)], live: [new(9, 9)]);
        Assert.IsTrue(map.HasCritter(new(6, 5)), "dead");
        Assert.IsFalse(map.HasCritter(new(7, 5)), "moved away");
        Assert.IsFalse(map.HasCritter(new(8, 5)));
        Assert.IsTrue(map.HasCritter(new(9, 9)));
    }
}
