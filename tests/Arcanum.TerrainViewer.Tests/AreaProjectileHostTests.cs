using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, goal-table.md §3.5, GT53–GT55, GT62): the projectile host over a session and a fake viewer. A projectile is a created object that owns its goal run (36), stepped by the frame step
/// on the animation clock: the line is built once by the path nodes, then a tick of steps every 35 ms leaves each tile (the viewer's crossing decides), an end pops the run and runs the end node. The strike, the item's
/// landing and the map are the viewer's. No original data.
/// </summary>
[TestClass]
public sealed class AreaProjectileHostTests
{
    private static readonly CellCoord Start = new(100, 100), Near = new(106, 100);

    private static readonly AreaObjectRecord Row = new(new("npc", 0, AreaSourceKind.Mobile, null, 0, 0, "npc", 0), 0, 15, 0, "", "", "", "",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private sealed class Viewer(AreaProjectileHost? host = null) : IAreaProjectileViewer
    {
        public AreaProjectileHost? Host { get; set; } = host;
        public bool MapShown = true;
        public readonly List<string> Calls = [];
        public readonly List<CellCoord> Left = [];
        // The tile the viewer ends the flight at when the projectile leaves it (the strike), and the tile a boomerang turns at (once).
        public CellCoord? EndAt, TurnAt;
        public CellCoord TurnTo;
        public Func<CellCoord, bool> Cells = _ => true;
        public readonly List<(bool Land, string Why)> Ended = [];

        public Func<CellCoord, bool>? LineMap(AreaProjectile projectile) => MapShown ? Cells : null;

        public ProjectileCrossResult Crossed(AreaProjectile projectile, ProjectileCrossing crossing)
        {
            Left.Add(crossing.Left);
            if (crossing.Left == EndAt && TurnAt is null) { Host!.Finish(projectile); Calls.Add("end " + crossing.Left); return ProjectileCrossResult.Ended; }
            if (TurnAt is { } at && crossing.Left == at)
            {
                TurnAt = null;
                projectile.Destination = TurnTo;
                projectile.State = projectile.State with { Returning = true };
                Calls.Add("turn " + crossing.Left);
                return ProjectileCrossResult.Turned;
            }
            return ProjectileCrossResult.Flies;
        }

        public void GoalEnded(AreaProjectile projectile, bool land, string why) { Ended.Add((land, why)); Calls.Add($"goal-end land={land}"); }
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public Viewer Viewer { get; } = new();
        public AreaProjectileHost Host { get; set; }
        public FrameStep Step { get; }
        private long _shots;

        public Fixture()
        {
            Host = new AreaProjectileHost(Session, Viewer);
            Viewer.Host = Host;
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, () => Host) };
        }

        public AreaProjectile Projectile(CellCoord from, CellCoord to, bool returns = false) => new()
        {
            Shot = new(++_shots), Owner = Session, Map = 1, Target = Row, Destination = to, Art = null,
            Flight = new(from, from, [(0, 0)], 1, from, 0, 0, 0), State = new(true, false, 30), Returns = returns,
        };

        public AreaProjectile Launch(CellCoord? to = null, CellCoord? from = null, bool returns = false)
        {
            var p = Projectile(from ?? Start, to ?? Near, returns);
            Assert.IsTrue(Host.Launch(p, from ?? Start, "oid:struck"));
            return p;
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void ALaunchedProjectileIsAnObjectWithItsOwnRunThatFliesTileByTileUntilTheViewerEndsIt()
    {
        using var f = new Fixture();
        f.Viewer.EndAt = Near;
        var p = f.Launch();
        Assert.AreEqual(p.Entity, f.Session.QueryProjectiles().Single().Entity, "a created object of the session");
        var run = f.Session.QueryGoals().RunsOf(p.Entity).Single();
        Assert.AreEqual((p.Entity, GoalType.Projectile), (run.Owner, run.Bottom.Goal));
        Assert.AreEqual(Near, run.Bottom.Tile);
        Assert.AreEqual("oid:struck", run.Bottom.Target, "the struck object sits in slot 1");
        Assert.AreEqual(0, f.Viewer.Left.Count, "nothing flies at the launch");
        var cells = new List<CellCoord>();
        for (var i = 0; i < 400 && f.Host.Count > 0; i++)
        {
            f.Step.AdvanceHost(20);
            if (f.Session.QueryProjectile(p.Entity) is { } now && (cells.Count == 0 || cells[^1] != now.Cell)) cells.Add(now.Cell);
        }
        Assert.AreEqual(0, f.Host.Count, "the projectile ended");
        Assert.AreEqual(Start, f.Viewer.Left[0], "the start tile is left first (PF4)");
        Assert.AreEqual(Near, f.Viewer.Left[^1]);
        CollectionAssert.AreEqual(new[] { "end " + Near }, f.Viewer.Calls, "the viewer's end is the only end: the goal's own end node finds nothing left to land");
        Assert.IsTrue(cells.Count > 1, "the object moved along the line: " + string.Join(" ", cells));
        Assert.IsNull(f.Session.QueryProjectile(p.Entity), "destroyed at its end");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the run ended");
    }

    [TestMethod]
    public void TheFlightTakesFourStepsATickAndATickEvery35ms()
    {
        using var f = new Fixture();
        f.Viewer.Cells = _ => true;
        var far = new CellCoord(112, 100);
        var p = f.Launch(to: far);
        // The first tick is due 5 ms after the start; then one tick per 35 ms of the animation clock, each of StepsPerTick steps.
        f.Run(200);
        var ticks = p.Ticks;
        Assert.IsTrue(ticks >= 4, $"ticks so far: {ticks}");
        Assert.AreEqual(ticks * p.StepsPerTick, p.Flight.Steps, "exactly four steps in every tick");
        f.Run(35 * 3 + 5);
        Assert.IsTrue(p.Ticks - ticks is >= 3 and <= 4, $"one tick per 35 ms ({p.Ticks - ticks} in about 105 ms)");
        Assert.AreEqual(p.Ticks * p.StepsPerTick, p.Flight.Steps);
    }

    [TestMethod]
    public void AProjectileStartedOnItsDestinationPopsAtOnceAndOnlyTheGoalsEndNodeSpeaks()
    {
        using var f = new Fixture();
        var p = f.Launch(to: Start);
        f.Run(60);
        CollectionAssert.AreEqual(new[] { (true, "the flight goal ended") }, f.Viewer.Ended);
        Assert.AreEqual(0, f.Viewer.Left.Count, "no tile was left: no strike, no script (GT53)");
        Assert.AreEqual(0, p.Ticks);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AFlightOffTheMapEndsThroughTheEndNodeWithTheItemLanding()
    {
        using var f = new Fixture();
        f.Viewer.Cells = cell => cell.X <= 102;
        f.Launch(to: new CellCoord(110, 100));
        f.Run(500);
        Assert.AreEqual(1, f.Viewer.Ended.Count);
        Assert.IsTrue(f.Viewer.Ended[0].Land, "the goal ended by itself: the thrown item is placed on the projectile's tile (0x4B2690)");
        Assert.AreEqual(0, f.Host.Count);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count);
    }

    [TestMethod]
    public void AHostToldTheMapIsNotShownGivesItsProjectilesUpWithNoLanding()
    {
        using var f = new Fixture();
        f.Launch();
        f.Viewer.MapShown = false;
        f.Run(300);
        CollectionAssert.AreEqual(new[] { (false, "the map or session changed") }, f.Viewer.Ended);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the run popped on its own");
    }

    [TestMethod]
    public void AMapChangeEndsTheRunAndGivesTheProjectileUpThroughItsEndNode()
    {
        using var f = new Fixture();
        var p = f.Launch();
        f.Run(60);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.PlaceActor(f.Session.Version, new(new SessionMapId(77), new(5, 5), NeighborDirection.XPlus)).Disposition);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count, "the session cleared the objects with the map");
        foreach (var end in f.Session.DrainGoalEnds()) f.Host.EndNodes(end);
        CollectionAssert.AreEqual(new[] { (false, "its run ended") }, f.Viewer.Ended, "given up: the item stays where it was thrown from");
        Assert.AreEqual(0, f.Host.Count);
        Assert.IsFalse(f.Host.Owns(p));
    }

    [TestMethod]
    public void ARoundsStartSweepAtLevelThreeDoesNotEndAFlyingProjectileButTheMapsCloseLevelDoes()
    {
        // GT62: a projectile's run is priority 5 for its whole flight; the round-start sweep (level 3) and the stun/knock-down levels (4, 5) are refused.
        using var f = new Fixture();
        var p = f.Launch();
        f.Run(60);
        foreach (var level in new[] { 3, 4, 5 })
            Assert.IsFalse(f.Session.InterruptGoals(p.Entity, level).Steps.Any(s => s.Ended is not null), $"level {level} refused");
        Assert.AreEqual(1, f.Host.Count);
        var close = f.Session.InterruptGoals(p.Entity, 6);
        Assert.IsTrue(close.Steps.Any(s => s.Ended is not null), "level 6 ends it");
        foreach (var end in close.Steps.Select(s => s.Ended).OfType<GoalRunEnd>()) f.Host.EndNodes(end);
        CollectionAssert.AreEqual(new[] { (false, "its run ended") }, f.Viewer.Ended);
    }

    [TestMethod]
    public void AnObjectDestroyedUnderItsRunGivesTheFlightUp()
    {
        using var f = new Fixture();
        var p = f.Launch();
        f.Run(40);
        Assert.IsTrue(f.Session.DestroyProjectile(p.Entity));
        f.Run(300);
        CollectionAssert.AreEqual(new[] { (false, "its object was destroyed") }, f.Viewer.Ended);
        Assert.AreEqual(0, f.Host.Count);
    }

    [TestMethod]
    public void ABoomerangsTurnFliesTheLineAnewToTheThrowersTileAndTheViewerEndsItThere()
    {
        using var f = new Fixture();
        var back = new CellCoord(101, 100);
        f.Viewer.TurnAt = Near;
        f.Viewer.TurnTo = back;
        f.Viewer.EndAt = back;
        var p = f.Launch(returns: true);
        for (var i = 0; i < 600 && f.Host.Count > 0; i++) f.Step.AdvanceHost(20);
        CollectionAssert.AreEqual(new[] { "turn " + Near, "end " + back }, f.Viewer.Calls, string.Join(" | ", f.Viewer.Calls) + " left=" + string.Join(" ", f.Viewer.Left));
        Assert.AreEqual(back, p.Destination);
        Assert.IsTrue(p.State.Returning);
        Assert.IsTrue(f.Viewer.Left.Contains(back));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void LoseAllGivesEveryFlightUpAndClearsTheObjectsAndRuns()
    {
        using var f = new Fixture();
        f.Launch();
        f.Launch(from: new CellCoord(100, 103), to: new CellCoord(105, 103));
        f.Host.LoseAll("the smoke ended");
        Assert.AreEqual(0, f.Host.Count);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the runs are killed");
        Assert.AreEqual(2, f.Viewer.Ended.Count(e => !e.Land));
    }

    [TestMethod]
    public void TheRouterGivesAProjectileRunToTheProjectileHostAndEndsOneWithNoHost()
    {
        using var f = new Fixture();
        var p = f.Launch();
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, () => null);
        var run = f.Session.QueryGoals().RunsOf(p.Entity).Single();
        Assert.IsNull(router.StepGoal(run, f.Session.QueryFrameClocks()).NextDelayMs, "no projectile host: the run ends");
        Assert.AreEqual(1, router.ProjectilesLost);
    }

    [TestMethod]
    public void AFullObjectTableRefusesTheLaunchAndGivesTheShotUp()
    {
        using var f = new Fixture();
        for (var i = 0; i < SessionProjectile.MaxProjectiles; i++) Assert.IsNotNull(f.Session.CreateProjectile(new(i, 0)));
        var p = f.Projectile(Start, Near);
        Assert.IsFalse(f.Host.Launch(p, Start, null));
        CollectionAssert.AreEqual(new[] { (false, "the projectile could not be created") }, f.Viewer.Ended);
        Assert.AreEqual(0, f.Host.Count);
    }

    private sealed class OpenPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void AFullGoalTableRefusesTheLaunchLeavesNoObjectAndGivesTheShotUp()
    {
        using var f = new Fixture();
        for (var i = 0; i < GoalRunTable.SlotCount; i++)
        {
            var missile = f.Session.CreateMissile(new(i, 1))!;
            Assert.IsTrue(f.Session.StartGoal(new GoalStartRequest(missile, GoalStarters.Projectile, new GoalFrame(GoalType.Projectile, null, new CellCoord(i, 9))), new OpenPorts()).Started, $"run {i}");
        }
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count);
        var p = f.Projectile(Start, Near);
        Assert.IsFalse(f.Host.Launch(p, Start, null));
        Assert.AreEqual(1, f.Viewer.Ended.Count);
        Assert.IsFalse(f.Viewer.Ended[0].Land, "a shot whose run could not start is given up");
        Assert.AreEqual(0, f.Host.Count);
        Assert.AreEqual(0, f.Session.QueryProjectiles().Count, "its object is destroyed again");
    }

    [TestMethod]
    public void ARebuiltProjectileTakesTheFlightTheHostWroteAndFliesOnAsTheLiveOneWould()
    {
        // ARC-DEV-288: the host writes the flight into the session at the launch and every tick; a host that adopts the projectile rebuilt from that record (a load) flies the same line on.
        using var live = new Fixture();
        using var saved = new Fixture();
        var far = new CellCoord(112, 100);
        var a = live.Launch(far);
        var b = saved.Launch(far);
        Assert.AreEqual(b.Saved(), saved.Session.QueryProjectile(b.Entity)!.Flight, "the launch writes the flight");
        live.Run(120);
        saved.Run(120);
        var record = saved.Session.QueryProjectile(b.Entity)!;
        Assert.AreEqual(b.Saved(), record.Flight, "every tick writes the flight");
        Assert.IsTrue(record.Flight!.Steps > 0);
        var rebuilt = AreaProjectile.Rebuild(record, saved.Session, Row, null, null, null, null)!;
        Assert.AreEqual((b.Flight.Start, b.Flight.Destination, b.Flight.Next, b.Flight.Tile, b.Flight.X, b.Flight.Y, b.Flight.Steps),
            (rebuilt.Flight.Start, rebuilt.Flight.Destination, rebuilt.Flight.Next, rebuilt.Flight.Tile, rebuilt.Flight.X, rebuilt.Flight.Y, rebuilt.Flight.Steps));
        CollectionAssert.AreEqual(b.Flight.Samples.ToArray(), rebuilt.Flight.Samples.ToArray(), "the line is rebuilt from its two tiles");
        // The adopting host stands in for the one the load replaced.
        var adopting = new AreaProjectileHost(saved.Session, saved.Viewer);
        Assert.IsTrue(adopting.Adopt(rebuilt));
        Assert.IsFalse(adopting.Adopt(rebuilt), "adopted once");
        saved.Host = adopting;
        saved.Viewer.Host = adopting;
        var leftBefore = saved.Viewer.Left.Count;
        live.Run(400);
        saved.Run(400);
        CollectionAssert.AreEqual(live.Viewer.Left.Skip(leftBefore).ToArray(), saved.Viewer.Left.Skip(leftBefore).ToArray(), "the adopted flight leaves the same tiles");
        Assert.AreEqual(live.Session.QueryProjectile(a.Entity)?.Cell, saved.Session.QueryProjectile(b.Entity)?.Cell);
    }
}
