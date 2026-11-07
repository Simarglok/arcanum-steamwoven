using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-DEV-145 (MIG13 S10a): the hero's request boundary over a recording port: the tile click, the right release's tail (RR5) and the turn.</summary>
[TestClass]
public sealed class HeroRequestsTests
{
    private sealed class Hero : IHeroActions
    {
        public HeroFacts State = new(new CellCoord(10, 10), 5, true, 0, false, false);
        public HeroInputOptions Input;
        public bool Retargets;
        public Func<HeroFacts, HeroFacts>? AfterInterrupt;
        public readonly List<string> Calls = [];
        public HeroFacts Facts() => State;
        public HeroInputOptions Options() => Input;
        public bool SetWalkTarget(CellCoord tile, bool run, bool held) { Calls.Add($"walk {tile.X},{tile.Y} run={run}{(held ? " held" : "")}"); return !Retargets; }
        public void InterruptOrdinary(string why) { Calls.Add("interrupt " + why); if (AfterInterrupt is not null) State = AfterInterrupt(State); }
        public void StartTurn(int rotation) => Calls.Add("turn " + rotation);
        public bool Walking;
        public bool EndHeldWalk() { Calls.Add("end held walk"); return Walking; }
    }

    [TestMethod]
    public void AWalkRequestRunsByTheAlwaysRunRule()
    {
        var hero = new Hero { Input = new(AlwaysRunOption: true, NumLockRun: false) };
        var host = new HeroRequestHost(hero);
        Assert.IsTrue(host.Apply(new WalkRequest(new(12, 10), Ctrl: false)).Run);
        Assert.IsFalse(host.Apply(new WalkRequest(new(12, 10), Ctrl: true)).Run);
        hero.Input = new(false, false);
        Assert.IsFalse(host.Apply(new WalkRequest(new(12, 10), false)).Run);
        Assert.IsTrue(host.Apply(new WalkRequest(new(12, 10), true)).Run);
        hero.Input = new(false, true);
        Assert.IsTrue(host.Apply(new WalkRequest(new(12, 10), false)).Run);
    }

    [TestMethod]
    public void AWalkRequestToTheSameGoalIsUnchanged()
    {
        var hero = new Hero { Retargets = true };
        var result = new HeroRequestHost(hero).Apply(new WalkRequest(new(12, 10), false));
        Assert.AreEqual(HeroRequestOutcome.Unchanged, result.Outcome);
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(new Hero()).Apply(new WalkRequest(new(12, 10), false)).Outcome);
    }

    [TestMethod]
    public void AStopInterruptsAWalkingHeroAndTurnsItTowardThePointer()
    {
        var hero = new Hero { State = new(new CellCoord(10, 10), 5, true, 2, false, false) };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(3, result.Rotation);
        CollectionAssert.AreEqual(new[] { "interrupt right release", "turn 3" }, hero.Calls);
    }

    [TestMethod]
    public void TheTurnIsDecidedAfterAnInterruptedTurnSnappedTheFacing()
    {
        // A turn toward rotation 3 is under way (priority 2) and shows 4; the interrupt snaps the art to 3, so a release toward 3 starts nothing.
        var hero = new Hero { State = new(new CellCoord(10, 10), 4, true, 2, false, false), AfterInterrupt = f => f with { Facing = 3, HighestRunPriority = 0 } };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(RotateStartVerdict.AlreadyFacing, result.Verdict);
        CollectionAssert.AreEqual(new[] { "interrupt right release" }, hero.Calls);
        // Toward another rotation the turn starts from the snapped facing's side.
        hero.Calls.Clear(); hero.State = new(new CellCoord(10, 10), 4, true, 2, false, false);
        new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 6)));
        CollectionAssert.AreEqual(new[] { "interrupt right release", "turn 7" }, hero.Calls);
    }

    [TestMethod]
    public void AStopOfAnIdleHeroOnlyTurns()
    {
        var hero = new Hero();
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 6)));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        CollectionAssert.AreEqual(new[] { "turn 7" }, hero.Calls);
    }

    [TestMethod]
    public void ACastingHeroRefusesTheStopAndKeepsItsFacing()
    {
        var hero = new Hero { State = new(new CellCoord(10, 10), 5, true, 4, false, false) };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Refused, result.Outcome);
        Assert.AreEqual(0, hero.Calls.Count);
    }

    [TestMethod]
    public void AnInactiveHeroTakesNoStopAtAll()
    {
        var hero = new Hero { State = new(new CellCoord(10, 10), 5, false, 2, false, false) };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Ignored, result.Outcome);
        Assert.AreEqual(0, hero.Calls.Count);
    }

    [TestMethod]
    public void AStopWithNoPointerTileInterruptsButDoesNotTurn()
    {
        var hero = new Hero { State = new(new CellCoord(10, 10), 5, true, 3, false, false) };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(null));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.IsNull(result.Rotation);
        CollectionAssert.AreEqual(new[] { "interrupt right release" }, hero.Calls);
    }

    [TestMethod]
    public void AnIdleHeroAlreadyFacingThePointerChangesNothing()
    {
        var hero = new Hero();
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(14, 10)));
        Assert.AreEqual(HeroRequestOutcome.Unchanged, result.Outcome);
        Assert.AreEqual(RotateStartVerdict.AlreadyFacing, result.Verdict);
        Assert.AreEqual(0, hero.Calls.Count);
    }

    [TestMethod]
    public void AFaceRequestTurnsAnIdleHeroOnlyWhenNoMainRunGoesOn()
    {
        var hero = new Hero();
        var host = new HeroRequestHost(hero);
        Assert.AreEqual(HeroRequestOutcome.Done, host.Apply(new FaceRequest(new CellCoord(10, 14))).Outcome);
        hero.State = hero.State with { HighestRunPriority = 2 };
        var busy = host.Apply(new FaceRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Ignored, busy.Outcome);
        Assert.AreEqual(RotateStartVerdict.MainRunBusy, busy.Verdict);
        CollectionAssert.AreEqual(new[] { "turn 3" }, hero.Calls);
    }

    [TestMethod]
    public void ARoundGivesTheTurnToItsCurrentParticipantOnly()
    {
        var hero = new Hero { State = new(new CellCoord(10, 10), 5, true, 0, InRound: true, IsCurrentParticipant: false) };
        var result = new HeroRequestHost(hero).Apply(new FaceRequest(new CellCoord(10, 14)));
        Assert.AreEqual(RotateStartVerdict.NotParticipant, result.Verdict);
        Assert.AreEqual(0, hero.Calls.Count);
    }

    [TestMethod]
    public void AHeroWithNoCellIsInterruptedButNotTurned()
    {
        var hero = new Hero { State = new(null, 5, true, 2, false, false) };
        var result = new HeroRequestHost(hero).Apply(new StopRequest(new CellCoord(10, 14)));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.IsNull(result.Rotation);
        CollectionAssert.AreEqual(new[] { "interrupt right release" }, hero.Calls);
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(hero).Apply(new FaceRequest(new CellCoord(10, 14))).Outcome);
    }

    [TestMethod]
    public void NullPortAndNullRequestAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new HeroRequestHost(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HeroRequestHost(new Hero()).Apply(null!));
    }

    // ---- ARC-DEV-147 (MIG13 S10c): the held walk ----

    [TestMethod]
    public void AHeldWalkRequestPassesTheHeldFlagAndRunsByTheAlwaysRunRuleWithoutCtrl()
    {
        var hero = new Hero { Input = new(AlwaysRunOption: false, NumLockRun: true) };
        var result = new HeroRequestHost(hero).Apply(new WalkRequest(new(18, 10), Ctrl: false, Held: true));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.IsTrue(result.Run, "the stick runs while the Num Lock toggle is on");
        CollectionAssert.AreEqual(new[] { "walk 18,10 run=True held" }, hero.Calls);
        hero.Calls.Clear();
        new HeroRequestHost(hero).Apply(new WalkRequest(new(18, 10), false));
        CollectionAssert.AreEqual(new[] { "walk 18,10 run=True" }, hero.Calls, "a plain click is not held");
    }

    [TestMethod]
    public void AReleaseEndsTheHeldWalkAndSaysWhatItDid()
    {
        var walking = new Hero { Walking = true };
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(walking).Apply(new ReleaseWalkRequest()).Outcome);
        CollectionAssert.AreEqual(new[] { "end held walk" }, walking.Calls);
        Assert.AreEqual(HeroRequestOutcome.Unchanged, new HeroRequestHost(new Hero()).Apply(new ReleaseWalkRequest()).Outcome, "no walk to end");
        var casting = new Hero { State = new(new CellCoord(10, 10), 5, true, 4, false, false) };
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(casting).Apply(new ReleaseWalkRequest()).Outcome, "a run of priority 4 refused the interrupt");
    }
}
