using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-012: the line of fire over an invented sector (0,0) with the given rows.</summary>
[TestClass]
public sealed class AreaLineOfFireTests
{
    private const int Wall = 0, Portal = 1, Container = 2, Item = 6, Npc = 16;
    private const uint Opaque = 0, ShootThrough = 0x20;

    private static AreaObjectKey Key(int n) => new("s", 1, AreaSourceKind.Static, 0, 0, n, "x", 1);

    // An ART id with the given rotation (bits 11–13) and piece or frame (bits 14–19).
    private static uint Art(int rotation, int piece) => (uint)((rotation << 11) | (piece << 14));

    private sealed record Thing(int N, int Type, int X, int Y, uint Flags = 0, uint Art = 0, uint Spell = 0)
    {
        public AreaObjectRecord Row => new(Key(N), 1, Type, 0, "", "", "", "", AreaScope.Core, null, X, Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
            [new(0, AreaPresence.Value, AreaValueOrigin.Instance, Art, [], null), new(18, AreaPresence.Value, AreaValueOrigin.Instance, Flags, [], null),
                new(19, AreaPresence.Value, AreaValueOrigin.Instance, Spell, [], null)],
            true, true, true);
    }

    private static AreaLineOfFire Map(Thing[] things, HashSet<AreaObjectKey>? gone = null, HashSet<AreaObjectKey>? dead = null,
        Dictionary<CellCoord, AreaObjectRecord>? live = null, HashSet<AreaObjectKey>? moved = null, Func<AreaObjectRecord, bool?>? open = null)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), null);
        return new(steps, things.Select(t => t.Row), moved ?? [], live ?? [], row => gone?.Contains(row.Key) == true,
            row => dead?.Contains(row.Key) == true, open ?? (_ => null));
    }

    private static AreaObjectRecord? Blocker(AreaLineOfFire map, CombatAnomalyProfile? profile = null) =>
        LineOfFireRules.Trace(new CellCoord(2, 2), 0, 0, new CellCoord(8, 2), map, profile ?? CombatAnomalyProfile.Steamwoven).Blocker;

    [TestMethod]
    public void TheMapIsTheStepEvidencesTiles()
    {
        var map = Map([]);
        Assert.IsTrue(map.Contains(new(0, 0)));
        Assert.IsFalse(map.Contains(new(64, 0)));
        Assert.IsNull(map.Step(new(0, 3), 1));
        Assert.AreEqual(new CellCoord(4, 4), map.Step(new(3, 3), 4));
    }

    [TestMethod]
    public void LivingCrittersAndSolidObjectsStopTheLineButItemsDeadAndRemovedOnesDoNot()
    {
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Npc, 5, 2, Opaque)]))?.Key);
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Container, 5, 2, Opaque)]))?.Key);
        Assert.IsNull(Blocker(Map([new(1, Item, 5, 2, Opaque)])), "items are not in the projectile's mask");
        Assert.IsNull(Blocker(Map([new(1, Npc, 5, 2, Opaque)], dead: [Key(1)])), "a dead critter passes");
        Assert.IsNull(Blocker(Map([new(1, Container, 5, 2, Opaque)], gone: [Key(1)])), "destroyed in the session");
        Assert.IsNull(Blocker(Map([new(1, Container, 5, 2, 0x1)])), "destroyed at T0");
        Assert.IsNull(Blocker(Map([new(1, Container, 5, 2, ShootThrough)])), "shoot-through only costs");
        // A companion's T0 record is moved to its live cell.
        var companion = new Thing(1, Npc, 5, 2, Opaque).Row;
        Assert.IsNull(Blocker(Map([new(1, Npc, 5, 2, Opaque)], moved: [Key(1)])));
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Npc, 5, 2, Opaque)], moved: [Key(1)], live: new() { [new(6, 2)] = companion }))?.Key);
    }

    [TestMethod]
    public void WallsDecideByTheirPieceAndPortalsByTheirState()
    {
        // A wall on (4,2)'s edge 5 (rotation 4 or 5 rounds up to 5) is crossed on the way to (8,2).
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Wall, 4, 2, Art: Art(4, 0))]))?.Key, "a solid piece");
        Assert.IsNull(Blocker(Map([new(1, Wall, 4, 2, Art: Art(4, 10))])), "a window piece on an edge step");
        Assert.IsNull(Blocker(Map([new(1, Wall, 4, 2, Art: Art(4, 22))])), "a doorway piece");
        Assert.IsNull(Blocker(Map([new(1, Wall, 4, 2, Art: Art(2, 0))])), "a wall on another edge");
        Assert.IsNull(Blocker(Map([new(1, Wall, 4, 2, ShootThrough, Art(4, 0), 0x100000)])), "a pass-walled wall takes the cover test");
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 0))]))?.Key, "a closed door");
        Assert.IsNull(Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 3))])), "a door open at T0 (frame 3)");
        Assert.IsNull(Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 0) | 0x200)])), "a busted door at T0");
        Assert.IsNull(Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 0))], open: _ => true)), "a door the session opened");
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 3))], open: _ => false))?.Key, "a door the session closed");
        Assert.IsNull(Blocker(Map([new(1, Portal, 4, 2, Opaque, Art(5, 0))], gone: [Key(1)])), "a door an attack destroyed");
        // A closed door on the target tile's near edge stops the line before the target (RA8).
        Assert.AreEqual(Key(1), Blocker(Map([new(1, Portal, 8, 2, Opaque, Art(1, 0)), new(2, Npc, 8, 2, Opaque)]))?.Key);
    }
}
