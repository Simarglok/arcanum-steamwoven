using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-086 (AI slice I7): perform's dispatch (0x4AC180), the starters (0x434B00, 0x434980, 0x4346A0, 0x436960) with the start
/// gate (0x4348E0), and the goal machine (0x423560) carrying out attacks, approaches (EX9, EX20), flights (EX12), the back-off run and
/// following, with the round's action points (EX6), over an invented open grid whose planner walks straight lines.
/// </summary>
[TestClass]
public sealed class ExecutorRulesTests
{
    private const string Npc = "oid:bandit", Foe = "pc", Leader = "oid:sogg";
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;
    private static readonly FixedRolls Rolls = new(150);

    /// <summary>Every draw answers <paramref name="value"/>, clamped to its range, and is counted.</summary>
    private sealed class FixedRolls(int value) : IAiRollSource
    {
        public int Draws;
        public int Roll(AiRollPurpose purpose, int min, int max)
        {
            Assert.IsTrue(purpose is AiRollPurpose.BlockedStepWait or AiRollPurpose.PleaseMoveStart);
            Draws++;
            return Math.Clamp(value, min, max);
        }
    }

    /// <summary>An open grid: objects on tiles, straight-line paths, a clear line within <see cref="Range"/> unless closed.</summary>
    internal sealed class World : IAiPerformWorld
    {
        public readonly Dictionary<string, CellCoord> At = [];
        public readonly Dictionary<string, AiMoverFacts> Movers = [];
        public readonly List<string> Calls = [];
        public readonly HashSet<CellCoord> Blocked = [];
        public readonly HashSet<string> GoneSet = [], DeadSet = [], Waits = [];
        public readonly Dictionary<string, int> Points = [];
        public readonly Dictionary<string, string> Leaders = [];
        public int Range = 1, Cost = 3, WalkMs = 100, RunMs = 50;
        public bool ViolenceFilter { get; set; }
        public bool Weapon, LineClosed, NoPaths, StoppedShort, Round, Grenades, BackOffStart, Throws, InterruptFails, Boomerang;
        public string? Actor;
        public DangerState State = DangerState.CombatFocus;
        public DangerFlags Bits;
        public BackOffRun BackOffStep = new(BackOffRunStep.Runs, 5);
        public CellCoord? Away;
        public AiFleeing Fleeing = new(true, Foe);
        public AiAttackFacts Attack = new();

        public CellCoord? Tile(string id) => At.TryGetValue(id, out var t) ? t : null;
        public bool Gone(string id) => GoneSet.Contains(id);
        public bool DeadCritter(string id) => DeadSet.Contains(id);
        public AiMoverFacts Mover(string id) => Movers.TryGetValue(id, out var m) ? m : new(Pc: id == Foe, Fatigue: 20, MaxFatigue: 20);
        public bool FirstStepClear(string id, string toward, bool ignoreTarget)
        {
            CellCoord from = At[id], to = At[toward];
            if (from == to) return true;
            var next = new CellCoord(from.X + Math.Sign(to.X - from.X), from.Y + Math.Sign(to.Y - from.Y));
            return next == to ? ignoreTarget || !Blocked.Contains(to) : !Blocked.Contains(next);
        }
        public AiAttackFacts AttackFacts(string id, string target) => Attack;
        public AiReachFacts Reach(string id, string target) =>
            new(AiExecutorRules.Distance(At[id], At[target]), Weapon ? Range : 0, Weapon, !LineClosed && AiExecutorRules.Distance(At[id], At[target]) <= Math.Max(Range, 1));
        public int AttackCost(string id) => Cost;

        public AiPath? PlanPath(string id, AiPathRequest request)
        {
            Calls.Add($"plan {request.Kind}{(request.Straight ? " straight" : "")}");
            if (NoPaths) return null;
            var to = request.Tile ?? At[request.Target!];
            var steps = new List<CellCoord>();
            for (var cell = At[id]; cell != to && steps.Count < 40;)
            {
                cell = new(cell.X + Math.Sign(to.X - cell.X), cell.Y + Math.Sign(to.Y - cell.Y));
                if (request.Kind is AiPathKind.Attack && cell == to) break;
                if (request.Kind is AiPathKind.Near && AiExecutorRules.Distance(cell, to) < request.Range) break;
                steps.Add(cell);
            }
            return new([.. steps], StoppedShort);
        }

        // ARC-AI-1313: the light at a cell (full by default) for the dark wander.
        public readonly Dictionary<CellCoord, int> Light = [];
        public int CellLight(CellCoord cell) => Light.TryGetValue(cell, out var level) ? level : 255;

        public readonly HashSet<CellCoord> Stuck = [];
        // ARC-DEV-209 (GT49): doors on cells and the door ports the walkers' opener goals run over.
        public readonly Dictionary<CellCoord, string> DoorAt = [];
        public IAiDoorWorld? DoorPorts;
        public string? ClosedDoorOn(string id, CellCoord next) =>
            DoorAt.TryGetValue(next, out var door) && DoorPorts is { } ports && ports.DoorValid(door) && !ports.DoorOpen(door) && !ports.DoorBusy(door) ? door : null;
        public IAiDoorWorld? Doors => DoorPorts;
        // ARC-DEV-302: a held mover's step start refuses every step.
        public bool Held;
        public AiStepBlock StepBlocked(string id, CellCoord next, bool flight) =>
            Held ? AiStepBlock.Held : Blocked.Contains(next) ? AiStepBlock.Occupied : Stuck.Contains(next) ? AiStepBlock.CannotStart : AiStepBlock.Clear;

        public int BeginStep(string id, CellCoord next, bool running)
        {
            Calls.Add($"step {next.X},{next.Y}{(running ? " run" : "")}");
            At[id] = next;
            return running ? RunMs : WalkMs;
        }

        // ARC-WORLD-1469 (TT9): the cells whose trap object the step onto them meets (spotted or set off), which ends the walk there.
        public readonly HashSet<CellCoord> TrapAt = [];
        public bool StepMeetsTrap(string id, CellCoord next)
        {
            if (!TrapAt.Contains(next)) return false;
            Calls.Add($"trap {next.X},{next.Y}");
            return true;
        }

        public int GetUp(string id) { Calls.Add("get up"); Movers[id] = Mover(id) with { KnockedDown = false }; return 300; }
        // ARC-DEV-137: the get-up is the real goal 63 now; its start shows animation 8, which stands the fake critter up.
        public void ShowArt(string id, int animation, int frame)
        {
            if (animation == 0) Arts.Remove(id); else Arts[id] = (animation, frame);
            Writes.Add((animation, frame));
            if (animation == AiExecutorRules.GetUpAnimation) { Calls.Add("get up"); Movers[id] = Mover(id) with { KnockedDown = false }; }
        }

        // ARC-DEV-137 (S6): the critter-state goals' art, clips and flags.
        public readonly Dictionary<string, (int Animation, int Frame)> Arts = [];
        public readonly List<(int Animation, int Frame)> Writes = [];
        public readonly Dictionary<int, AiAnimationClip> Clips = [];
        public readonly HashSet<string> UnconsciousSet = [], StonedSet = [], ParalysedSet = [];
        public int Cleared, Finished;
        public AiAnimationClip? Clip(string id, int animation) => Clips.TryGetValue(animation, out var clip) ? clip : null;
        public int ShownAnimation(string id) => Arts.TryGetValue(id, out var art) ? art.Animation : 0;
        public int ShownFrame(string id) => Arts.TryGetValue(id, out var art) ? art.Frame : 0;
        public bool Unconscious(string id) => UnconsciousSet.Contains(id);
        public bool Stoned(string id) => StonedSet.Contains(id);
        public bool Paralysed(string id) => ParalysedSet.Contains(id);
        public void ClearStunned(string id) { Cleared++; Calls.Add("clear stun"); }
        public void DyingFinished(string id) { Finished++; Calls.Add("dying finished"); }
        public int Unconceal(string id) { Calls.Add("unconceal"); Movers[id] = Mover(id) with { Concealed = false }; return 200; }
        public void Face(string id, string toward) => Calls.Add($"face {toward}");
        public void PlaySound(string id, int sound) => Calls.Add($"sound {sound}");
        public bool AttackArtMissing;
        public AiAttackSwing BeginAttack(string id, string target) { Calls.Add($"swing {target}"); return AttackArtMissing ? AiAttackSwing.NoArt : new(400, 600); }
        public void Hit(string id, string target) => Calls.Add($"hit {target}");
        public void SetNoFlee(string id) => Calls.Add("no-flee");
        public AiCastGoalStart StartCast(string id, int run) => throw new AssertFailedException("no cast here");
        public bool FaceCast(string id, int run) => throw new AssertFailedException("no cast here");
        public AiCastSwing BeginCast(string id, int run, bool castAnimation) => throw new AssertFailedException("no cast here");
        public bool CastActionFrame(string id, int run) => throw new AssertFailedException("no cast here");
        public bool ReleaseCast(string id, int run) => throw new AssertFailedException("no cast here");
        public void EndCast(string id, int run, bool refused) => throw new AssertFailedException("no cast here");
        public bool FaceForAction(string id, string? target, CellCoord? tile) => true;
        public AiActionSwing BeginAction(string id, AiActionAnimation animation) => new(AiActionStart.Started, 300, 300);
        public bool AttemptSkill(string id, string target, int skill, string? item) => false;
        public void AttemptPickLocks(string id, string target, string? item) { }
        public void AttemptSkillWithItem(string id, string target, string? item, int skill, int modifier) { }
        public bool ThrowItem(string id, string item, CellCoord tile) => true;
        public void PickUp(string id, string item) { }
        public string? ItemHolder(string item) => null;
        public AiUsedItem UsedItem(string user, string item) => new();
        public bool RunUseScript(string user, string item, string? target) => true;
        public void OpenTargeting(string user, string item) { }
        public void UseTrapDevice(string user, string item, string? target) { }
        public void ReadWritten(string user, string item) { }
        public bool Lockable(string target) => false;
        public void StartSkillWithItem(string user, string item, string target, int skill) { }
        public void UseItemMagic(string user, string item, string? target) { }
        public void SpendItem(string item) { }
        public bool InRound => Round;
        public string? RoundActor => Actor;
        public int ActionPoints(string id) => Points.GetValueOrDefault(id);
        public int PendingCost { get; set; }

        public void Consume(string id, int cost)
        {
            Calls.Add($"ap -{cost}");
            Points[id] = Math.Max(Points.GetValueOrDefault(id) - cost, 0);
        }

        public void EndSubturn(string id) { Calls.Add("end subturn"); Actor = null; }
        public bool Active(string id) => true;
        public DangerState ReadDanger(string npc) => State;
        public DangerFlags DangerBits(string npc) => Bits;
        public BackOffRun BackOff(string npc, string? target) => BackOffStep;
        public CellCoord? AwayTile(string npc, string? target, int range) => Away;
        public void EndBackOff(string npc) => Calls.Add("end back-off");
        public AiCastStart Cast(string npc, AiPassChoice choice) { Calls.Add("cast"); return new(null); }
        public void LeaveCombatMode(string npc) => Calls.Add("leave combat mode");
        public string? Leader(string npc) => Leaders.TryGetValue(npc, out var l) ? l : null;
        public bool Waiting(string npc) => Waits.Contains(npc);
        public void LoyaltyCheck(string npc, bool followStarted) => Calls.Add(followStarted ? "loyalty started" : "loyalty");
        public void RunRoutine(string npc) => Calls.Add("routine");
        public bool StartBackOff(string npc, string? target) { Calls.Add("back-off start?"); return BackOffStart; }
        public AiGrenadeResult Grenade(string npc, string target)
        {
            Calls.Add("grenade?");
            return Grenades ? new(AiGrenadeStop.Throw, "oid:grenade") : new(AiGrenadeStop.NoCheck);
        }
        public string? ThrowableWeapon(string npc) => Throws ? "oid:dagger" : null;
        public void Unwield(string npc, string item) => Calls.Add($"unwield {item}");
        public bool HasSkillArt(string npc) => true;
        public bool NpcPickupsOn => true;
        public bool BoomerangInFlight(string npc) => Boomerang;
        public bool InterruptLowerAnimations(string id) => !InterruptFails;
        // ARC-AI-1315: the critters on the tile (the finder and the step apart read them) and the please-move start.
        public readonly List<AiTileCritter> Sharers = [];
        public IReadOnlyList<AiTileCritter> TileCritters(string id) => Sharers;
        public bool StartPleaseMove(string mover, string awayFrom) { Calls.Add($"please move {mover} from {awayFrom}"); return true; }
        public AiFleeing PerformFleeing(string npc) { Calls.Add("perform fleeing"); return Fleeing; }
        public void PerformSurrender(string npc) => Calls.Add("perform surrender");
    }

    private static World Arena(int distance)
    {
        var world = new World();
        world.At[Npc] = new(10, 10);
        world.At[Foe] = new(10 + distance, 10);
        return world;
    }

    private static GoalRun AttackRun(int? sound = null) => new(Npc, default, new GoalFrame(GoalType.Attack, Foe, sound: sound));

    /// <summary>Steps the run until it ends; returns the results in order.</summary>
    internal static List<AiGoalStepResult> Drive(GoalRun run, World world, CombatAnomalyProfile profile, int limit = 200)
    {
        var results = new List<AiGoalStepResult>();
        for (var current = run; current is not null && results.Count < limit;)
        {
            var step = AiExecutorRules.Step(current, world, Rolls, profile, results.Count > 0 ? results[^1].Reason : AiExecutorReason.None);
            results.Add(step);
            current = step.Run;
        }
        return results;
    }

    // ---- the pure tests ----

    [TestMethod]
    public void ReachNeedsRangeAtLeastOneAndAClearLine()
    {
        Assert.IsTrue(AiExecutorRules.InReach(new(1, 0, false, true)));
        Assert.IsFalse(AiExecutorRules.InReach(new(2, 0, false, true)));
        Assert.IsTrue(AiExecutorRules.InReach(new(8, 8, true, true)));
        Assert.IsFalse(AiExecutorRules.InReach(new(8, 8, true, false)));
        Assert.IsFalse(AiExecutorRules.InReach(new(-1, 8, true, true)));
    }

    [TestMethod]
    public void AttackPossibleFollowsTheTestsOrder()
    {
        Assert.IsTrue(AiExecutorRules.AttackPossible(new()));
        Assert.IsFalse(AiExecutorRules.AttackPossible(new(AttackerInactive: true)));
        Assert.IsFalse(AiExecutorRules.AttackPossible(new(BodyOfAir: true)));
        Assert.IsTrue(AiExecutorRules.AttackPossible(new(BodyOfAir: true, Elemental: true)));
        Assert.IsFalse(AiExecutorRules.AttackPossible(new(TargetGone: true)));
        Assert.IsFalse(AiExecutorRules.AttackPossible(new(TargetDead: true)));
        Assert.IsTrue(AiExecutorRules.AttackPossible(new(ForeignBlocker: true)));
        Assert.IsFalse(AiExecutorRules.AttackPossible(new(ForeignBlocker: true, NoPathToTarget: true)));
    }

    [TestMethod]
    public void EscapeDestinationIsRangePlusOneStepsFromTheSourceThroughTheNpc()
    {
        Assert.AreEqual(new CellCoord(20, 10), AiExecutorRules.EscapeDestination(new(10, 10), new(11, 10), 9));
        Assert.AreEqual(new CellCoord(0, 0), AiExecutorRules.EscapeDestination(new(10, 10), new(8, 9), 9));
        // The direction is one of eight: a shallow slope goes straight along the major axis.
        Assert.AreEqual(new CellCoord(10, 16), AiExecutorRules.EscapeDestination(new(10, 10), new(11, 13), 5));
    }

    [TestMethod]
    public void StartGateInARound()
    {
        var world = Arena(3);
        Assert.IsTrue(AiExecutorRules.StartGate(world, Npc, 9), "outside a round");
        world.Round = true;
        world.Actor = Foe;
        Assert.IsFalse(AiExecutorRules.StartGate(world, Npc, 0), "another critter's turn");
        world.Actor = Npc;
        world.Points[Npc] = 3;
        world.Movers[Npc] = new(Fatigue: 7, MaxFatigue: 20);
        Assert.IsTrue(AiExecutorRules.StartGate(world, Npc, 3));
        Assert.IsFalse(AiExecutorRules.StartGate(world, Npc, 4), "35 % fatigue is not above");
        world.Movers[Npc] = new(Fatigue: 8, MaxFatigue: 20);
        Assert.IsTrue(AiExecutorRules.StartGate(world, Npc, 4), "40 % fatigue");
    }

    [TestMethod]
    public void AFidgetFollowsADoneRunOfAnNpcOrTheHeroInCombatModeOrAutoAnimating()
    {
        var world = Arena(3);
        // Outside a round: combat mode or the auto-animate flag, no animation running, an art that allows it; the hero too (ARC-DEV-272, TH9).
        Assert.IsTrue(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: true, autoAnimates: false, animating: false, artAllows: true));
        Assert.IsTrue(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: false, autoAnimates: true, animating: false, artAllows: true));
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: false, autoAnimates: false, animating: false, artAllows: true), "neither");
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: true, autoAnimates: false, animating: true, artAllows: true), "an animation runs");
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: true, autoAnimates: false, animating: false, artAllows: false), "the art test");
        Assert.IsTrue(AiExecutorRules.FidgetAfterRun(world, Foe, combatMode: true, autoAnimates: true, animating: false, artAllows: true), "the hero (ARC-DEV-272, TH9)");
        // In a round combat mode no longer counts; the auto-animating NPC needs the current participant's action points and the gate.
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 4;
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: true, autoAnimates: false, animating: false, artAllows: true), "combat mode in a round");
        Assert.IsTrue(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: false, autoAnimates: true, animating: false, artAllows: true));
        world.Points[Npc] = 0;
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: false, autoAnimates: true, animating: false, artAllows: true), "no action points left");
        world.Actor = Foe;
        world.Points[Foe] = 5;
        Assert.IsFalse(AiExecutorRules.FidgetAfterRun(world, Npc, combatMode: false, autoAnimates: true, animating: false, artAllows: true), "another critter's turn");
    }

    // ---- the attack ----

    [TestMethod]
    public void AdjacentMeleeAttackSwingsHitsAndFinishesAfterOneAttack()
    {
        var world = Arena(1);
        var steps = Drive(AttackRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "swing pc", "hit pc" }, world.Calls);
        CollectionAssert.AreEqual(new[] { 400, 600, 0 }, steps.Select(s => s.DelayMs).ToArray());
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        Assert.AreEqual(AiExecutorReason.AttackHit, steps[^1].Reason);
    }

    [TestMethod]
    public void AnAttackStartThatFailsForAMissingArtEndsTheGoalWithNoHit()
    {
        // ARC-COMBAT-1932 (0x42B9C0): the node fails when the attack ART exists in no file; the goal ends, no action frame and no hit step.
        var world = Arena(1);
        world.AttackArtMissing = true;
        var steps = Drive(AttackRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "swing pc" }, world.Calls, "the start was asked, no hit followed");
        Assert.AreEqual(AiExecutorReason.AttackNoArt, steps[^1].Reason);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
    }

    [TestMethod]
    public void MeleeApproachPlaysTheAlertedSoundOnceAndAttacksWhenAdjacent()
    {
        var world = Arena(4);
        var steps = Drive(AttackRun(sound: 7), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Attack", "sound 7", "step 11,10", "step 12,10", "step 13,10", "swing pc", "hit pc" }, world.Calls);
        Assert.AreEqual(AiExecutorReason.AttackApproach, steps[0].Reason);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
    }

    [TestMethod]
    public void TheTargetStepsAwayBeforeTheActionFrame()
    {
        var world = Arena(1);
        var first = AiExecutorRules.Step(AttackRun(), world, Rolls, Steamwoven);
        world.At[Foe] = new(13, 10);
        var second = AiExecutorRules.Step(first.Run!, world, Rolls, Steamwoven);
        Assert.AreEqual(AiExecutorReason.AttackOutOfReach, second.Reason);
        CollectionAssert.DoesNotContain(world.Calls, "hit pc");
    }

    [TestMethod]
    public void AGoneOrDeadTargetOrAnImpossibleAttackEndsTheRun()
    {
        var world = Arena(1);
        world.DeadSet.Add(Foe);
        world.Round = true;
        world.Actor = Npc;
        world.PendingCost = 2;
        world.Points[Npc] = 5;
        var step = AiExecutorRules.Step(AttackRun(), world, Rolls, Original);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.AttackTargetGone), (step.End, step.Reason), "AG2: the attack goal pops");
        Assert.AreEqual(5, world.Points[Npc], "no end step, so no EX6 charge");

        world = Arena(1);
        world.Attack = new(TargetGone: true);
        world.Round = true;
        world.Actor = Npc;
        step = AiExecutorRules.Step(AttackRun(), world, Rolls, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.AttackImpossible), (step.End, step.Reason));
        CollectionAssert.AreEqual(new[] { "end subturn" }, world.Calls);
    }

    [TestMethod]
    public void KnockedDownAndConcealedNpcsGetUpAndUnconcealFirst()
    {
        var world = Arena(1);
        world.Movers[Npc] = new(KnockedDown: true, Concealed: true, Fatigue: 20, MaxFatigue: 20);
        var steps = Drive(AttackRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "get up", "unconceal", "swing pc", "hit pc" }, world.Calls);
        // ARC-DEV-137: the get-up is goal 63 pushed above the attack (its start is the first step, ending at once; its one frame then pops it and the attack goes on in the same step), the unconceal is still the world's delay.
        CollectionAssert.AreEqual(new[] { 0, 200, 400, 600, 0 }, steps.Select(s => s.DelayMs).ToArray());
    }

    [TestMethod]
    public void NoPathPausesOneSecondInRealTimeAndEndsAtOnceInARound()
    {
        var world = Arena(4);
        world.NoPaths = true;
        var steps = Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual(2, steps.Count);
        Assert.AreEqual(AiExecutorRules.NoPathPauseMs, steps[0].DelayMs);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.AttackNoPath), (steps[1].End, steps[1].Reason));

        world = Arena(4);
        world.NoPaths = true;
        world.Round = true;
        world.Actor = Npc;
        steps = Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual(AiRunEnd.EndStep, steps[0].End);
        CollectionAssert.AreEqual(new[] { "plan Attack", "end subturn" }, world.Calls);
    }

    [TestMethod]
    public void AdjacentButOutOfReachPausesThenEnds()
    {
        var world = Arena(1);
        world.LineClosed = true;
        var steps = Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual(AiExecutorRules.NoPathPauseMs, steps[0].DelayMs);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.AttackBlocked), (steps[^1].End, steps[^1].Reason));
    }

    [TestMethod]
    public void ARangedAttackerInReachShootsAtOnce()
    {
        var world = Arena(6);
        world.Weapon = true;
        world.Range = 10;
        Drive(AttackRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "swing pc", "hit pc" }, world.Calls);
    }

    [TestMethod]
    public void RangedApproachStopsInReachButTheOriginalWalksToTheTarget()
    {
        // EX9: the target is 14 tiles away, the weapon reaches 10.
        var world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual(new CellCoord(14, 10), world.At[Npc], "Steamwoven stops at the first tile in reach");
        CollectionAssert.Contains(world.Calls, "hit pc");

        world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        Drive(AttackRun(), world, Original);
        Assert.AreEqual(new CellCoord(23, 10), world.At[Npc], "the original walks on to the adjacent tile");
        CollectionAssert.Contains(world.Calls, "hit pc");
    }

    [TestMethod]
    public void ABlockedStepRePlansTheMeleeApproachButEndsTheRangedOne()
    {
        // EX20: the melee approach pops back to the combat approach, which plans again.
        var world = Arena(4);
        world.Blocked.Add(new(12, 10));
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 30;
        var steps = Drive(AttackRun(), world, Steamwoven, limit: 3);
        Assert.AreEqual((150, AiExecutorReason.StepBlocked), (steps[1].DelayMs, steps[1].Reason), "the blocked step's wait");
        Assert.AreEqual(2, world.Calls.Count(c => c == "plan Attack"), "re-planned after the blocked step");
        Assert.IsTrue(world.Calls.Contains("ap -2"));
        Assert.IsTrue(steps.All(s => !s.Ended));

        world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        world.Blocked.Add(new(12, 10));
        steps = Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.StepBlocked), (steps[^1].End, steps[^1].Reason));
    }

    // ---- action points ----

    [TestMethod]
    public void InARoundStepsAndTheAttackArePaid()
    {
        var world = Arena(3);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 10;
        Drive(AttackRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Attack", "ap -2", "step 11,10", "ap -2", "step 12,10", "ap -3", "swing pc", "hit pc" }, world.Calls);
        Assert.AreEqual(3, world.Points[Npc]);
    }

    [TestMethod]
    public void AnUnpaidStepKillsTheRunWithoutTheEndStep()
    {
        var world = Arena(4);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 3;
        var steps = Drive(AttackRun(), world, Original);
        Assert.AreEqual((AiRunEnd.Killed, AiExecutorReason.Unpaid), (steps[^1].End, steps[^1].Reason));
        Assert.AreEqual(1, world.Points[Npc], "action points unchanged by the refused step");
        Assert.AreEqual(2, world.PendingCost, "the unpaid cost stays pending");
    }

    [TestMethod]
    public void TheOriginalEndStepChargesThePendingCostAgain()
    {
        // EX6: a flight that walked and then ends pays its last step's cost a second time in the Original setting.
        foreach (var (profile, expected) in new[] { (Steamwoven, new[] { "ap -1", "ap -1", "ap -2" }), (Original, new[] { "ap -1", "ap -1", "ap -2", "ap -1" }) })
        {
            var world = Arena(1);
            world.At[Npc] = new(18, 10);
            world.At[Foe] = new(10, 10);
            world.Round = true;
            world.Actor = Npc;
            world.Points[Npc] = 20;
            var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, profile);
            Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.FleeSafe), (steps[^1].End, steps[^1].Reason), profile.Name);
            CollectionAssert.AreEqual(expected, world.Calls.Where(c => c.StartsWith("ap", StringComparison.Ordinal)).ToArray(), profile.Name);
        }
    }

    // ---- the flight ----

    [TestMethod]
    public void AFlightRunsTheEscapeLineUntilBeyondNineTiles()
    {
        var world = Arena(1);
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        Assert.AreEqual(new CellCoord(1, 10), world.At[Npc], "10 tiles from the source");
        Assert.IsTrue(world.Calls.Where(c => c.StartsWith("plan", StringComparison.Ordinal)).All(c => c == "plan Escape straight"));
        Assert.AreEqual(2, world.Calls.Count(c => c.StartsWith("plan", StringComparison.Ordinal)), "re-planned after an 8-step segment");
        Assert.IsTrue(world.Calls.Where(c => c.StartsWith("step", StringComparison.Ordinal)).All(c => c.EndsWith(" run", StringComparison.Ordinal)));
        Assert.AreEqual(AiExecutorReason.FleeSafe, steps[^1].Reason);
    }

    [TestMethod]
    public void AnEncumberedFlightWalks()
    {
        var world = Arena(1);
        world.Movers[Npc] = new(Encumbrance: 4, Fatigue: 20, MaxFatigue: 20);
        Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        Assert.IsTrue(world.Calls.Where(c => c.StartsWith("step", StringComparison.Ordinal)).All(c => !c.EndsWith(" run", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AFlaggedNpcRunsOnlyAsAProwlingMaster()
    {
        // ARC-AI-1711 (0x42EDC0, 0x42E9B0): a critter whose concealment flag is set sneaks or walks below master and runs as a master; an unflagged one is not asked.
        foreach (var (flagged, training, runs) in new[] { (true, SkillTraining.None, false), (true, SkillTraining.Apprentice, false), (true, SkillTraining.Expert, false), (true, SkillTraining.Master, true), (false, SkillTraining.None, true) })
        {
            var world = Arena(1);
            world.Movers[Npc] = new(Prowling: flagged, ProwlingTraining: training, Fatigue: 20, MaxFatigue: 20);
            Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
            var steps = world.Calls.Where(c => c.StartsWith("step", StringComparison.Ordinal)).ToArray();
            Assert.IsTrue(steps.Length > 0);
            Assert.AreEqual(runs, steps.All(c => c.EndsWith(" run", StringComparison.Ordinal)), $"flagged {flagged}, {training}");
        }
        Assert.IsTrue(AiExecutorRules.RunPermitted(new(Pc: true, Prowling: true)), "the PC's stealth gait is the viewer's");
        // ARC-RENDER-2539 (0x42EDC0): a flagged critter below prowling expert steps the stealth walk; an expert walks, a master runs; an unflagged one and the PC never sneak.
        Assert.IsTrue(AiExecutorRules.Sneaks(new(Prowling: true, ProwlingTraining: SkillTraining.None)));
        Assert.IsTrue(AiExecutorRules.Sneaks(new(Prowling: true, ProwlingTraining: SkillTraining.Apprentice)));
        Assert.IsFalse(AiExecutorRules.Sneaks(new(Prowling: true, ProwlingTraining: SkillTraining.Expert)));
        Assert.IsFalse(AiExecutorRules.Sneaks(new(Prowling: true, ProwlingTraining: SkillTraining.Master)));
        Assert.IsFalse(AiExecutorRules.Sneaks(new(Prowling: false, ProwlingTraining: SkillTraining.None)));
        Assert.IsFalse(AiExecutorRules.Sneaks(new(Pc: true, Prowling: true, ProwlingTraining: SkillTraining.None)));
        Assert.IsFalse(AiExecutorRules.RunPermitted(new(Encumbrance: 4, Prowling: true, ProwlingTraining: SkillTraining.Master)));
    }

    [TestMethod]
    public void ACorneredNpcFacesItsSourceAndNeverFleesAgain()
    {
        var world = Arena(1);
        world.NoPaths = true;
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Escape straight", "face pc", "no-flee" }, world.Calls);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.FleeCornered), (steps[^1].End, steps[^1].Reason));

        world = Arena(1);
        world.NoPaths = true;
        world.Movers[Npc] = new(CannotTurn: true, Fatigue: 20, MaxFatigue: 20);
        Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Escape straight" }, world.Calls, "a stunned NPC neither turns nor gets NO_FLEE");

        world = Arena(1);
        world.GoneSet.Add(Foe);
        Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "face pc", "no-flee" }, world.Calls, "no escape from a destroyed source");
    }

    [TestMethod]
    public void ABlockedWayBackEndsTheFlight()
    {
        var world = Arena(3);
        world.At[Npc] = new(13, 10);
        world.At[Foe] = new(10, 10);
        world.Blocked.Add(new(12, 10));
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 5;
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.Flee, Foe), GoalRunFlags.Flight), world, Steamwoven);
        Assert.AreEqual(AiExecutorReason.FleeSafe, steps[^1].Reason);
        CollectionAssert.AreEqual(new[] { "ap -2" }, world.Calls);
    }

    [TestMethod]
    public void TheSpreadOutMoveAwaySetsNoFleeOnlyInTheOriginal()
    {
        // EX12: a follower spread out next to its leader with no way out.
        foreach (var (profile, expected) in new[] { (Steamwoven, 0), (Original, 1) })
        {
            var world = new World();
            world.At[Npc] = new(5, 5);
            world.At[Leader] = new(6, 5);
            world.Movers[Npc] = new(SpreadOut: true, Fatigue: 20, MaxFatigue: 20);
            world.NoPaths = true;
            Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.SpreadOut, Leader)), world, profile);
            Assert.AreEqual(expected, world.Calls.Count(c => c == "no-flee"), profile.Name);
            Assert.IsTrue(world.Calls.Contains("plan Escape"), "the spread-out escape line may search");
        }
        foreach (var (profile, expected) in new[] { (Steamwoven, 0), (Original, 1) })
        {
            var world = new World();
            world.At[Npc] = new(5, 5);
            world.At[Leader] = new(6, 5);
            world.Movers[Npc] = new(SpreadOut: true, Fatigue: 20, MaxFatigue: 20);
            world.StoppedShort = true;
            Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.SpreadOut, Leader)), world, profile, limit: 3);
            Assert.AreEqual(expected, world.Calls.Count(c => c == "no-flee"), $"{profile.Name}: a bent escape line");
        }
    }

    [TestMethod]
    public void TheSpreadOutGoalMovesToSixTilesFromTheLeader()
    {
        var world = new World();
        world.At[Npc] = new(5, 5);
        world.At[Leader] = new(6, 5);
        world.Movers[Npc] = new(SpreadOut: true, Fatigue: 20, MaxFatigue: 20);
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.SpreadOut, Leader)), world, Steamwoven, limit: 12);
        // To the tile 6 steps from the leader through the follower, then back within the spread-out range of 5, where it stands.
        CollectionAssert.AreEqual(new[] { "plan Escape", "step 4,5", "step 3,5", "step 2,5", "step 1,5", "step 0,5", "plan Near", "step 1,5" },
            world.Calls.Take(8).ToArray());
        Assert.AreEqual(AiExecutorReason.FollowInRange, steps[^1].Reason, "then it stands in range");
    }

    // ---- the back-off run and following ----

    [TestMethod]
    public void TheBackOffRunRunsToItsTile()
    {
        var world = Arena(1);
        world.Bits = DangerFlags.BackingOff | DangerFlags.Fighting;
        world.Away = new(5, 10);
        var perform = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual(AiPerformKind.BackOffRun, perform.Kind);
        Assert.IsTrue(perform.Run!.Flags.HasFlag(GoalRunFlags.Running));
        var steps = Drive(perform.Run, world, Steamwoven);
        Assert.AreEqual(new CellCoord(5, 10), world.At[Npc]);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.RunToTile), (steps[^1].End, steps[^1].Reason));
    }

    [TestMethod]
    public void TheBackOffEndsWhenReachedOrWithoutAnAwayLine()
    {
        var world = Arena(1);
        world.Bits = DangerFlags.BackingOff;
        Assert.AreEqual(AiPerformKind.BackOffEnded, AiExecutorRules.Perform(world, Npc, Foe, null, null, null).Kind);
        world.BackOffStep = new(BackOffRunStep.Reached, 5);
        Assert.AreEqual(AiPerformKind.BackOffEnded, AiExecutorRules.Perform(world, Npc, Foe, null, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "end back-off", "end back-off" }, world.Calls);
    }

    [TestMethod]
    public void AStartedBackOffWithNoAwayLineEndsInTheSamePassAndAttacksNothing()
    {
        var world = Arena(1);
        world.BackOffStart = true;
        var perform = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.BackOffEnded, false), (perform.Kind, perform.Started));
        CollectionAssert.AreEqual(new[] { "back-off start?", "end back-off" }, world.Calls, "no path clears BACKING_OFF; the grenade step and the attack are not reached");
    }

    [TestMethod]
    public void AFollowerWalksWithinFourAndRunsBeyond()
    {
        foreach (var (distance, reason, running) in new[] { (4, AiExecutorReason.FollowWalk, false), (9, AiExecutorReason.FollowRun, true) })
        {
            var world = new World();
            world.At[Npc] = new(0, 0);
            world.At[Leader] = new(distance, 0);
            var first = AiExecutorRules.Step(new GoalRun(Npc, default, new GoalFrame(GoalType.Follow, Leader)), world, Rolls, Steamwoven);
            Assert.AreEqual(reason, first.Reason);
            var steps = Drive(first.Run!, world, Steamwoven);
            Assert.AreEqual(running, world.Calls.Any(c => c.EndsWith(" run", StringComparison.Ordinal)), $"distance {distance}");
            Assert.AreEqual(AiExecutorRules.FollowRange, AiExecutorRules.Distance(world.At[Npc], world.At[Leader]));
            Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        }
    }

    [TestMethod]
    public void AFollowerInRangePaysTwoEveryTenthOfASecond()
    {
        var world = new World();
        world.At[Npc] = new(0, 0);
        world.At[Leader] = new(1, 0);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 5;
        var run = new GoalRun(Npc, default, new GoalFrame(GoalType.Follow, Leader));
        for (var i = 0; i < 3; i++)
        {
            var step = AiExecutorRules.Step(run, world, Rolls, Steamwoven);
            Assert.AreEqual((AiExecutorRules.FollowRecheckMs, AiExecutorReason.FollowInRange), (step.DelayMs, step.Reason));
            run = step.Run!;
        }
        CollectionAssert.AreEqual(new[] { "ap -2", "ap -2", "ap -2" }, world.Calls);
    }

    // ---- perform and the starters ----

    [TestMethod]
    public void PerformDoesNothingOnAnotherCrittersTurn()
    {
        var world = Arena(1);
        world.Round = true;
        world.Actor = Foe;
        Assert.AreEqual(AiPerformKind.NotActor, AiExecutorRules.Perform(world, Npc, Foe, new AiPassChoice(AiPassAction.Cast, 11, -1, null, Foe), null, null).Kind);
        Assert.AreEqual(0, world.Calls.Count);
    }

    [TestMethod]
    public void PerformRunsThePassActionsBeforeTheDangerState()
    {
        foreach (var (action, kind, goal) in new[]
                 {
                     (AiPassAction.Cast, AiPerformKind.Cast, (GoalType?)null), (AiPassAction.UseItem, AiPerformKind.UseItem, GoalType.UseItem),
                     (AiPassAction.UseSkill, AiPerformKind.UseSkill, GoalType.UseSkill),
                 })
        {
            var world = Arena(1);
            var perform = AiExecutorRules.Perform(world, Npc, Foe, new AiPassChoice(action, 11, 10, "oid:kit", Foe), null, null);
            Assert.AreEqual((kind, goal), (perform.Kind, perform.Run?.Bottom.Goal));
            CollectionAssert.DoesNotContain(world.Calls, "back-off start?", "the danger state's branch is not reached");
        }
    }

    [TestMethod]
    public void CombatPerformTriesTheBackOffThenAGrenadeThenTheAttack()
    {
        var world = Arena(1);
        world.BackOffStart = true;
        world.Away = new(5, 10);
        var started = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.BackOffRun, GoalType.RunToTile, new CellCoord(5, 10), true), (started.Kind, started.Run!.Bottom.Goal, started.Run.Bottom.Tile, started.Started),
            "the back-off starts and runs in the same perform");
        CollectionAssert.AreEqual(new[] { "back-off start?" }, world.Calls, "no grenade step and no attack after it");
        world = Arena(1);
        world.Grenades = true;
        var grenade = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.Grenade, GoalType.ThrowItem, "oid:grenade", world.At[Foe]),
            (grenade.Kind, grenade.Run!.Bottom.Goal, grenade.Run.Bottom.Item, grenade.Run.Bottom.Tile));
        CollectionAssert.AreEqual(new[] { "back-off start?", "grenade?" }, world.Calls);
        world = Arena(1);
        var attack = AiExecutorRules.Perform(world, Npc, Foe, null, 12, null);
        Assert.AreEqual(AiPerformKind.Attack, attack.Kind);
        Assert.AreEqual((GoalType.Attack, Foe, 12), (attack.Run!.Bottom.Goal, attack.Run.Bottom.Target, attack.Run.Bottom.Sound));
        Assert.IsTrue(attack.Run.Flags.HasFlag(GoalRunFlags.Running), "fatigue above 8");
    }

    [TestMethod]
    public void TheAttackStarterRefusesKeepsOrRetargets()
    {
        var world = Arena(1);
        Assert.AreEqual(AiStartRefusal.Self, AiExecutorRules.StartAttack(world, Npc, Npc, null, null).Refusal);
        world.Throws = true;
        var thrown = AiExecutorRules.StartAttack(world, Npc, Foe, null, null);
        Assert.AreEqual((AiPerformKind.ThrowWeapon, GoalType.ThrowItem, "oid:dagger"), (thrown.Kind, thrown.Run!.Bottom.Goal, thrown.Run.Bottom.Item));
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 0;
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        world.InterruptFails = true;
        var refusedThrow = AiExecutorRules.StartAttack(world, Npc, Foe, null, null);
        Assert.AreEqual((AiPerformKind.ThrowWeapon, AiStartRefusal.Interrupt), (refusedThrow.Kind, refusedThrow.Refusal), "the weapon is off even so (AG13)");
        world.Round = false;
        world.Movers.Remove(Npc);
        world.InterruptFails = false;
        world.Calls.Remove("unwield oid:dagger");
        world.Throws = false;
        var running = AttackRun();
        var same = AiExecutorRules.StartAttack(world, Npc, Foe, null, running);
        Assert.AreEqual((AiStartRefusal.AlreadyQueued, running, false), (same.Refusal, same.Run, same.Started));
        world.At["oid:other"] = new(9, 10);
        var other = AiExecutorRules.StartAttack(world, Npc, "oid:other", null, running);
        Assert.AreEqual(("oid:other", AiStartRefusal.None, true), (other.Run!.Bottom.Target, other.Refusal, other.Started));
        world.InterruptFails = true;
        Assert.AreEqual(AiStartRefusal.Interrupt, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        world.InterruptFails = false;
        world.Boomerang = true;
        Assert.AreEqual(AiStartRefusal.BoomerangInFlight, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        world.Boomerang = false;
        world.Attack = new(BodyOfAir: true);
        Assert.AreEqual(AiStartRefusal.BodyOfAir, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        CollectionAssert.AreEqual(new[] { "unwield oid:dagger" }, world.Calls, "no critter shares the tile, so the step apart after each refusal asks nobody");
        world.Attack = new();
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 2;
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
    }

    [TestMethod]
    public void ARetargetRestartsAnApproachButASwingIsKept()
    {
        // The run approaches the PC (attack, combat approach, melee walk); the next pass picks another target.
        var world = Arena(6);
        world.At["oid:other"] = new(10, 14);
        var approach = AiExecutorRules.Step(AttackRun(), world, Rolls, Steamwoven).Run!;
        Assert.AreEqual(3, approach.Frames.Length);
        var retarget = AiExecutorRules.StartAttack(world, Npc, "oid:other", null, approach);
        Assert.IsTrue(retarget.Started);
        Assert.AreEqual(1, retarget.Run!.Frames.Length, "no frame keeps the old target");
        Drive(retarget.Run, world, Steamwoven);
        CollectionAssert.Contains(world.Calls, "hit oid:other");

        world = Arena(1);
        var swinging = AiExecutorRules.Step(AttackRun(), world, Rolls, Steamwoven).Run!;
        Assert.AreEqual(GoalType.AttemptAttack, swinging.Bottom.Goal);
        foreach (var target in new[] { Foe, "oid:other" })
        {
            world.At["oid:other"] = new(11, 11);
            var again = AiExecutorRules.StartAttack(world, Npc, target, null, swinging);
            Assert.AreEqual((AiStartRefusal.AlreadyQueued, swinging, false), (again.Refusal, again.Run, again.Started), target);
        }
    }

    [TestMethod]
    public void AHeldWalkerWaitsInPlaceWithoutCostAndWalksOnWhenTheHoldEnds()
    {
        // ARC-DEV-302 (GT148): node 9's refusal restarts the node with delay 0: no step, no wait drawn, the run stays.
        var world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        world.Held = true;
        var draws = Rolls.Draws;
        var held = Drive(AttackRun(), world, Steamwoven, limit: 12);
        Assert.IsTrue(held.All(s => !s.Ended), "the run stays");
        Assert.AreEqual(new CellCoord(10, 10), world.At[Npc]);
        Assert.AreEqual((0, AiExecutorReason.StepHeld), (held[^1].DelayMs, held[^1].Reason));
        Assert.AreEqual(draws, Rolls.Draws, "no wait drawn");
        world.Held = false;
        var walked = Drive(held[^1].Run!, world, Steamwoven);
        Assert.AreNotEqual(new CellCoord(10, 10), world.At[Npc], "the walk goes on");
    }

    [TestMethod]
    public void AStepOntoATrapObjectEndsTheWalkOnThatTile()
    {
        // ARC-WORLD-1469 (TT9, 0x4305D0): the step that meets a trap object ends the walk on its tile; the attack goal's walk then ends and the next frame plans again from there.
        var world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        world.TrapAt.Add(new(12, 10));
        Drive(AttackRun(), world, Steamwoven);
        var at = world.Calls.IndexOf("trap 12,10");
        Assert.AreEqual(1, world.Calls.Count(c => c == "trap 12,10"), "met once: " + string.Join("; ", world.Calls));
        Assert.AreEqual("step 12,10", world.Calls[at - 1], "the trap is the one the step onto 12,10 met");
        Assert.AreEqual("plan Attack", world.Calls[at + 1], "the walk ended there (the path index became its length): the next step is a new plan from the trap's tile, not the old path's next tile");
    }

    [TestMethod]
    public void AStepThatCannotStartGivesUpWithoutTheWait()
    {
        var world = Arena(14);
        world.Weapon = true;
        world.Range = 10;
        world.Stuck.Add(new(12, 10));
        var draws = Rolls.Draws;
        var steps = Drive(AttackRun(), world, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.StepBlocked), (steps[^1].End, steps[^1].Reason));
        Assert.AreEqual(draws, Rolls.Draws, "no wait drawn");
        Assert.AreEqual(new CellCoord(11, 10), world.At[Npc]);

        // A melee walk re-plans onto the same stuck step in the next frame, not within one step.
        world = Arena(4);
        world.Stuck.Add(new(12, 10));
        var first = Drive(AttackRun(), world, Steamwoven, limit: 4);
        Assert.IsTrue(first.All(s => !s.Ended && s.Nodes < AiExecutorRules.MaxNodes));
        Assert.AreEqual((0, AiExecutorReason.StepBlocked), (first[1].DelayMs, first[1].Reason));
    }

    [TestMethod]
    public void TheHundredAndFirstNodeEndsTheSubturnAndKillsTheRun()
    {
        // A spread-out follower next to its leader whose step toward the leader is blocked: spread out pushes move away, which pops
        // at once, again and again within one step.
        var world = new World();
        world.At[Npc] = new(5, 5);
        world.At[Leader] = new(7, 5);
        world.Blocked.Add(new(6, 5));
        world.Movers[Npc] = new(SpreadOut: true, Fatigue: 20, MaxFatigue: 20);
        world.Round = true;
        world.Actor = Npc;
        var step = AiExecutorRules.Step(new GoalRun(Npc, default, new GoalFrame(GoalType.SpreadOut, Leader)), world, Rolls, Steamwoven);
        Assert.AreEqual((AiRunEnd.NodeLimit, AiExecutorRules.MaxNodes), (step.End, step.Nodes));
        CollectionAssert.AreEqual(new[] { "end subturn" }, world.Calls);
    }

    [TestMethod]
    public void ARunHoldsOneToEightGoals()
    {
        var run = AttackRun();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => run.WithFrames([]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => run.WithFrames([.. Enumerable.Repeat(run.Bottom, AiExecutorRules.MaxDepth + 1)]));
    }

    [TestMethod]
    public void FleePerformStartsTheFlightWithItsFlag()
    {
        var world = Arena(1);
        world.State = DangerState.Flee;
        var flee = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.Flee, GoalType.Flee, GoalRunFlags.Flight, true), (flee.Kind, flee.Run!.Bottom.Goal, flee.Run.Flags, flee.Started));
        world.Fleeing = new(false, Foe);
        var kept = AiExecutorRules.Perform(world, Npc, Foe, null, null, flee.Run);
        Assert.AreEqual((flee.Run, false), (kept.Run, kept.Started), "no goal: the current run is kept");
        world.State = DangerState.Surrender;
        Assert.AreEqual(AiPerformKind.Surrender, AiExecutorRules.Perform(world, Npc, Foe, null, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "perform fleeing", "perform fleeing", "perform surrender" }, world.Calls);
    }

    [TestMethod]
    public void NonCombatPerformFollowsTheLeaderOrRunsTheRoutine()
    {
        var world = Arena(1);
        world.State = DangerState.None;
        Assert.AreEqual(AiPerformKind.Routine, AiExecutorRules.Perform(world, Npc, null, null, null, null).Kind);
        world.Leaders[Npc] = Leader;
        world.At[Leader] = new(20, 10);
        var follow = AiExecutorRules.Perform(world, Npc, null, null, null, null);
        Assert.AreEqual((AiPerformKind.Follow, GoalType.Follow, GoalRunFlags.None), (follow.Kind, follow.Run!.Bottom.Goal, follow.Run.Flags));
        Assert.AreEqual(AiStartRefusal.AlreadyQueued, AiExecutorRules.Perform(world, Npc, null, null, null, follow.Run).Refusal);
        world.At[Leader] = new(14, 10);
        Assert.AreEqual(AiStartRefusal.LeaderNear, AiExecutorRules.Perform(world, Npc, null, null, null, null).Refusal);
        world.Waits.Add(Npc);
        Assert.AreEqual(AiPerformKind.Waiting, AiExecutorRules.Perform(world, Npc, null, null, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "leave combat mode", "routine", "leave combat mode", "loyalty started", "leave combat mode", "loyalty",
            "leave combat mode", "loyalty", "leave combat mode" }, world.Calls);
    }

    [TestMethod]
    public void APushBeyondEightGoalsEndsTheRun()
    {
        var world = Arena(4);
        var frames = Enumerable.Range(0, AiExecutorRules.MaxDepth).Select(_ => new GoalFrame(GoalType.Attack, Foe)).ToArray();
        var run = new GoalRun(Npc, default, frames[0]).WithFrames([.. frames]);
        Assert.AreEqual(AiRunEnd.Overflow, AiExecutorRules.Step(run, world, Rolls, Steamwoven).End);
    }

    // ---- critters sharing a tile (ARC-AI-1315, ledger VA4, VAG4: 0x436720, 0x4363E0, 0x4364D0, goal 68) ----

    private static AiTileCritter Critter(string id, long order, bool pc = false, bool busy = false, bool living = true) => new(id, pc, living, busy, order);

    [TestMethod]
    public void TheFinderPairsTheBlockerWithTheSourceAndTheLargerHandleMoves()
    {
        var low = Critter("oid:low", 1);
        var high = Critter("oid:high", 2);
        // Two NPCs: the second of the pair is the one asked to move, the larger handle whoever is the source.
        Assert.AreEqual(new AiTileSharerPair(low, high), AiTileSharerRules.Find("oid:low", [low, high]));
        Assert.AreEqual(new AiTileSharerPair(low, high), AiTileSharerRules.Find("oid:high", [low, high]));
        Assert.AreEqual(high, AiTileSharerRules.Choose(low, high)!.Value.Mover);
        Assert.AreEqual(low, AiTileSharerRules.Choose(low, high)!.Value.AwayFrom);
        // A player character as the source: the NPC is the blocker and moves (a PC is never the blocker).
        var hero = Critter("pc", 0, pc: true);
        var pair = AiTileSharerRules.Find("pc", [hero, high])!.Value;
        Assert.AreEqual((high, hero), (pair.First, pair.Second));
        Assert.AreEqual((high, hero), (AiTileSharerRules.Choose(pair.First, pair.Second)!.Value.Mover, AiTileSharerRules.Choose(pair.First, pair.Second)!.Value.AwayFrom));
        // An NPC that shares a tile only with the hero finds no blocker: it attacks.
        Assert.IsNull(AiTileSharerRules.Find("oid:high", [hero, high]));
        // A busy source, a lone critter, and dead or busy company give nothing.
        Assert.IsNull(AiTileSharerRules.Find("oid:low", [Critter("oid:low", 1, busy: true), high]));
        Assert.IsNull(AiTileSharerRules.Find("oid:low", [low]));
        Assert.IsNull(AiTileSharerRules.Find("oid:low", [low, Critter("oid:high", 2, busy: true)]));
        Assert.IsNull(AiTileSharerRules.Find("oid:low", [low, Critter("oid:high", 2, living: false)]));
        // The first living, not busy NPC in the tile's order is the blocker, not the smallest handle.
        var third = Critter("oid:third", 3);
        Assert.AreEqual(new AiTileSharerPair(low, high), AiTileSharerRules.Find("oid:low", [low, Critter("oid:dead", 9, living: false), high, third]));
    }

    [TestMethod]
    public void ThePleaseMoveChoiceSendsTheNpcAwayFromAPlayerCharacterAndTheLargerOfTwoPlayers()
    {
        var hero = Critter("pc", 5, pc: true);
        var npc = Critter("oid:npc", 1);
        Assert.AreEqual(npc, AiTileSharerRules.Choose(hero, npc)!.Value.Mover, "second is the NPC");
        Assert.AreEqual(npc, AiTileSharerRules.Choose(npc, hero)!.Value.Mover, "second is the player character: the first (an NPC) moves");
        var other = Critter("pc2", 6, pc: true);
        Assert.AreEqual(other, AiTileSharerRules.Choose(hero, other)!.Value.Mover);
        Assert.AreEqual(other, AiTileSharerRules.Choose(other, hero)!.Value.Mover, "two players: the larger handle moves");
        Assert.IsNull(AiTileSharerRules.Choose(npc, npc));
    }

    [TestMethod]
    public void AnAttackerSharingATileAsksTheBlockerToMoveInsteadOfAttacking()
    {
        var world = Arena(1);
        world.Sharers.AddRange([Critter(Npc, 1), Critter("oid:other", 2)]);
        var result = AiExecutorRules.StartAttack(world, Npc, Foe, null, null);
        Assert.AreEqual((AiStartRefusal.TileSharer, false, null), (result.Refusal, result.Started, result.Run));
        CollectionAssert.AreEqual(new[] { $"please move oid:other from {Npc}" }, world.Calls, "no attack, no step apart, one please-move start");
        // Round: only the current participant passes the start gate (0x4348E0), so a mover that is not the actor is not asked, and the attack still does not start.
        world.Calls.Clear();
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 10;
        Assert.AreEqual(AiStartRefusal.TileSharer, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        CollectionAssert.AreEqual(Array.Empty<string>(), world.Calls);
        // Alone on the tile, the attacker attacks as before.
        world.Round = false;
        world.Sharers.Clear();
        Assert.IsTrue(AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Started);
    }

    [TestMethod]
    public void AFoundBlockerThatCannotMoveStillEndsTheAttackStartWithoutAStepApart()
    {
        var world = Arena(1);
        // The finder found the blocker (idle NPC, larger handle); the round's gate refuses the mover, which is not the actor: nothing starts, no step apart, no attack.
        world.Sharers.AddRange([Critter(Npc, 1), Critter("oid:other", 2), Critter("oid:third", 3)]);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 10;
        var result = AiExecutorRules.StartAttack(world, Npc, Foe, null, null);
        Assert.AreEqual((AiStartRefusal.TileSharer, false), (result.Refusal, result.Started));
        CollectionAssert.AreEqual(Array.Empty<string>(), world.Calls);
        // A blocker that is busy is not one: the attacker meets the next idle NPC of the tile's list.
        world.Round = false;
        world.Sharers.Clear();
        world.Sharers.AddRange([Critter(Npc, 1), Critter("oid:busy", 2, busy: true), Critter("oid:third", 3)]);
        Assert.AreEqual(AiStartRefusal.TileSharer, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        CollectionAssert.AreEqual(new[] { $"please move oid:third from {Npc}" }, world.Calls);
    }

    [TestMethod]
    public void ARefusedAttackStartStepsApartTheOtherNpcsOnTheTile()
    {
        var world = Arena(1);
        // The attacker is busy (it has its run), so the finder gives nothing; the boomerang refuses the start and the step apart asks the others.
        world.Sharers.AddRange([Critter(Npc, 1, busy: true), Critter("oid:a", 2), Critter("oid:b", 3), Critter("oid:c", 4)]);
        world.Boomerang = true;
        Assert.AreEqual(AiStartRefusal.BoomerangInFlight, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        // One stays (the smallest handle among the idle NPCs, a, which a busy attacker does not count against); the rest are asked, the last listed first.
        CollectionAssert.AreEqual(new[] { "please move oid:c from oid:a", "please move oid:b from oid:a" }, world.Calls);
        // A player character on the tile stays, and every idle NPC is asked to move away from it.
        world.Calls.Clear();
        world.Sharers.Clear();
        world.Sharers.AddRange([Critter(Npc, 4, busy: true), Critter("oid:a", 2), Critter("pc", 9, pc: true)]);
        AiExecutorRules.StartAttack(world, Npc, Foe, null, null);
        CollectionAssert.AreEqual(new[] { "please move oid:a from pc" }, world.Calls);
        // A gate refusal (not the actor in a round) returns without it.
        world.Calls.Clear();
        world.Round = true;
        world.Actor = "oid:a";
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        CollectionAssert.AreEqual(Array.Empty<string>(), world.Calls);
    }

    [TestMethod]
    public void TheStepApartNeedsTwoLivingIdleCrittersAndAsksNobodyOtherwise()
    {
        var world = Arena(1);
        world.Sharers.AddRange([Critter("oid:a", 1), Critter("oid:b", 2, living: false), Critter("oid:c", 3, busy: true)]);
        Assert.AreEqual(0, AiTileSharerRules.StepApart(world, "oid:a"));
        world.Sharers.Add(Critter("oid:d", 4));
        Assert.AreEqual(1, AiTileSharerRules.StepApart(world, "oid:a"));
        CollectionAssert.AreEqual(new[] { "please move oid:d from oid:a" }, world.Calls);
    }

    [TestMethod]
    public void GoalSixtyEightStepsToTheFirstClearNeighbourFromARandomRotationAndEnds()
    {
        var world = Arena(1);
        world.At[Npc] = new(10, 10);
        var run = new GoalRun(Npc, default, new GoalFrame(GoalType.PleaseMove, "oid:other"));
        // A draw of 8 starts at rotation 0 (the tile at −1, −1): the neighbour comes first, the walk is the run to a tile's.
        var steps = Drive(run, world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan ToTile", "step 9,9" }, world.Calls);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.RunToTile), (steps[^1].End, steps[^1].Reason));
        // A draw of 3 starts at rotation 3 (0, +1); a blocked neighbour is passed over to the next one around the circle.
        world = Arena(1);
        world.At[Npc] = new(10, 10);
        world.Blocked.Add(new(10, 11));
        var first = AiExecutorRules.Step(run, world, new FixedRolls(3), Steamwoven);
        Assert.AreEqual(new CellCoord(11, 11), first.Run!.Frames[0].Tile, "rotation 4");
        // Every neighbour blocked: no tile, the goal pops and the run ends.
        world = Arena(1);
        world.At[Npc] = new(10, 10);
        foreach (var rotation in Enumerable.Range(0, 8)) world.Blocked.Add(Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(new(10, 10), rotation));
        var none = AiExecutorRules.Step(run, world, Rolls, Steamwoven);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.PleaseMoveNoTile), (none.End, none.Reason));
        CollectionAssert.AreEqual(Array.Empty<string>(), world.Calls);
    }
}
