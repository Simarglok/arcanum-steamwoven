using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-141 (MIG13 S8c, goal-table.md §3.5, GT59–GT62): the knockback host over a session and a fake body set. The push is a goal run of the pushed object (43, then 64 pushed), stepped on the animation clock
/// in ticks of 35 ms, four tiles long, ended by a block (which bumps the object), by the line's end or by an interrupt. No original data.
/// </summary>
[TestClass]
public sealed class AreaKnockbackHostTests
{
    private const string Npc = "oid:pushed";
    private static readonly CellCoord Home = new(100, 100), Source = new(90, 100);

    private sealed class Bodies : IAreaKnockbackViewer
    {
        public CellCoord? At = Home;
        public int Facing = 5;
        public Func<CellCoord, bool> OnMap = _ => true;
        public Func<CellCoord, CellCoord, KnockbackBlock> Blocks = (_, _) => KnockbackBlock.None;
        public readonly List<(CellCoord Tile, int Ms)> Slides = [];
        public readonly List<int> Bumps = [];
        public CellCoord? Tile(string target) => target == Npc ? At : null;
        public int Rotation(string target) => Facing;
        public bool Contains(CellCoord tile) => OnMap(tile);
        public KnockbackBlock Block(string target, CellCoord left, CellCoord tested) => Blocks(left, tested);
        public void Slide(string target, CellCoord tile, int ms) { At = tile; Slides.Add((tile, ms)); }
        public void Bump(string target, int maximum) => Bumps.Add(maximum);
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public Bodies Bodies { get; } = new();
        public AreaKnockbackHost Host { get; }
        public FrameStep Step { get; }
        public List<KnockbackFinished> Ends { get; } = [];

        public Fixture()
        {
            Host = new AreaKnockbackHost(Session, Bodies, (_, end) => Ends.Add(end));
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, () => Host) };
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void AStartCreatesARunOfThePushedObjectWithPriorityFive()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        var run = f.Session.QueryGoals().RunsOf(Npc).Single();
        Assert.AreEqual(GoalType.Knockback, run.Bottom.Goal);
        Assert.AreEqual(new CellCoord(104, 100), run.Bottom.Tile, "four tiles away from the source");
        Assert.IsTrue(f.Host.Owns(Npc));
        Assert.AreEqual(0, f.Bodies.Slides.Count, "the first step comes a moment later");
    }

    [TestMethod]
    public void TheObjectSlidesFourTilesTileByTileAndTheRunEnds()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        f.Run(2000);
        CollectionAssert.AreEqual(new[] { new CellCoord(101, 100), new CellCoord(102, 100), new CellCoord(103, 100), new CellCoord(104, 100) }, f.Bodies.Slides.Select(s => s.Tile).ToArray());
        Assert.IsTrue(f.Bodies.Slides.All(s => s.Ms == KnockbackRules.TickMs));
        Assert.AreEqual(KnockbackOutcome.Exhausted, f.Ends.Single().Outcome);
        Assert.AreEqual(new CellCoord(104, 100), f.Ends.Single().Tile);
        Assert.AreEqual(0, f.Bodies.Bumps.Count, "no collision, no damage");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the run ended");
        Assert.AreEqual(0, f.Host.Count);
    }

    [TestMethod]
    public void ABlockLeavesTheObjectOnItsTileAndBumpsItWithTheStepsLeft()
    {
        using var f = new Fixture();
        f.Bodies.Blocks = (_, tested) => tested.X == 103 ? KnockbackBlock.Terrain : KnockbackBlock.None;
        Assert.IsTrue(f.Host.Start(Npc, Source));
        f.Run(2000);
        Assert.AreEqual(KnockbackOutcome.Blocked, f.Ends.Single().Outcome);
        Assert.AreEqual(new CellCoord(102, 100), f.Ends.Single().Tile, "it stands on the tile it left");
        Assert.AreEqual(1, f.Bodies.Bumps.Count);
        Assert.IsTrue(f.Bodies.Bumps[0] >= 1);
        Assert.AreEqual(f.Ends.Single().BumpStepsLeft, f.Bodies.Bumps[0]);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AStepOffTheMapRefusesTheStartAndLeavesNoRun()
    {
        using var f = new Fixture();
        f.Bodies.OnMap = tile => tile.X < 104;
        Assert.IsFalse(f.Host.Start(Npc, Source));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(0, f.Host.Count);
    }

    [TestMethod]
    public void AnObjectThatCannotBePushedRefusesTheStart()
    {
        using var f = new Fixture();
        Assert.IsFalse(f.Host.Start("oid:other", Source));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void ASecondPushWhileTheFirstIsAtItsStartIsRefusedByPriorityFive()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        Assert.IsFalse(f.Host.Start(Npc, Source), "another knockback is refused while 43 is current (GT59)");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void ALevelThreeStartInterruptsTheSlideMidwayAndLeavesTheObjectWhereItIs()
    {
        // GT62: during the slide the current goal is 64 (priority 3), so a level-3 interrupt (a walk click, a round's start sweep) ends the run with no end node.
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        f.Run(80);
        Assert.IsTrue(f.Bodies.Slides.Count is > 0 and < 4, "mid-slide: " + f.Bodies.Slides.Count);
        var change = f.Session.InterruptGoals(Npc, 3);
        foreach (var end in change.Steps.Select(s => s.Ended).OfType<GoalRunEnd>()) f.Host.EndNodes(end);
        Assert.AreEqual(KnockbackOutcome.Interrupted, f.Ends.Single().Outcome);
        Assert.AreEqual(f.Bodies.Slides[^1].Tile, f.Ends.Single().Tile);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        f.Run(500);
        Assert.AreEqual(f.Bodies.Slides[^1].Tile, f.Bodies.At, "no further slide");
    }

    [TestMethod]
    public void AMapChangeEndsThePushThroughTheSessionsEnds()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        f.Run(60);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.PlaceActor(f.Session.Version, new(new SessionMapId(77), new(5, 5), NeighborDirection.XPlus)).Disposition);
        foreach (var end in f.Session.DrainGoalEnds()) f.Host.EndNodes(end);
        Assert.AreEqual(KnockbackOutcome.Interrupted, f.Ends.Single().Outcome);
        Assert.AreEqual(0, f.Host.Count);
    }

    [TestMethod]
    public void ARunWithNoHostEndsInTheRouter()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Npc, Source));
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, () => null);
        var run = f.Session.QueryGoals().RunsOf(Npc).Single();
        Assert.AreEqual(SessionGoalStepAnswer.End, router.StepGoal(run, default));
        Assert.AreEqual(1, router.KnockbacksLost);
    }

    [TestMethod]
    public void ThePushedObjectsOwnRotationPushesItBackwardOnTheSameTile()
    {
        using var f = new Fixture();
        f.Bodies.Facing = 5;
        Assert.IsTrue(f.Host.Start(Npc, Home));
        Assert.AreEqual(new CellCoord(96, 100), f.Session.QueryGoals().RunsOf(Npc).Single().Bottom.Tile);
    }
}
