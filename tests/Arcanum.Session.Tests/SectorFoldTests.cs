using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-104 (MIG9 S4, K1 §3.4, RL8, RL17, ledger CX7, CX8): the session's sector cache and the fold over an invented exterior: a guard and a
// dead-or-alive watchman in sector 0,0, a smith in sector 1,0, a destroyed ghost and a switched-off sleeper in sector 0,0, a chest in sector 0,0.
// The hero stands at 100,200 (sector 1,3). Cells and ids are invented; no original data.
[TestClass]
public sealed class SectorFoldTests
{
    private const long T0 = 54_000_000; // 15:00 on day 0, the fixture's start hour.
    private const string Guard = "oid:guard", Watch = "oid:watch", Smith = "oid:smith", Ghost = "oid:ghost", Sleeper = "oid:sleeper", Chest = "oid:chest";
    private static readonly SectorMapExtent Extent = new(16, 16);
    private static readonly CellCoord InZero = new(10, 20), InOne = new(70, 20);

    private static SessionResidentRow[] Rows() =>
    [
        new(Guard, SessionResidentKind.Npc, new(10, 20)), new(Watch, SessionResidentKind.Npc, new(12, 20)), new(Smith, SessionResidentKind.Npc, new(70, 20)),
        new(Ghost, SessionResidentKind.Npc, new(14, 20)) { Destroyed = true }, new(Sleeper, SessionResidentKind.Npc, new(16, 20)) { Off = true },
        new(Chest, SessionResidentKind.Container, new(18, 20)),
    ];

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven, SectorCacheLimits? limits = null)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules }).Owner!;
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent, limits));
        return owner;
    }

    private static SectorTouchResult Touch(GameSessionOwner owner, params CellCoord[] cells)
    {
        var result = owner.TouchSectors(owner.Version, Exterior, cells);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    // Loads eight other sectors (a cache of eight slots), so sector 0,0, the least recent, is evicted.
    private static void EvictZero(GameSessionOwner owner)
    {
        var fill = Touch(owner, Enumerable.Range(2, 8).Select(x => new CellCoord(x * 64, 512)).ToArray());
        Assert.IsTrue(fill.Folds.Any(f => f.Evicted == new ResidentSector(0, 0)));
        Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(0, 0))!.Value);
    }

    [TestMethod]
    public void TheCacheOpensOnlyOverAnOpenResidencyAndCloses()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(SectorsOpenStatus.NoResidency, owner.OpenSectors(Exterior, Extent));
        Assert.IsNull(owner.QuerySectorLoaded(Exterior, new(0, 0)), "no cache: the host keeps its own answer");
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.TouchSectors(owner.Version, Exterior, [InZero]).Failure);
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(SectorsOpenStatus.InvalidExtent, owner.OpenSectors(Exterior, new(0, 4)));
        Assert.AreEqual(SectorsOpenStatus.InvalidExtent, owner.OpenSectors(Exterior, new(1, 1)), "the smith's sector 1,0 lies outside");
        Assert.AreEqual(SectorsOpenStatus.NoResidency, owner.OpenSectors(Cave, Extent), "only the open map");
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent));
        Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(0, 0))!.Value);
        Assert.IsTrue(owner.QuerySectors(Exterior)!.MapOpen);

        // A residency opened again drops the cache; so does a close and the hero's map change.
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.IsNull(owner.QuerySectors(Exterior));
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent));
        Assert.IsTrue(owner.CloseResidency());
        Assert.IsNull(owner.QuerySectors(Exterior));
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent));
        Touch(owner, InZero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.IsNull(owner.QuerySectors(Exterior));
        Assert.IsNull(owner.QuerySectorLoaded(Exterior, new(0, 0)));
    }

    [TestMethod]
    public void AFirstLoadFoldsEveryNpcThatIsNotDestroyedWithTheMarkerAndAHitFoldsNothing()
    {
        using var owner = Owner();
        var before = owner.Version;
        var touch = Touch(owner, InZero, new(11, 21));
        var fold = touch.Folds.Single();
        Assert.AreEqual(new ResidentSector(0, 0), fold.Sector);
        Assert.IsTrue(fold.Marked, "no eviction record: the fold flag");
        Assert.IsNull(fold.Evicted);
        CollectionAssert.AreEqual(new[] { Guard, Sleeper, Watch }, fold.Npcs.ToArray(), "entity order; off NPCs too, the destroyed ghost and the chest not");
        Assert.AreEqual(0, fold.Refused.Count);
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the fold's ticks");
        foreach (var npc in fold.Npcs) Assert.AreEqual((T0 + 1, true), owner.QueryAiTick(npc));
        Assert.IsNull(owner.QueryAiTick(Smith), "sector 1,0 was not touched");
        Assert.IsNull(owner.QueryAiTick(Ghost));
        Assert.IsTrue(owner.QuerySectorLoaded(Exterior, new(0, 0))!.Value);
        Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(1, 0))!.Value);

        // A loaded sector is only refreshed: no fold, no tick, no revision.
        var again = Touch(owner, InZero);
        Assert.AreEqual(0, again.Folds.Count);
        CollectionAssert.AreEqual(new[] { new ResidentSector(0, 0) }, again.Hits.ToArray());
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void AFoldReplacesThePendingTickAndAReturnWithinThreeSecondsCarriesNoMarker()
    {
        using var owner = Owner(limits: SectorCacheLimits.TryCreate(8, out var eight) == SectorCacheLimitsFailure.None ? eight : null);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = Guard, Start = SessionAiTickStart.Delayed, DistanceToPc = 30 }).Disposition);
        Assert.AreEqual((T0 + 5000, false), owner.QueryAiTick(Guard));
        Touch(owner, InZero);
        Assert.AreEqual((T0 + 1, true), owner.QueryAiTick(Guard), "the fold's tick replaces the pending one");

        // Evicted at T0 and back 2,999 ms later: an immediate tick without the marker; 3,000 ms later: the marker.
        EvictZero(owner);
        Advance(owner, 2999);
        var quick = Touch(owner, InZero).Folds.Single();
        Assert.IsFalse(quick.Marked);
        Assert.AreEqual((T0 + 2999 + 1, false), owner.QueryAiTick(Guard));
        EvictZero(owner);
        Advance(owner, 3000);
        Assert.IsTrue(Touch(owner, InZero).Folds.Single().Marked);
        Assert.AreEqual((T0 + 5999 + 1, true), owner.QueryAiTick(Watch));
    }

    [TestMethod]
    public void Cx7AReturnAcrossMidnightFoldsWithTheMarkerOnlyUnderTheOriginalSetting()
    {
        foreach (var (rules, marked) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            SectorCacheLimits.TryCreate(8, out var eight);
            using var owner = Owner(rules, eight);
            Touch(owner, InZero);
            Advance(owner, 86_399_000 - T0); // 23:59:59.000 on day 0.
            EvictZero(owner);
            Advance(owner, 1_500); // 00:00:00.500 on day 1.
            Assert.AreEqual(marked, Touch(owner, InZero).Folds.Single().Marked, rules.ToString());
            Assert.AreEqual(marked, owner.QueryAiTick(Guard)!.Value.WakeUp);
        }
        // The rules themselves: the original's plan and the fix.
        Assert.IsTrue(SectorCacheRules.FoldDue(86_399_000, 86_400_500));
        Assert.IsFalse(SectorCacheRules.FoldDueAfter(86_399_000, 86_400_500));
        Assert.IsTrue(SectorCacheRules.FoldDueAfter(86_399_000, 86_402_000));
        Assert.IsTrue(SectorCacheRules.FoldDue(1_000, 3 * 86_400_000L + 500), "days later both plans fold");
        Assert.IsTrue(SectorCacheRules.FoldDueAfter(1_000, 3 * 86_400_000L + 500));
        Assert.IsFalse(SectorCacheRules.FoldDue(1_000, 2_500));
        Assert.IsFalse(SectorCacheRules.FoldDueAfter(1_000, 2_500));
    }

    [TestMethod]
    public void AnImmediateTickCarriesNoMarkerAndRunsNoWakeUpBlock()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = Smith, Start = SessionAiTickStart.Immediate, DistanceToPc = 30 }).Disposition);
        Assert.AreEqual((T0 + 1, false), owner.QueryAiTick(Smith));
        Touch(owner, InZero);
        Advance(owner, 1);
        var world = new AiSchedulerTests.World().With(Guard).With(Watch).With(Sleeper).With(Smith);
        var pass = owner.ProcessEvents(world);
        var smith = pass.AiTicks.Single(t => t.Entity == Smith);
        Assert.IsFalse(smith.WakeUp);
        Assert.IsFalse(smith.Tick!.Steps.HasFlag(AiTickSteps.WakeUp));
        var guard = pass.AiTicks.Single(t => t.Entity == Guard);
        Assert.IsTrue(guard.WakeUp);
        Assert.IsTrue(guard.Tick!.Steps.HasFlag(AiTickSteps.WakeUp | AiTickSteps.Waypoints | AiTickSteps.FirstHeartbeatContinued));
        CollectionAssert.Contains(world.Calls, "script 10 oid:guard oid:guard");
        CollectionAssert.DoesNotContain(world.Calls, "script 10 oid:smith oid:smith");
    }

    [TestMethod]
    public void AFoldInsideAnEventPassSchedulesItsTicksWhenThePassEnds()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = Smith, Start = SessionAiTickStart.Immediate }).Disposition);
        Advance(owner, 1);
        var world = new AiSchedulerTests.World().With(Smith);
        SectorTouchResult? inner = null;
        world.OnScript = (point, _) =>
        {
            if (point == AiScriptPoint.Heartbeat && inner is null) inner = owner.TouchSectors(owner.Version, Exterior, [InZero]);
        };
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, inner!.Failure);
        Assert.IsTrue(inner.Deferred);
        CollectionAssert.AreEqual(new[] { Guard, Sleeper, Watch }, inner.Folds.Single().Npcs.ToArray());
        Assert.AreEqual((3, 0), (pass.DeferredFoldTicks, pass.DeferredFoldTicksRefused));
        Assert.AreEqual((T0 + 2, true), owner.QueryAiTick(Guard), "scheduled at the pass's end, 1 ms ahead");
        Assert.AreEqual(owner.Version, pass.Version);
    }

    [TestMethod]
    public void ATouchRefusesNegativeCellsTooManySectorsAndAStaleVersionAndIgnoresCellsOutsideTheMap()
    {
        SectorCacheLimits.TryCreate(8, out var eight);
        using var owner = Owner(limits: eight);
        Assert.AreEqual(GameSessionFailure.InvalidSectors, owner.TouchSectors(owner.Version, Exterior, [new(-1, 0)]).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSectors,
            owner.TouchSectors(owner.Version, Exterior, Enumerable.Range(0, 9).Select(x => new CellCoord(x * 64, 0)).ToArray()).Failure,
            "nine sectors in one batch on eight slots");
        Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(0, 0))!.Value, "a refused batch changes nothing");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.TouchSectors(new GameSessionVersion(owner.Identity, owner.Version.Revision + 1), Exterior, [InZero]).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.TouchSectors(owner.Version, Cave, [InZero]).Failure);
        var outside = Touch(owner, new CellCoord(16 * 64, 0), new CellCoord(0, 16 * 64));
        Assert.AreEqual((0, 0), (outside.Folds.Count, outside.Hits.Count));
        Assert.IsNull(owner.QueryAiTick(Guard));
    }
}
