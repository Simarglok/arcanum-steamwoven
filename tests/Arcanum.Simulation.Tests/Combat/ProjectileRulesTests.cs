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
        Assert.AreEqual(0x60001234U, ProjectileRules.FacedArt(0x60001234, 3), "an item art stays as it is (0x502930 returns class 6 unchanged)");
        Assert.AreEqual(0x60001234U, ProjectileRules.FacedArt(0x60001234, 31));
        Assert.IsNull(ProjectileRules.FacedArt(0x20000000, 3), "the other classes are not read");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProjectileRules.FacedArt(bullet, 32));
    }

    [TestMethod]
    public void AThrownNonWeaponIsDrawnOnASizeArc()
    {
        // 200 at the middle, 50 at the ends, 200 − 150 × |D − 2d| / D truncated in between.
        Assert.AreEqual(200, ProjectileRules.ThrownScale(10, 5));
        Assert.AreEqual(50, ProjectileRules.ThrownScale(10, 10), "d is not under D");
        Assert.AreEqual(50, ProjectileRules.ThrownScale(10, 12));
        Assert.AreEqual(50, ProjectileRules.ThrownScale(10, 0), "d under 1");
        Assert.AreEqual(50, ProjectileRules.ThrownScale(0, 0), "D under 1");
        Assert.AreEqual(200 - 150 * 8 / 10, ProjectileRules.ThrownScale(10, 1), "|10 − 2| = 8");
        Assert.AreEqual(200 - 150 * 6 / 10, ProjectileRules.ThrownScale(10, 8), "|10 − 16| = 6 (past the middle)");
        Assert.AreEqual(200 - 150 * 1 / 7, ProjectileRules.ThrownScale(7, 3), "truncated: 150 / 7 = 21");
        Assert.AreEqual(ProjectileRules.ThrownScale(9, 3), ProjectileRules.ThrownScale(9, 6), "symmetric about the middle");
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

    // ARC-COMBAT-018 (0x4B39B0, ledger PS rows): a bow shot from expert training flies as two projectiles; nothing else does.
    [TestMethod]
    public void ABowShotAtExpertFliesAsTwoProjectiles()
    {
        Assert.AreEqual(1, ProjectileRules.Projectiles(CombatSkill.Bow, SkillTraining.None));
        Assert.AreEqual(1, ProjectileRules.Projectiles(CombatSkill.Bow, SkillTraining.Apprentice));
        Assert.AreEqual(2, ProjectileRules.Projectiles(CombatSkill.Bow, SkillTraining.Expert));
        Assert.AreEqual(2, ProjectileRules.Projectiles(CombatSkill.Bow, SkillTraining.Master));
        Assert.AreEqual(1, ProjectileRules.Projectiles(CombatSkill.Firearms, SkillTraining.Master));
        Assert.AreEqual(1, ProjectileRules.Projectiles(CombatSkill.Throwing, SkillTraining.Master));
        Assert.AreEqual(1, ProjectileRules.Projectiles(CombatSkill.Melee, SkillTraining.Master));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProjectileRules.Projectiles((CombatSkill)7, SkillTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProjectileRules.Projectiles(CombatSkill.Bow, (SkillTraining)7));
    }

    // ARC-COMBAT-018 (0x4B2870): a shot without a target (a missed throw's scatter) ends on leaving its destination, before the
    // obstruction and stray tests of that tile; before it, it flies free.
    [TestMethod]
    public void AShotWithoutATargetEndsOnLeavingItsDestination()
    {
        var map = new Map { TargetTile = new(6, 6) };
        var free = new ProjectileShot(false, true, 3);
        var destination = new CellCoord(6, 6);
        map.Critters[destination] = ["bystander"];
        map.Blockers[(destination, LineOfFireRules.Rotation(destination, new(7, 7)))] = "wall";
        var end = ProjectileRules.Cross(free, new(destination, new(7, 7), 6), destination, null, Hero, map, NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.DestinationEnd, (string?)null, true, free), (end.Strike, end.Struck, end.Ends, end.Shot),
            "it ends there even beyond its range");
        // On the way it meets obstructions and strays, the critter that was its target included.
        map.Critters[new(4, 4)] = [Target, Hero];
        var stray = ProjectileRules.Cross(free, new(new(4, 4), new(5, 5), 2), destination, null, Hero, map, Rolls(1));
        Assert.AreEqual((ProjectileStrikeKind.Stray, Target), (stray.Strike, stray.Struck));
        map.Blockers[(new(5, 5), LineOfFireRules.Rotation(new(5, 5), new(6, 6)))] = "door";
        Assert.AreEqual(ProjectileStrikeKind.Obstruction, ProjectileRules.Cross(free, new(new(5, 5), new(6, 6), 2), destination, null, Hero, map, NoRolls).Strike);
        // Without a target a shot is a miss flying free (the scatter sets flag 0x20).
        Assert.ThrowsExactly<ArgumentException>(() => ProjectileRules.Cross(new ProjectileShot(false, false, 3), new(new(4, 4), new(5, 5), 2), destination, null, Hero, map, NoRolls));
        // ARC-COMBAT-2435 (TW11): a throw at a location that hit has no target either and strikes nothing on the way (no stray roll, no obstruction test); it ends on leaving its destination.
        var located = new ProjectileShot(true, true, 3);
        Assert.AreEqual(ProjectileStrikeKind.None, ProjectileRules.Cross(located, new(new(4, 4), new(5, 5), 2), destination, null, Hero, map, NoRolls).Strike);
        Assert.AreEqual(ProjectileStrikeKind.None, ProjectileRules.Cross(located, new(new(5, 5), new(6, 6), 2), destination, null, Hero, map, NoRolls).Strike, "the door on the way is not met");
        Assert.AreEqual(ProjectileStrikeKind.DestinationEnd, ProjectileRules.Cross(located, new(destination, new(7, 7), 6), destination, null, Hero, map, NoRolls).Strike);
        // A shot with a target still flies on past its destination as a miss.
        var past = ProjectileRules.Cross(new ProjectileShot(false, false, 10), new(destination, new(7, 7), 6), destination, Target, Hero, new Map(), NoRolls);
        Assert.AreEqual((ProjectileStrikeKind.None, true), (past.Strike, past.Shot.FreeFlight));
    }

    // ARC-COMBAT-019 (0x4B2690, 0x435A00): a boomerang's first end turns it back from the tile being left to the thrower's tile on a
    // new line, its outcome, range and facing kept.
    [TestMethod]
    public void ABoomerangTurnsBackFromTheTileItLeavesToTheThrower()
    {
        var thrower = new CellCoord(10, 10);
        var flight = ProjectileRules.Launch(thrower, new(14, 14))!;
        while (flight.Tile != new CellCoord(13, 13)) flight = ProjectileRules.Step(flight, Inside).Flight!;
        var shot = new ProjectileShot(true, false, 8);
        var (back, turned) = ProjectileRules.TurnBack(flight, shot, new(11, 10));
        Assert.AreEqual(shot with { Returning = true }, turned);
        Assert.AreEqual((new CellCoord(13, 13), new CellCoord(11, 10), new CellCoord(13, 13), 0L), (back!.Start, back.Destination, back.Tile, back.Steps));
        Assert.AreEqual(ProjectileRules.Launch(new(13, 13), new(11, 10))!.Samples.Count, back.Samples.Count, "the line of PF3 between the two tiles");
        var (none, still) = ProjectileRules.TurnBack(flight, shot, new(13, 13));
        Assert.AreEqual(((ProjectileFlight?)null, true), (none, still.Returning), "on the thrower's own tile there is no line: back at once");
    }

    // ARC-COMBAT-1971 (PF9, 0x424D90, 0x42CAC0, 0x4B2690): a boomerang turned with the hero on the tile it left returns through the one tile it entered and ends with the second end's script and event; with the hero on the tile it
    // entered the flight goal ends without context; any other hero tile flies back.
    [TestMethod]
    public void ABoomerangTurnedOnTheHeroTileEndsAtOnceWithOrWithoutItsSecondEnd()
    {
        var left = new CellCoord(13, 13);
        var entered = new CellCoord(14, 14);
        Assert.AreEqual(BoomerangTurnEnd.EndsWithScripts, ProjectileRules.TurnEndAt(left, entered, left), "the hero on the tile left: the one-tile return, its end runs");
        Assert.AreEqual(BoomerangTurnEnd.EndsBare, ProjectileRules.TurnEndAt(left, entered, entered), "the hero on the tile entered: node 3 pops, the goal's own end has no context");
        Assert.AreEqual(BoomerangTurnEnd.Flies, ProjectileRules.TurnEndAt(left, entered, new(10, 10)));
        Assert.AreEqual(BoomerangTurnEnd.EndsWithScripts, ProjectileRules.TurnEndAt(left, null, left), "a strike's end does not know the tile entered");
        Assert.AreEqual(BoomerangTurnEnd.Flies, ProjectileRules.TurnEndAt(left, null, entered));
    }

    // ARC-COMBAT-019 (0x4B2870, flag 0x2000): a returning boomerang strikes nothing, meets no obstruction, has no range and ends when it
    // leaves the tile it flies back to.
    [TestMethod]
    public void AReturningBoomerangStrikesNothingAndEndsAtItsDestination()
    {
        var map = new Map { TargetTile = new(4, 4) };
        map.Critters[new(4, 4)] = [Target, "bystander"];
        map.Blockers[(new(4, 4), LineOfFireRules.Rotation(new(4, 4), new(3, 3)))] = "wall";
        var back = new CellCoord(1, 1);
        foreach (var shot in new[] { new ProjectileShot(true, false, 2, true), new ProjectileShot(false, true, 2, true) })
        {
            var on = ProjectileRules.Cross(shot, new(new(4, 4), new(3, 3), 50), back, Target, Hero, map, NoRolls);
            Assert.AreEqual((ProjectileStrikeKind.None, (string?)null, shot), (on.Strike, on.Struck, on.Shot), "through the target's tile, a wall and beyond any range");
            var end = ProjectileRules.Cross(shot, new(back, new(0, 0), 3), back, Target, Hero, map, NoRolls);
            Assert.AreEqual((ProjectileStrikeKind.DestinationEnd, true), (end.Strike, end.Ends));
            Assert.AreEqual(ProjectileStrikeKind.DestinationEnd, ProjectileRules.Cross(shot, new(back, new(0, 0), 3), back, null, Hero, map, NoRolls).Strike,
                "with or without a target");
        }
    }
}
