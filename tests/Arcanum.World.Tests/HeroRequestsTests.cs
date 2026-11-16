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
        public HeroClickFacts Click = new(true, false, false, false, false, HeroClickTarget.Critter);
        public bool TalkStarts = true;
        public HeroClickFacts ClickFacts(string target, bool alt, bool shift) => Click with { Alt = alt, Shift = shift };
        public bool StartTalk(string target, bool run, bool noWalk) { Calls.Add($"talk {target} run={run}{(noWalk ? " nowalk" : "")}"); return TalkStarts; }
        public bool StartUse(string target, bool container, bool run) { Calls.Add($"use {target} container={container} run={run}"); return TalkStarts; }
        public bool StartPickup(string target, bool run) { Calls.Add($"pickup {target} run={run}"); return TalkStarts; }
        public bool StartAttack(string target, bool run) { Calls.Add($"attack {target} run={run}"); return TalkStarts; }
        public bool StartAttemptAttack(string target) { Calls.Add($"attempt {target}"); return TalkStarts; }
        public bool StartLockpick(string portal, string item) { Calls.Add($"lockpick {portal} {item}"); return TalkStarts; }
        public bool StartPickPocket(string victim, string item, bool plant) { Calls.Add($"pickpocket {victim} {item} plant={plant}"); return TalkStarts; }
        public bool StartCast(int run, string? target) { Calls.Add($"cast {run} {target}"); return TalkStarts; }
        public bool StartThrow(string item, CellCoord tile) { Calls.Add($"throw {item} {tile.X},{tile.Y}"); return TalkStarts; }
        public bool StartUseItem(string item, string target, bool run, bool noWalk) { Calls.Add($"use-item {item} {target} run={run} nowalk={noWalk}"); return TalkStarts; }
        public bool StartUseItemOnTile(string item, CellCoord tile) { Calls.Add($"use-item-tile {item} {tile.X},{tile.Y}"); return TalkStarts; }
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
    public void AWalkRequestWithShiftHeldDoesNothing()
    {
        // ARC-INPUT-1936 (GT107, SA47): the tile press with a Shift key held returns before the walk starter and the held flag.
        var hero = new Hero();
        var result = new HeroRequestHost(hero).Apply(new WalkRequest(new(12, 10), Ctrl: false, Shift: true));
        Assert.AreEqual(HeroRequestOutcome.Ignored, result.Outcome);
        Assert.AreEqual(0, hero.Calls.Count, "no walk target, no interrupt");
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(hero).Apply(new WalkRequest(new(12, 10), Ctrl: true, Held: true, Shift: true)).Outcome, "a repeat is an ordinary press");
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(hero).Apply(new WalkRequest(new(12, 10), Ctrl: false)).Outcome, "without Shift the walk starts");
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

    // ARC-DEV-169 (MIG13 S11a): the object click's talk.

    [TestMethod]
    public void AnObjectClickOnAnNpcStartsTheTalkRunning()
    {
        var hero = new Hero { Input = new(AlwaysRunOption: false, NumLockRun: false) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1"));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.Talk, result.Click);
        Assert.IsTrue(result.Run, "GS27: after the decision running is turned on, whatever the always-run option says");
        CollectionAssert.AreEqual(new[] { "talk npc:1 run=True" }, hero.Calls);
    }

    [TestMethod]
    public void AShiftClickOnAnNpcStartsTheTalkWithTheNoWalkFlagAndNoRunning()
    {
        // ARC-AI-1137 (GT122): the Shift branch of the start replaces the running call: run flag 0x100 and no running.
        var hero = new Hero { Input = new(AlwaysRunOption: true, NumLockRun: false) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1", Shift: true));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.Talk, result.Click);
        Assert.IsFalse(result.Run);
        CollectionAssert.AreEqual(new[] { "talk npc:1 run=False nowalk" }, hero.Calls);
    }

    [TestMethod]
    public void AnObjectClickTheRulesRefuseStartsNothing()
    {
        var hero = new Hero { Click = new(false, false, false, false, false, HeroClickTarget.Critter) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1"));
        Assert.AreEqual(HeroRequestOutcome.Ignored, result.Outcome);
        Assert.AreEqual(HeroClickGoal.None, result.Click);
        Assert.AreEqual(0, hero.Calls.Count);
    }

    // ARC-DEV-270: a Shift click on a creature in combat mode is the attempt attack 19 (GS26) with run flag 0x100 in place of running (GS27).
    [TestMethod]
    public void AShiftClickOnACreatureInCombatModeStartsTheAttemptAttackWithoutRunning()
    {
        var hero = new Hero { Click = new(true, false, false, false, true, HeroClickTarget.Critter, Shift: true) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1", Shift: true));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.AttemptAttack, result.Click);
        Assert.IsFalse(result.Run);
        CollectionAssert.AreEqual(new[] { "attempt npc:1" }, hero.Calls);
    }

    [TestMethod]
    public void AShiftClickTheGateRefusesIsARefusal()
    {
        var hero = new Hero { Click = new(true, false, false, false, true, HeroClickTarget.Critter, Shift: true), TalkStarts = false };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1", Shift: true));
        Assert.AreEqual(HeroRequestOutcome.Refused, result.Outcome);
        Assert.AreEqual(HeroClickGoal.AttemptAttack, result.Click);
    }

    // ARC-DEV-181 (MIG13 S12a): the object click's attack.
    [TestMethod]
    public void AClickOnACreatureInCombatModeStartsTheAttackGoal()
    {
        var hero = new Hero { Click = new(true, false, false, false, true, HeroClickTarget.Critter) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1"));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.Attack, result.Click);
        CollectionAssert.AreEqual(new[] { "attack npc:1 run=True" }, hero.Calls);
    }

    [TestMethod]
    public void AnAltClickOnAnObjectInCombatModeStartsTheAttackGoal()
    {
        var hero = new Hero { Click = new(true, false, false, false, true, HeroClickTarget.Scenery, Alt: true) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:1", Alt: true));
        Assert.AreEqual(HeroClickGoal.Attack, result.Click);
        CollectionAssert.AreEqual(new[] { "attack obj:1 run=True" }, hero.Calls);
    }

    // ARC-DEV-182 (MIG13 S12b): the press with an item on the cursor.
    [TestMethod]
    public void AThrowRequestStartsTheThrowGoalAtTheTile()
    {
        var hero = new Hero();
        var result = new HeroRequestHost(hero).Apply(new ThrowRequest("kit:7", new(14, 10)));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        CollectionAssert.AreEqual(new[] { "throw kit:7 14,10" }, hero.Calls);
    }

    [TestMethod]
    public void AThrowTheGateRefusesIsARefusalAndAnInactiveHeroIgnoresIt()
    {
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(new Hero { TalkStarts = false }).Apply(new ThrowRequest("kit:7", new(14, 10))).Outcome);
        var down = new Hero { State = new(new CellCoord(10, 10), 0, false, 0, false, false) };
        var result = new HeroRequestHost(down).Apply(new ThrowRequest("kit:7", new(14, 10)));
        Assert.AreEqual(HeroRequestOutcome.Ignored, result.Outcome);
        Assert.AreEqual(0, down.Calls.Count);
    }

    // ARC-DEV-183 (MIG13 S12c): the cast request.
    [TestMethod]
    public void ACastRequestStartsTheCastingGoalAndAHeroThatCannotActIgnoresIt()
    {
        var hero = new Hero();
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(hero).Apply(new CastRequest(7, "npc:1")).Outcome);
        CollectionAssert.AreEqual(new[] { "cast 7 npc:1" }, hero.Calls);
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(new Hero { TalkStarts = false }).Apply(new CastRequest(7, null)).Outcome);
        var down = new Hero { State = new(new CellCoord(10, 10), 0, false, 0, false, false) };
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(down).Apply(new CastRequest(7, null)).Outcome);
        Assert.AreEqual(0, down.Calls.Count);
    }

    // ARC-DEV-184 (MIG13 S12d): the pick pocket request.
    [TestMethod]
    public void APickPocketRequestStartsTheSkillGoalAndAHeroThatCannotActIgnoresIt()
    {
        var hero = new Hero();
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(hero).Apply(new PickPocketRequest("npc:1", "item:1", Plant: true)).Outcome);
        CollectionAssert.AreEqual(new[] { "pickpocket npc:1 item:1 plant=True" }, hero.Calls);
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(new Hero { TalkStarts = false }).Apply(new PickPocketRequest("npc:1", "item:1", false)).Outcome);
        var down = new Hero { State = new(new CellCoord(10, 10), 0, false, 0, false, false) };
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(down).Apply(new PickPocketRequest("npc:1", "item:1", false)).Outcome);
    }

    // ARC-DEV-185 (MIG13 S12e): the lockpick request.
    [TestMethod]
    public void ALockpickRequestStartsTheSkillGoalAndAHeroThatCannotActIgnoresIt()
    {
        var hero = new Hero();
        Assert.AreEqual(HeroRequestOutcome.Done, new HeroRequestHost(hero).Apply(new LockpickRequest("door:1", "item:picks")).Outcome);
        CollectionAssert.AreEqual(new[] { "lockpick door:1 item:picks" }, hero.Calls);
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(new Hero { TalkStarts = false }).Apply(new LockpickRequest("door:1", "item:picks")).Outcome);
        var down = new Hero { State = new(new CellCoord(10, 10), 0, false, 0, false, false) };
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(down).Apply(new LockpickRequest("door:1", "item:picks")).Outcome);
    }

    [TestMethod]
    public void AnAttackTheGateRefusesIsARefusal()
    {
        var hero = new Hero { TalkStarts = false, Click = new(true, false, false, false, true, HeroClickTarget.Critter) };
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1")).Outcome);
    }

    [TestMethod]
    public void AGateThatRefusesTheTalkIsARefusal()
    {
        var hero = new Hero { TalkStarts = false };
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(hero).Apply(new ObjectClickRequest("npc:1")).Outcome);
    }

    // ARC-DEV-174 (MIG13 S11b): the object click's use.

    [TestMethod]
    public void AnObjectClickOnSceneryStartsTheUseGoalRunning()
    {
        var hero = new Hero { Click = new(true, false, false, false, false, HeroClickTarget.Scenery) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:1"));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.UseObject, result.Click);
        CollectionAssert.AreEqual(new[] { "use obj:1 container=False run=True" }, hero.Calls);
    }

    [TestMethod]
    public void AnObjectClickOnAContainerStartsGoalEightyOne()
    {
        var hero = new Hero { Click = new(true, false, false, false, false, HeroClickTarget.Container) };
        Assert.AreEqual(HeroClickGoal.UseContainer, new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:2")).Click);
        CollectionAssert.AreEqual(new[] { "use obj:2 container=True run=True" }, hero.Calls);
    }

    // ARC-DEV-175 (MIG13 S11c): the object click's pickup.

    [TestMethod]
    public void AnObjectClickOnAnItemStartsGoalFifteenRunning()
    {
        var hero = new Hero { Click = new(true, false, false, false, false, HeroClickTarget.Item) };
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:3"));
        Assert.AreEqual(HeroRequestOutcome.Done, result.Outcome);
        Assert.AreEqual(HeroClickGoal.Pickup, result.Click);
        CollectionAssert.AreEqual(new[] { "pickup obj:3 run=True" }, hero.Calls);
    }

    [TestMethod]
    public void AnObjectClickOnADeadCreatureStartsGoalEightyOneAndOnACorpseInCombatModeNeedsAlt()
    {
        var hero = new Hero { Click = new(true, false, false, false, false, HeroClickTarget.Critter, TargetDead: true) };
        Assert.AreEqual(HeroClickGoal.UseContainer, new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:4")).Click);
        CollectionAssert.AreEqual(new[] { "use obj:4 container=True run=True" }, hero.Calls);
        hero.Calls.Clear();
        hero.Click = new(true, false, false, false, true, HeroClickTarget.Critter, TargetDead: true);
        var result = new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:4"));
        Assert.AreEqual(HeroRequestOutcome.Ignored, result.Outcome, "a corpse in combat mode without Alt does nothing");
        Assert.AreEqual(0, hero.Calls.Count);
        Assert.AreEqual(HeroClickGoal.UseContainer, new HeroRequestHost(hero).Apply(new ObjectClickRequest("obj:4", Alt: true)).Click, "with Alt it is looted");
    }
    // ---- ARC-DEV-365 (T-g): the item use goals 39 and 41 ----

    [TestMethod]
    public void AnItemUseRequestStartsGoal39RunningByTheAlwaysRunRuleAndShiftSetsTheNoWalkFlag()
    {
        var hero = new Hero { Input = new(AlwaysRunOption: false, NumLockRun: false) };
        var host = new HeroRequestHost(hero);
        var result = host.Apply(new UseItemRequest("kit:3", "npc:2", Ctrl: true, Shift: true));
        Assert.AreEqual((HeroRequestOutcome.Done, true), (result.Outcome, result.Run));
        CollectionAssert.AreEqual(new[] { "use-item kit:3 npc:2 run=True nowalk=True" }, hero.Calls);
        hero.Calls.Clear();
        Assert.IsFalse(host.Apply(new UseItemRequest("kit:3", "npc:2", Ctrl: false, Shift: false)).Run);
        CollectionAssert.AreEqual(new[] { "use-item kit:3 npc:2 run=False nowalk=False" }, hero.Calls);
    }

    [TestMethod]
    public void AnItemUseRequestIsIgnoredForAHeroThatCannotActAndRefusedWhenTheStartIs()
    {
        var down = new Hero { State = new(new CellCoord(10, 10), 5, false, 0, false, false) };
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(down).Apply(new UseItemRequest("kit:3", "npc:2", false, false)).Outcome);
        Assert.AreEqual(HeroRequestOutcome.Ignored, new HeroRequestHost(down).Apply(new UseItemOnTileRequest("kit:3", new(3, 4))).Outcome);
        Assert.AreEqual(0, down.Calls.Count);
        var refused = new Hero { TalkStarts = false };
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(refused).Apply(new UseItemRequest("kit:3", "npc:2", false, false)).Outcome);
        Assert.AreEqual(HeroRequestOutcome.Refused, new HeroRequestHost(refused).Apply(new UseItemOnTileRequest("kit:3", new(3, 4))).Outcome);
    }

    [TestMethod]
    public void AnItemUseOnATileRequestStartsGoal41WithNoRunFlag()
    {
        var hero = new Hero();
        var result = new HeroRequestHost(hero).Apply(new UseItemOnTileRequest("kit:3", new(3, 4)));
        Assert.AreEqual((HeroRequestOutcome.Done, false), (result.Outcome, result.Run));
        CollectionAssert.AreEqual(new[] { "use-item-tile kit:3 3,4" }, hero.Calls);
    }
}
