using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-287 (MIG13 tail T6, goal-table.md Tail T6): the hero pushed by a knockback. The grid session's relocation (a settled actor on another cell, facing and time kept, a step under way dropped) and the AI host's
/// hero ports (the scene's hero body slides the hero, the collision damage goes to the hero's own life) under the real knockback host. Invented rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaHeroPushTests
{
    private const string Pc = AiSchedulerRules.Pc;

    // ---- the grid session's relocation ----

    private static AreaStepSnapshot OpenSteps(int size = 32)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < size; x++) for (var y = 0; y < size; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private static AreaGridSession Grid(CellCoord start) =>
        new(OpenSteps(), SimulationTime.At(0), StepDuration.Of(1, 2), chart: new AreaChart(0, 0, start, "TEST"), start: start, facing: NeighborDirection.YPlus);

    [TestMethod]
    public void ARelocationPutsTheSettledActorOnTheCellWithItsFacingAndTimeKept()
    {
        var grid = Grid(new(5, 5));
        grid.Advance(SimulationTime.At(3), []);
        var time = grid.State.Time;
        Assert.IsTrue(grid.Relocate(new(9, 5)));
        Assert.AreEqual((new CellCoord(9, 5), MovementPhase.Settled, NeighborDirection.YPlus), (grid.State.CommittedCell, grid.State.Phase, grid.State.Facing));
        Assert.AreEqual(time, grid.State.Time);
        Assert.AreEqual(Grid(new(9, 5)).State.Anchor, grid.State.Anchor, "the cell's centre, as a session that starts there");
    }

    [TestMethod]
    public void AStepUnderWayIsDroppedAndTheNextStepStartsFromTheNewCell()
    {
        var grid = Grid(new(5, 5));
        var quarter = new SimulationTime(ExactScalar.Create(1, 4));
        grid.Advance(quarter, [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        Assert.AreNotEqual(MovementPhase.Settled, grid.State.Phase, "a step is under way");
        Assert.IsTrue(grid.Relocate(new(5, 9)));
        Assert.IsNull(grid.State.Segment);
        Assert.AreEqual(MovementPhase.Settled, grid.State.Phase);
        Assert.AreEqual(ActorIntent.Neutral, grid.State.LatestIntent, "a held key does not walk the pushed actor on");
        var next = grid.Advance(SimulationTime.At(1), [new InputBatch(quarter, [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        var started = next.Movement.Events.First(e => e.Kind == MovementEventKind.StepStarted);
        Assert.AreEqual(new CellCoord(5, 9), started.Key.From, "the next step leaves the cell the actor was put on");
    }

    [TestMethod]
    public void ACellOutsideTheChartOrTheAdmittedSectorsRefusesTheRelocationAndChangesNothing()
    {
        var grid = Grid(new(5, 5));
        var before = grid.State;
        Assert.IsFalse(grid.Relocate(new(AreaGridSession.MaximumChartCell + 1, 5)), "off the chart");
        Assert.IsFalse(grid.Relocate(new(70, 5)), "a sector the evidence does not admit");
        Assert.AreSame(before, grid.State);
    }

    // ---- the AI host's hero ports under the knockback host ----

    private sealed class Body : IAreaHeroPush
    {
        public CellCoord At = AreaAiRouteTests.HeroCell;
        public int Facing = 4;
        public bool Refuse;
        public readonly List<CellCoord> Slides = [];
        public int Rotation => Facing;
        public bool Slide(CellCoord tile) { if (Refuse) return false; At = tile; Slides.Add(tile); return true; }
    }

    private sealed class Fixture : IDisposable
    {
        public Body Body { get; } = new();
        public AreaKnockbackHost Host { get; }
        public AreaAiRouteTests.Scene Scene { get; }
        public FrameStep Step { get; }
        public List<KnockbackFinished> Ends { get; } = [];
        public List<GoalRunEnd> OtherEnds { get; } = [];

        public Fixture(bool withBody = true)
        {
            Scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), () => Body.At, heroPush: withBody ? Body : null);
            Host = new AreaKnockbackHost(Scene.Session, new AreaAiKnockbackBodies(() => Scene.Host), (_, end) => Ends.Add(end), OtherEnds.Add);
            Step = new FrameStep(Scene.Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, () => Host) };
        }

        public int HitPointDamage => Scene.Session.QueryHeroLife()!.Record.State.HitPointDamage;
        public int FatigueDamage => Scene.Session.QueryHeroLife()!.Record.State.FatigueDamage;
        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Scene.Session.Dispose();
    }

    [TestMethod]
    public void TheHeroCanBePushedOnlyWhenTheSceneGivesItsBody()
    {
        using var with = new Fixture();
        using var without = new Fixture(withBody: false);
        Assert.IsTrue(with.Scene.Host.CanBePushed(Pc));
        Assert.IsFalse(without.Scene.Host.CanBePushed(Pc));
        Assert.IsFalse(without.Host.Start(Pc, new(13, 20)), "no body: the start is refused and leaves no run");
        Assert.AreEqual(0, without.Scene.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AFreePushSlidesTheHeroFourTilesAwayFromTheSourceWithoutDamage()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Pc, new(13, 20)));
        Assert.IsTrue(f.Host.Owns(Pc));
        f.Run(2000);
        CollectionAssert.AreEqual(new[] { new CellCoord(15, 20), new CellCoord(16, 20), new CellCoord(17, 20), new CellCoord(18, 20) }, f.Body.Slides);
        Assert.AreEqual(KnockbackOutcome.Exhausted, f.Ends.Single().Outcome);
        Assert.AreEqual(new CellCoord(18, 20), f.Scene.Host.Tile(Pc));
        Assert.AreEqual((0, 0), (f.HitPointDamage, f.FatigueDamage), "no collision");
        Assert.AreEqual(0, f.Scene.Session.QueryGoals().InUseCount, "no run is left");
    }

    [TestMethod]
    public void APushFromTheHeroOwnTileSendsItBackwardAlongItsRotation()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Start(Pc, AreaAiRouteTests.HeroCell));
        f.Run(2000);
        Assert.AreEqual(4, f.Body.Slides.Count);
        Assert.AreEqual(f.Body.Slides[^1], f.Scene.Host.Tile(Pc));
        Assert.IsTrue(AiExecutorRules.Distance(AreaAiRouteTests.HeroCell, f.Body.Slides[^1]) == 4, "four tiles away from where it stood");
    }

    [TestMethod]
    public void ALivingCritterOnTheLineBlocksTheHeroAndTheCollisionHurtsTheHero()
    {
        using var f = new Fixture();
        // The NPC stands at (10, 20): pushing the hero west from a source east of it meets it on the fourth tile.
        Assert.IsTrue(f.Host.Start(Pc, new(15, 20)));
        f.Run(2000);
        var end = f.Ends.Single();
        Assert.AreEqual(KnockbackOutcome.Blocked, end.Outcome);
        Assert.AreEqual(new CellCoord(11, 20), end.Tile, "the hero stands on the tile it left");
        Assert.IsTrue(f.HitPointDamage > 0 || f.FatigueDamage > 0, "the hero took the collision damage");
    }

    [TestMethod]
    public void ABodyThatRefusesTheTileEndsThePushWhereTheHeroStandsWithoutDamage()
    {
        using var f = new Fixture();
        f.Body.Refuse = true;
        Assert.IsTrue(f.Host.Start(Pc, new(13, 20)));
        f.Run(2000);
        Assert.AreEqual(KnockbackOutcome.Interrupted, f.Ends.Single().Outcome);
        Assert.AreEqual(AreaAiRouteTests.HeroCell, f.Scene.Host.Tile(Pc));
        Assert.AreEqual((0, 0), (f.HitPointDamage, f.FatigueDamage));
    }

    [TestMethod]
    public void ThePushEndsTheHeroWalkAtLevelFiveAndALaterWalkCannotInterruptIt()
    {
        using var f = new Fixture();
        var walk = f.Scene.Session.StartGoal(new GoalStartRequest(Pc, GoalStarters.Walk, new GoalFrame(GoalType.MoveToTile, null, new CellCoord(20, 20)), GoalRunFlags.None, DueMs: 5, PaceMs: 100), new HeroStartPorts());
        Assert.IsTrue(walk.Started);
        Assert.IsTrue(f.Host.Start(Pc, new(13, 20)));
        Assert.AreEqual(GoalType.MoveToTile, f.OtherEnds.Single().Run.Bottom.Goal, "the walk's run was ended by the push's interrupt, its end nodes are the hero host's");
        var again = f.Scene.Session.StartGoal(new GoalStartRequest(Pc, GoalStarters.Walk, new GoalFrame(GoalType.MoveToTile, null, new CellCoord(20, 20)), GoalRunFlags.None, DueMs: 5, PaceMs: 100), new HeroStartPorts());
        Assert.IsFalse(again.Started, "a walk (level 3) cannot interrupt the push");
    }

    private sealed class HeroStartPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void TheCollisionDamageIsRolledOnTheHeroOwnLifeAndADeadHeroTakesNothing()
    {
        using var f = new Fixture();
        f.Scene.Host.PushBump(Pc, 5);
        Assert.IsTrue(f.HitPointDamage is >= 1 and <= 5 && f.FatigueDamage is >= 1 and <= 5, $"normal and fatigue rolled 1 to the steps left: {f.HitPointDamage}, {f.FatigueDamage}");
        var life = f.Scene.Session.QueryHeroLife()!;
        f.Scene.Session.ApplyDamage(f.Scene.Session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = life.Status.MaxHitPoints + 100 }, new SessionDamageParties(null, null));
        Assert.IsTrue(f.Scene.Session.QueryHeroLife()!.Status.IsDead);
        var (hit, fatigue, version) = (f.HitPointDamage, f.FatigueDamage, f.Scene.Session.Version);
        f.Scene.Host.PushBump(Pc, 5);
        Assert.AreEqual((hit, fatigue), (f.HitPointDamage, f.FatigueDamage), "a dead critter takes nothing (0x4B4784)");
        Assert.AreEqual(version, f.Scene.Session.Version, "and the session is not touched");
    }

    [TestMethod]
    public void ADyingHeroRefusesThePush()
    {
        // GT59: the starter refuses an object that is dying (goal 31), the hero's run of it included.
        using var f = new Fixture();
        var life = f.Scene.Session.QueryHeroLife()!;
        f.Scene.Session.ApplyDamage(f.Scene.Session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = life.Status.MaxHitPoints + 100 }, new SessionDamageParties(null, null));
        Assert.IsFalse(f.Host.Start(Pc, new(13, 20)));
        Assert.AreEqual(0, f.Body.Slides.Count);
    }
}
