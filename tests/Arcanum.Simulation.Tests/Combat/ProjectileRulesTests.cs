using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-COMBAT-016: the original's projectile in flight (ledger PF rows) over a synthetic map.</summary>
[TestClass]
public sealed class ProjectileRulesTests
{
    private static bool Inside(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 64 && tile.Y < 64;

    private sealed class Map : IProjectileMap<string>
    {
        public CellCoord? TargetTile { get; set; }
        public readonly Dictionary<CellCoord, List<string>> Critters = [];
        public readonly Dictionary<(CellCoord, int), string> Blockers = [];
        public IEnumerable<string> LivingCritters(CellCoord tile) => Critters.GetValueOrDefault(tile) ?? [];
        public string? Obstruction(CellCoord from, int rotation) => Blockers.GetValueOrDefault((from, rotation));
    }

    private const string Hero = "hero", Target = "target";

    private static Func<int, int, int> Rolls(params int[] values)
    {
        var queue = new Queue<int>(values);
        return (min, max) =>
        {
            Assert.AreEqual((1, ProjectileRules.StrayDie), (min, max));
            return queue.Dequeue();
        };
    }

    private static readonly Func<int, int, int> NoRolls = (_, _) => throw new AssertFailedException("no roll expected");

    [TestMethod]
    public void TheDirectionCountsClockwiseFromUpTheScreen()
    {
        var at = new CellCoord(10, 10);
        Assert.AreEqual(0, ProjectileRules.Direction(at, at));
        Assert.AreEqual(0, ProjectileRules.Direction(at, new(9, 9)), "up the screen");
        Assert.AreEqual(8, ProjectileRules.Direction(at, new(9, 11)), "right");
        Assert.AreEqual(16, ProjectileRules.Direction(at, new(11, 11)), "down");
        Assert.AreEqual(24, ProjectileRules.Direction(at, new(11, 9)), "left");
        // (0, +1) is screen (+40, +20): 26.57 degrees from the right, sector 2, plus 8.
        Assert.AreEqual(10, ProjectileRules.Direction(at, new(10, 11)));
        // (+1, 0) is screen (−40, +20): 153.43 degrees, sector 13, plus 8.
        Assert.AreEqual(21, ProjectileRules.Direction(at, new(11, 10)));
    }

    [TestMethod]
    public void AScenerProjectileArtTakesItsRotationAndOneOfFourNumbers()
    {
        // Number 8 (bits 19..27), rotation and frame 0: the installed firearm projectile 0x40400000.
        const uint bullet = 0x40400000;
        Assert.AreEqual(bullet, ProjectileRules.FacedArt(bullet, 0));
        Assert.AreEqual(bullet | (5U << 11), ProjectileRules.FacedArt(bullet, 5));
        Assert.AreEqual((bullet + (1U << 19)) | (7U << 11), ProjectileRules.FacedArt(bullet, 15));
        Assert.AreEqual((bullet + (3U << 19)) | (7U << 11), ProjectileRules.FacedArt(bullet, 31));
        // Other bits stay; a number past 511 stays as it was.
        Assert.AreEqual(0x40000020U | (2U << 11), ProjectileRules.FacedArt(0x40000020, 2));
        Assert.AreEqual(0x4FF80000U | (1U << 11), ProjectileRules.FacedArt(0x4FF80000, 25));
        Assert.IsNull(ProjectileRules.FacedArt(0x60000000, 3), "an item art is not read");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProjectileRules.FacedArt(bullet, 32));
    }

    [TestMethod]
    public void TheFlightCrossesTheLineOfFiresTilesThenKeepsItsLine()
    {
        foreach (var (from, to) in new[] { (new CellCoord(10, 10), new CellCoord(10, 16)), (new(20, 20), new(14, 23)), (new(5, 30), new(12, 30)) })
        {
            var flight = ProjectileRules.Launch(from, to)!;
            Assert.AreEqual((from, 0L, 0L), (flight.Tile, flight.X, flight.Y));
            var crossings = new List<ProjectileCrossing>();
            for (var i = 0; i < 400 && crossings.Count < 40; i++)
            {
                var (next, crossing) = ProjectileRules.Step(flight, Inside);
                if (next is null) break;
                Assert.AreEqual(flight.Steps + 1, next.Steps);
                if (crossing is { } c) crossings.Add(c);
                flight = next;
            }
            var walk = LineOfFireRules.Walk(from, 0, 0, to, Inside)!;
            CollectionAssert.AreEqual(walk.Select(s => (s.From, s.To)).ToArray(), crossings.Take(walk.Count).Select(c => (c.Left, c.Entered)).ToArray(),
                "up to the destination the flight crosses the line of fire's tiles");
            Assert.AreEqual(0, crossings[0].Distance, "the start tile is left first");
            Assert.IsTrue(crossings.Count > walk.Count, "past the destination it flies on");
            foreach (var c in crossings) Assert.AreEqual(ProjectileRules.Distance(from, c.Left), c.Distance);
            // It flies on in the same direction: the distance from the start keeps growing.
            Assert.IsTrue(ProjectileRules.Distance(from, crossings[^1].Entered) > ProjectileRules.Distance(from, to));
        }
        Assert.IsNull(ProjectileRules.Launch(new(3, 3), new(3, 3)));
    }

    [TestMethod]
    public void AFlightLeavingTheMapEnds()
    {
        var flight = ProjectileRules.Launch(new(1, 1), new(0, 0))!;
        for (var i = 0; i < 100; i++)
        {
            var (next, _) = ProjectileRules.Step(flight, Inside);
            if (next is null) return;
            flight = next;
        }
        Assert.Fail("the flight did not end at the map's edge");
    }

    [TestMethod]
    public void AHitStrikesItsTargetWhereverItStandsOnTheLine()
    {
        var map = new Map { TargetTile = new(4, 4) };
        var hit = new ProjectileShot(true, false, 10);
        var destination = new CellCoord(6, 6);
        var early = ProjectileRules.Cross(hit, new(new(3, 3), new(4, 4), 1), destination, Target, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.None, false), (early.Strike, early.Ends));
        var struck = ProjectileRules.Cross(hit, new(new(4, 4), new(5, 5), 2), destination, Target, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.Target, Target, true), (struck.Strike, struck.Struck, struck.Ends));
        // Critters on the line are not rolled for while the shot carries a hit.
        map.Critters[new(3, 3)] = ["bystander"];
        Assert.AreEqual(ProjectileStrikeKind.None, ProjectileRules.Cross(hit, new(new(3, 3), new(4, 4), 1), destination, Target, Hero, map, NoRolls).Strike);
    }

    [TestMethod]
    public void AHitWhoseTargetLeftTheLineMissesPastTheDestination()
    {
        var map = new Map { TargetTile = new(9, 2) };
        var hit = new ProjectileShot(true, false, 10);
        var destination = new CellCoord(6, 6);
        // Leaving the destination: this crossing still reads the hit (the target is elsewhere), the shot flies on as a free miss.
        var left = ProjectileRules.Cross(hit, new(destination, new(7, 7), 4), destination, Target, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.None, new ProjectileShot(false, true, 10)), (left.Strike, left.Shot));
        // A target standing on the destination is still struck as it is left.
        map.TargetTile = destination;
        Assert.AreEqual(ProjectileStrikeKind.Target, ProjectileRules.Cross(hit, new(destination, new(7, 7), 4), destination, Target, Hero, map, NoRolls).Strike);
    }

    [TestMethod]
    public void AMissStrikesAnotherLivingCritterOneInThree()
    {
        var map = new Map { TargetTile = new(3, 3) };
        var miss = new ProjectileShot(false, false, 10);
        var destination = new CellCoord(6, 6);
        // The target and the attacker are skipped; each other critter rolls in list order until one rolls 1.
        map.Critters[new(3, 3)] = [Target, "a", Hero, "b", "c"];
        var rolled = ProjectileRules.Cross(miss, new(new(3, 3), new(4, 4), 1), destination, Target, Hero, map, Rolls(2, 1));
        Assert.AreEqual((ProjectileStrikeKind.Stray, "b"), (rolled.Strike, rolled.Struck));
        var none = ProjectileRules.Cross(miss, new(new(3, 3), new(4, 4), 1), destination, Target, Hero, map, Rolls(3, 2, 2));
        Assert.AreEqual(ProjectileStrikeKind.None, none.Strike);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProjectileRules.Cross(miss, new(new(3, 3), new(4, 4), 1), destination, Target, Hero, map, Rolls(4)));
        // Before the destination a missed shot does not meet obstructions.
        map.Blockers[(new(5, 5), 4)] = "wall";
        Assert.AreEqual(ProjectileStrikeKind.None, ProjectileRules.Cross(miss, new(new(5, 5), new(6, 6), 3), destination, Target, Hero, map, NoRolls).Strike);
    }

    [TestMethod]
    public void AFreeFlightMeetsObstructionsFirstAndEndsBeyondItsRange()
    {
        var map = new Map();
        var free = new ProjectileShot(false, true, 5);
        var destination = new CellCoord(6, 6);
        map.Blockers[(new(8, 8), LineOfFireRules.Rotation(new(8, 8), new(9, 9)))] = "wall";
        map.Critters[new(8, 8)] = ["bystander"];
        var wall = ProjectileRules.Cross(free, new(new(8, 8), new(9, 9), 5), destination, Target, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.Obstruction, "wall"), (wall.Strike, wall.Struck));
        Assert.AreEqual(ProjectileStrikeKind.None, ProjectileRules.Cross(free, new(new(7, 7), new(8, 8), 5), destination, Target, Hero, map, NoRolls).Strike,
            "at the range itself it flies on");
        var end = ProjectileRules.Cross(free, new(new(9, 9), new(10, 10), 6), destination, Target, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.RangeEnd, (string?)null, true), (end.Strike, end.Struck, end.Ends));
        // A stray strike comes before the range end.
        map.Critters[new(9, 9)] = ["far"];
        Assert.AreEqual(ProjectileStrikeKind.Stray, ProjectileRules.Cross(free, new(new(9, 9), new(10, 10), 6), destination, Target, Hero, map, Rolls(1)).Strike);
    }

    [TestMethod]
    public void TicksStepFourTimesUnlessTheWeaponSaysOtherwise()
    {
        Assert.AreEqual(12, ProjectileRules.StepsIn(3, 0));
        Assert.AreEqual(21, ProjectileRules.StepsIn(3, 7));
        Assert.AreEqual(0, ProjectileRules.StepsIn(-2, 4));
    }
}
