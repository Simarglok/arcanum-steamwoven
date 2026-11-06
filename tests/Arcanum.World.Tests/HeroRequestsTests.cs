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
        public readonly List<string> Calls = [];
        public HeroFacts Facts() => State;
        public HeroInputOptions Options() => Input;
        public bool SetWalkTarget(CellCoord tile, bool run) { Calls.Add($"walk {tile.X},{tile.Y} run={run}"); return !Retargets; }
        public void InterruptOrdinary(string why) => Calls.Add("interrupt " + why);
        public void StartTurn(int rotation) => Calls.Add("turn " + rotation);
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
    public void NullPortAndNullRequestAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new HeroRequestHost(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new HeroRequestHost(new Hero()).Apply(null!));
    }
}
