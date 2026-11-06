using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-139 (MIG13 S8, goal-table.md §3.5, GT52, GT56–GT58, GT62): the spell-missile host over a session and a fake cast host. A missile is a created object that owns its goal run, stepped by the frame
/// step on the animation clock: ticks of 35 ms, a pace after each leg, the release at the destination (the target re-check and the BEGIN are the cast host's), a new leg for a target that moved, an end node
/// that destroys the missile and frees the cast's run unpaid. No original data.
/// </summary>
[TestClass]
public sealed class AreaMissileHostTests
{
    private const string Npc = "oid:npc";
    private static readonly CellCoord Start = new(100, 100), Near = new(104, 100);

    private sealed class Caster(GameSessionOwner owner) : ISpellMissileCaster
    {
        public readonly HashSet<int> Flying = [];
        public readonly List<string> Calls = [];
        public bool ReCheckFails;
        public GameSessionOwner Owner => owner;
        public IReadOnlyList<SpellCastEvent> Log { get; } = [];
        public bool MissileFlies(int run) => Flying.Contains(run);
        public SessionSpellRunResult? ArriveMissile(int run)
        {
            if (!Flying.Remove(run)) return null;
            Calls.Add("arrive " + run);
            return ReCheckFails ? null : new(GameSessionDisposition.Changed, GameSessionFailure.None, null, [], owner.Version);
        }
        public SessionSpellRunResult? LoseMissile(int run)
        {
            if (!Flying.Remove(run)) return null;
            Calls.Add("lose " + run);
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, [], owner.Version);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public Caster Caster { get; }
        public AreaMissileHost Host { get; }
        public FrameStep Step { get; }
        public List<MissileFinished> Ends { get; } = [];
        public CellCoord? TargetAt { get; set; } = Near;
        public bool MapShown { get; set; } = true;

        public Fixture()
        {
            Caster = new(Session);
            Host = new AreaMissileHost(Session, _ => MapShown ? _ => true : null, _ => TargetAt, Ends.Add);
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), () => Host) };
        }

        public bool Launch(int run, CellCoord? to = null, string? target = Npc, CellCoord? from = null)
        {
            Caster.Flying.Add(run);
            return Host.Launch(Caster, Npc, 1, from ?? Start, to ?? Near, run, 5, target, 0x1000, stepsPerTick: 4, paceMs: 100);
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void ALaunchedMissileIsAnObjectWithItsOwnRunThatFliesAndReleasesTheCastOnArrival()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(3));
        var missile = f.Host.Missiles.Single();
        Assert.AreEqual(missile.Entity, f.Session.QueryMissiles().Single().Entity, "a created object of the session");
        Assert.AreEqual(missile.Entity, f.Session.QueryGoals().RunsOf(missile.Entity).Single().Owner, "it owns its run");
        Assert.AreEqual(GoalType.ShootSpell, f.Session.QueryGoals().RunsOf(missile.Entity).Single().Bottom.Goal);
        Assert.AreEqual(0, f.Caster.Calls.Count, "nothing is released at the launch");
        var cells = new List<CellCoord>();
        for (var i = 0; i < 400 && f.Host.Count > 0; i++)
        {
            f.Step.AdvanceHost(20);
            if (f.Session.QueryMissile(missile.Entity) is { } now && (cells.Count == 0 || cells[^1] != now.Cell)) cells.Add(now.Cell);
        }
        Assert.AreEqual(0, f.Host.Count, "the missile ended");
        CollectionAssert.AreEqual(new[] { "arrive 3" }, f.Caster.Calls, "the release re-checks and begins the run");
        Assert.AreEqual(MissileOutcome.Arrived, f.Ends.Single().Outcome);
        Assert.IsNotNull(f.Ends.Single().Begun);
        Assert.IsTrue(cells.Count > 1 && cells[^1] == Near, "the object moved tile by tile to the destination: " + string.Join(" ", cells));
        Assert.IsNull(f.Session.QueryMissile(missile.Entity), "destroyed after the release (node 6)");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the run ended");
        Assert.IsTrue(missile.Legs >= 1 && missile.Steps > 0);
    }

    [TestMethod]
    public void AMissileLaunchedOnItsTargetReleasesAtOnce()
    {
        using var f = new Fixture();
        f.TargetAt = Start;
        Assert.IsTrue(f.Launch(1, to: Start));
        f.Run(40);
        Assert.AreEqual(MissileOutcome.Arrived, f.Ends.Single().Outcome);
        Assert.AreEqual(0, f.Ends.Single().Missile.Legs, "no leg flew");
    }

    [TestMethod]
    public void ATargetThatMovedIsFollowedOnANewLeg()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(2));
        var moved = new CellCoord(104, 103);
        f.Run(60);
        f.TargetAt = moved;
        f.Run(3000);
        Assert.AreEqual(MissileOutcome.Arrived, f.Ends.Single().Outcome);
        Assert.AreEqual(moved, f.Ends.Single().Missile.Flight.Tile, "the release is where the target stands now (B12)");
        Assert.IsTrue(f.Ends.Single().Missile.Legs >= 2, "a second leg flew");
    }

    [TestMethod]
    public void ARefusedLineLosesTheMissileAndFreesTheRunUnpaid()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(4, to: new CellCoord(160, 160), target: null));
        f.Run(100);
        Assert.AreEqual(MissileOutcome.Lost, f.Ends.Single().Outcome);
        Assert.AreEqual("the line is too long", f.Ends.Single().Why);
        CollectionAssert.AreEqual(new[] { "lose 4" }, f.Caster.Calls);
        Assert.AreEqual(0, f.Session.QueryMissiles().Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AFailedReCheckFreesTheRunThroughTheCastHostAndStillDestroysTheMissile()
    {
        using var f = new Fixture();
        f.Caster.ReCheckFails = true;
        Assert.IsTrue(f.Launch(6));
        f.Run(3000);
        Assert.AreEqual(MissileOutcome.Arrived, f.Ends.Single().Outcome);
        Assert.IsNull(f.Ends.Single().Begun);
        Assert.AreEqual(0, f.Session.QueryMissiles().Count);
        Assert.AreEqual(1, f.Ends.Count, "one end");
    }

    [TestMethod]
    public void ARoundsStartSweepEndsAFlyingMissileWithoutARelease()
    {
        // GT62: a missile on goal 78 (priority 3) is swept at level 3, so a round's start ends it and the spell is never released.
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(7));
        f.Run(60);
        var entity = f.Host.Missiles.Single().Entity;
        var change = f.Session.InterruptGoals(entity, 3);
        Assert.IsTrue(change.Steps.Any(s => s.Ended is not null), "the sweep ended the run");
        foreach (var end in change.Steps.Select(s => s.Ended).OfType<GoalRunEnd>()) f.Host.EndNodes(end);
        Assert.AreEqual(MissileOutcome.Lost, f.Ends.Single().Outcome);
        CollectionAssert.AreEqual(new[] { "lose 7" }, f.Caster.Calls);
        Assert.IsNull(f.Session.QueryMissile(entity));
    }

    [TestMethod]
    public void AMapChangeLosesTheMissileThroughItsEndNode()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(8));
        f.Run(60);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.PlaceActor(f.Session.Version, new(new SessionMapId(77), new(5, 5), NeighborDirection.XPlus)).Disposition);
        Assert.AreEqual(0, f.Session.QueryMissiles().Count, "the session cleared the objects with the map");
        foreach (var end in f.Session.DrainGoalEnds()) f.Host.EndNodes(end);
        Assert.AreEqual(MissileOutcome.Lost, f.Ends.Single().Outcome);
        CollectionAssert.AreEqual(new[] { "lose 8" }, f.Caster.Calls);
        Assert.AreEqual(0, f.Host.Count);
    }

    [TestMethod]
    public void AHostThatIsToldTheMapIsNotShownLosesItsMissilesAtTheirNextTick()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(9));
        f.MapShown = false;
        f.Run(200);
        Assert.AreEqual(MissileOutcome.Lost, f.Ends.Single().Outcome);
        Assert.AreEqual("the map or session changed", f.Ends.Single().Why);
    }

    [TestMethod]
    public void ARunFreedUnderTheMissileDropsItWithoutTouchingTheRun()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(10));
        f.Run(60);
        f.Caster.Flying.Remove(10);
        f.Run(200);
        Assert.AreEqual(MissileOutcome.Dropped, f.Ends.Single().Outcome);
        Assert.AreEqual(0, f.Caster.Calls.Count, "nothing of the run was touched");
        Assert.AreEqual(0, f.Session.QueryMissiles().Count);
    }

    [TestMethod]
    public void ALaunchInASlotThatAnEarlierMissileHeldDropsTheEarlierOne()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(11));
        var first = f.Host.Missiles.Single().Entity;
        Assert.IsTrue(f.Launch(11));
        Assert.AreEqual(1, f.Host.Count);
        Assert.AreNotEqual(first, f.Host.Missiles.Single().Entity);
        Assert.AreEqual(MissileOutcome.Dropped, f.Ends.Single().Outcome);
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(first).Length, "its run was killed");
    }

    [TestMethod]
    public void LoseAllFreesEveryRunAndClearsTheObjects()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(12));
        Assert.IsTrue(f.Launch(13));
        f.Host.LoseAll("the smoke ended");
        Assert.AreEqual(0, f.Host.Count);
        Assert.AreEqual(0, f.Session.QueryMissiles().Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the runs are killed");
        CollectionAssert.AreEquivalent(new[] { "lose 12", "lose 13" }, f.Caster.Calls);
    }

    [TestMethod]
    public void TheRouterGivesAMissileRunToTheMissileHostAndEndsOneWithNoHost()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Launch(14));
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), () => null);
        var run = f.Session.QueryGoals().RunsOf(f.Host.Missiles.Single().Entity).Single();
        Assert.IsNull(router.StepGoal(run, f.Session.QueryFrameClocks()).NextDelayMs, "no missile host: the run ends");
        Assert.AreEqual(1, router.MissilesLost);
    }
}
