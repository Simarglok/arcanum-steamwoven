using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-DEV-141 (MIG13 S8c, goal-table.md GT59, GT61): the knockback's destination, line and tick as pure rules. No original data.</summary>
[TestClass]
public sealed class KnockbackRulesTests
{
    private static readonly CellCoord Target = new(100, 100);

    private static bool Everywhere(CellCoord _) => true;

    [TestMethod]
    public void ThePushGoesFourTilesAwayFromTheSource()
    {
        // The source west of the target (smaller x): the rotation from source to target is 5 (+x), so the target ends four tiles further east.
        Assert.AreEqual(new CellCoord(104, 100), KnockbackRules.Destination(new(90, 100), Target, 0, Everywhere));
        Assert.AreEqual(new CellCoord(96, 100), KnockbackRules.Destination(new(110, 100), Target, 0, Everywhere), "the opposite side pushes the other way");
        Assert.AreEqual(new CellCoord(104, 104), KnockbackRules.Destination(new(90, 90), Target, 0, Everywhere), "a diagonal");
    }

    [TestMethod]
    public void OnTheSameTileTheTargetIsPushedBackwardFromItsOwnRotation()
    {
        // The target's own rotation plus 4 (mod 8): facing rotation 5 (+x) is pushed to rotation 1 (-x).
        Assert.AreEqual(new CellCoord(96, 100), KnockbackRules.Destination(Target, Target, 5, Everywhere));
        Assert.AreEqual(new CellCoord(104, 100), KnockbackRules.Destination(Target, Target, 1, Everywhere));
        Assert.AreEqual(new CellCoord(104, 100), KnockbackRules.Destination(Target, Target, 9, Everywhere), "a rotation is taken modulo 8");
    }

    [TestMethod]
    public void AStepOffTheMapRefusesTheStart()
    {
        Assert.IsNull(KnockbackRules.Destination(new(90, 100), Target, 0, tile => tile.X < 104), "the fourth step leaves the map");
        Assert.IsNotNull(KnockbackRules.Destination(new(90, 100), Target, 0, tile => tile.X <= 104));
    }

    [TestMethod]
    public void TheLineIsTenPixelStepsAndRefusedBeyondOneHundred()
    {
        var line = KnockbackRules.Line(Target, new(104, 100));
        Assert.IsNotNull(line);
        Assert.IsTrue(line.Samples.Count > 4, "four tiles are many 10-pixel steps");
        Assert.IsNull(KnockbackRules.Line(Target, Target), "no line to the same tile");
        Assert.IsNull(KnockbackRules.Line(Target, new(130, 100)), "more than 100 steps is refused");
    }

    private const int East = 5;

    [TestMethod]
    public void ATickTakesFourStepsAndReportsTheTilesItEntered()
    {
        var flight = KnockbackRules.Line(Target, new(104, 100))!;
        var entered = new List<CellCoord>();
        var ticks = 0;
        KnockbackTick tick;
        do
        {
            tick = KnockbackRules.Tick(flight, Everywhere, East, (_, _) => KnockbackBlock.None);
            ticks++;
            Assert.IsTrue(tick.Flight.Next - flight.Next <= KnockbackRules.StepsPerTick);
            entered.AddRange(tick.Entered);
            flight = tick.Flight;
        } while (tick.Kind == KnockbackTickKind.Continue && ticks < 100);
        Assert.AreEqual(KnockbackTickKind.Exhausted, tick.Kind, "the line ran out");
        Assert.AreEqual(new CellCoord(104, 100), flight.Tile, "the object stands on the destination");
        Assert.AreEqual(flight.Samples.Count, flight.Next, "the slide ended in the step that took the last sample");
        CollectionAssert.AreEqual(new[] { new CellCoord(101, 100), new CellCoord(102, 100), new CellCoord(103, 100), new CellCoord(104, 100) }, entered, "tile by tile, in order");
        Assert.AreEqual((flight.Samples.Count + KnockbackRules.StepsPerTick - 1) / KnockbackRules.StepsPerTick, ticks, "four steps a tick");
    }

    [TestMethod]
    public void ABlockStopsTheObjectOnTheTileItLeftWithTheStepsLeftCounted()
    {
        var flight = KnockbackRules.Line(Target, new(104, 100))!;
        var asked = new List<(CellCoord Left, CellCoord Tested)>();
        KnockbackBlock Block(CellCoord left, CellCoord tested)
        {
            asked.Add((left, tested));
            return tested.X == 102 ? KnockbackBlock.Terrain : KnockbackBlock.None;
        }
        var before = flight;
        var tick = KnockbackRules.Tick(flight, Everywhere, East, Block);
        for (var i = 0; i < 20 && tick.Kind == KnockbackTickKind.Continue; i++)
        {
            before = tick.Flight;
            tick = KnockbackRules.Tick(before, Everywhere, East, Block);
        }
        Assert.AreEqual(KnockbackTickKind.Blocked, tick.Kind);
        Assert.AreEqual(new CellCoord(102, 100), tick.BlockedBy);
        Assert.AreEqual(new CellCoord(101, 100), tick.Flight.Tile, "it stays on the tile it left");
        Assert.IsTrue(tick.StepsLeft > 4, "a block two tiles from the end has much line left");
        Assert.AreEqual(tick.StepsLeft, KnockbackRules.BumpMaximum(tick.StepsLeft), "the damage rolls 1 to the steps left");
        Assert.IsTrue(asked.All(a => a.Tested == new CellCoord(a.Left.X + 1, a.Left.Y)), "the test looks one step from the tile left in the line's rotation");
    }

    [TestMethod]
    public void ACritterBlocksOnAnyTileChangeAndALastStepBlockRollsOne()
    {
        var flight = KnockbackRules.Line(Target, new(104, 100))!;
        var tick = KnockbackRules.Tick(flight, Everywhere, East, (_, tested) => tested.X == 101 ? KnockbackBlock.Critter : KnockbackBlock.None);
        for (var i = 0; i < 20 && tick.Kind == KnockbackTickKind.Continue; i++)
            tick = KnockbackRules.Tick(tick.Flight, Everywhere, East, (_, tested) => tested.X == 101 ? KnockbackBlock.Critter : KnockbackBlock.None);
        Assert.AreEqual(KnockbackTickKind.Blocked, tick.Kind, "a living critter blocks the first tile change, not only the last step");
        Assert.AreEqual(1, KnockbackRules.BumpMaximum(0), "a range under one still rolls 1");
        // A block on the very last sample counts that sample: one step left, both amounts exactly 1.
        var line = KnockbackRules.Line(Target, new(104, 100))!;
        Assert.AreEqual(1, KnockbackRules.StepsLeft(line with { Next = line.Samples.Count - 1 }), "the last sample counts itself");
        Assert.AreEqual(1, KnockbackRules.BumpMaximum(1));
    }

    [TestMethod]
    public void ANeighbourOffTheMapTestsNothing()
    {
        var flight = KnockbackRules.Line(Target, new(104, 100))!;
        var tick = KnockbackRules.Tick(flight, tile => tile.X < 105, East, (_, _) => KnockbackBlock.Terrain);
        Assert.AreEqual(KnockbackTickKind.Blocked, tick.Kind, "a neighbour on the map is tested");
        var asked = 0;
        var edge = KnockbackRules.Tick(flight, tile => tile.X <= 101, East, (_, _) => { asked++; return KnockbackBlock.Terrain; });
        // The neighbour of the tile left is off the map once the object stands on x=101: nothing is asked of it, so the object goes on (and its point then leaves the map).
        Assert.AreEqual(1, asked, "only the first tile change (onto 101) was tested");
        Assert.AreEqual(KnockbackTickKind.Blocked, edge.Kind);
    }

    [TestMethod]
    public void APointOffTheMapEndsTheSlide()
    {
        var flight = KnockbackRules.Line(Target, new(104, 100))!;
        var tick = KnockbackRules.Tick(flight, tile => tile.X < 102, East, (_, _) => KnockbackBlock.None);
        for (var i = 0; i < 20 && tick.Kind == KnockbackTickKind.Continue; i++) tick = KnockbackRules.Tick(tick.Flight, tile => tile.X < 102, East, (_, _) => KnockbackBlock.None);
        Assert.AreEqual(KnockbackTickKind.OffMap, tick.Kind);
    }
}
