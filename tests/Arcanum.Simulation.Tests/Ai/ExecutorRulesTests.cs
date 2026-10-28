using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            Assert.AreEqual(AiRollPurpose.BlockedStepWait, purpose);
            Draws++;
            return Math.Clamp(value, min, max);
        }
    }

    /// <summary>An open grid: objects on tiles, straight-line paths, a clear line within <see cref="Range"/> unless closed.</summary>
    private sealed class World : IAiPerformWorld
    {
        public readonly Dictionary<string, CellCoord> At = [];
        public readonly Dictionary<string, AiMoverFacts> Movers = [];
        public readonly List<string> Calls = [];
        public readonly HashSet<CellCoord> Blocked = [];
        public readonly HashSet<string> GoneSet = [], DeadSet = [], Waits = [];
        public readonly Dictionary<string, int> Points = [];
        public readonly Dictionary<string, string> Leaders = [];
        public int Range = 1, Cost = 3, WalkMs = 100, RunMs = 50;
        public bool Weapon, LineClosed, NoPaths, StoppedShort, Round, Grenade, BackOffStart, Throws, InterruptFails, Boomerang;
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

        public bool StepBlocked(string id, CellCoord next, bool flight) => Blocked.Contains(next);

        public int BeginStep(string id, CellCoord next, bool running)
        {
            Calls.Add($"step {next.X},{next.Y}{(running ? " run" : "")}");
            At[id] = next;
            return running ? RunMs : WalkMs;
        }

        public int GetUp(string id) { Calls.Add("get up"); Movers[id] = Mover(id) with { KnockedDown = false }; return 300; }
        public int Unconceal(string id) { Calls.Add("unconceal"); Movers[id] = Mover(id) with { Concealed = false }; return 200; }
        public void Face(string id, string toward) => Calls.Add($"face {toward}");
        public void PlaySound(string id, int sound) => Calls.Add($"sound {sound}");
        public AiAttackSwing BeginAttack(string id, string target) { Calls.Add($"swing {target}"); return new(400, 600); }
        public void Hit(string id, string target) => Calls.Add($"hit {target}");
        public void SetNoFlee(string id) => Calls.Add("no-flee");
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
        public void Cast(string npc) => Calls.Add("cast");
        public void UseItem(string npc) => Calls.Add("use item");
        public void UseSkill(string npc) => Calls.Add("use skill");
        public void LeaveCombatMode(string npc) => Calls.Add("leave combat mode");
        public string? Leader(string npc) => Leaders.TryGetValue(npc, out var l) ? l : null;
        public bool Waiting(string npc) => Waits.Contains(npc);
        public void LoyaltyCheck(string npc) => Calls.Add("loyalty");
        public void RunRoutine(string npc) => Calls.Add("routine");
        public bool StartBackOff(string npc, string? target) { Calls.Add("back-off start?"); return BackOffStart; }
        public bool ThrowGrenade(string npc, string? target) { Calls.Add("grenade?"); return Grenade; }
        public bool ThrowsWieldedWeapon(string npc) => Throws;
        public void ThrowWeapon(string npc, string target) => Calls.Add($"throw at {target}");
        public bool BoomerangInFlight(string npc) => Boomerang;
        public bool InterruptLowerAnimations(string id) => !InterruptFails;
        public void StepApart(string id) => Calls.Add("step apart");
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

    private static AiGoalRun AttackRun(int? sound = null) => new(Npc, new AiGoalFrame(AiGoalType.Attack, Foe, sound: sound));

    /// <summary>Steps the run until it ends; returns the results in order.</summary>
    private static List<AiGoalStepResult> Drive(AiGoalRun run, World world, CombatAnomalyProfile profile, int limit = 200)
    {
        var results = new List<AiGoalStepResult>();
        for (var current = run; current is not null && results.Count < limit;)
        {
            var step = AiExecutorRules.Step(current, world, Rolls, profile);
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
        var step = AiExecutorRules.Step(AttackRun(), world, Rolls, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.AttackTargetGone), (step.End, step.Reason));

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
        CollectionAssert.AreEqual(new[] { 300, 200, 400, 600, 0 }, steps.Select(s => s.DelayMs).ToArray());
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
            var steps = Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, profile);
            Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.FleeSafe), (steps[^1].End, steps[^1].Reason), profile.Name);
            CollectionAssert.AreEqual(expected, world.Calls.Where(c => c.StartsWith("ap", StringComparison.Ordinal)).ToArray(), profile.Name);
        }
    }

    // ---- the flight ----

    [TestMethod]
    public void AFlightRunsTheEscapeLineUntilBeyondNineTiles()
    {
        var world = Arena(1);
        var steps = Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
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
        Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
        Assert.IsTrue(world.Calls.Where(c => c.StartsWith("step", StringComparison.Ordinal)).All(c => !c.EndsWith(" run", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ACorneredNpcFacesItsSourceAndNeverFleesAgain()
    {
        var world = Arena(1);
        world.NoPaths = true;
        var steps = Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Escape straight", "face pc", "no-flee" }, world.Calls);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.FleeCornered), (steps[^1].End, steps[^1].Reason));

        world = Arena(1);
        world.NoPaths = true;
        world.Movers[Npc] = new(CannotTurn: true, Fatigue: 20, MaxFatigue: 20);
        Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Escape straight" }, world.Calls, "a stunned NPC neither turns nor gets NO_FLEE");

        world = Arena(1);
        world.GoneSet.Add(Foe);
        Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
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
        var steps = Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Flee, Foe), AiRunFlags.Flight), world, Steamwoven);
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
            Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.SpreadOut, Leader)), world, profile);
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
            Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.SpreadOut, Leader)), world, profile, limit: 3);
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
        var steps = Drive(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.SpreadOut, Leader)), world, Steamwoven, limit: 12);
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
        var perform = AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null);
        Assert.AreEqual(AiPerformKind.BackOffRun, perform.Kind);
        Assert.IsTrue(perform.Run!.Flags.HasFlag(AiRunFlags.Running));
        var steps = Drive(perform.Run, world, Steamwoven);
        Assert.AreEqual(new CellCoord(5, 10), world.At[Npc]);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.RunToTile), (steps[^1].End, steps[^1].Reason));
    }

    [TestMethod]
    public void TheBackOffEndsWhenReachedOrWithoutAnAwayLine()
    {
        var world = Arena(1);
        world.Bits = DangerFlags.BackingOff;
        Assert.AreEqual(AiPerformKind.BackOffEnded, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Kind);
        world.BackOffStep = new(BackOffRunStep.Reached, 5);
        Assert.AreEqual(AiPerformKind.BackOffEnded, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "end back-off", "end back-off" }, world.Calls);
    }

    [TestMethod]
    public void AFollowerWalksWithinFourAndRunsBeyond()
    {
        foreach (var (distance, reason, running) in new[] { (4, AiExecutorReason.FollowWalk, false), (9, AiExecutorReason.FollowRun, true) })
        {
            var world = new World();
            world.At[Npc] = new(0, 0);
            world.At[Leader] = new(distance, 0);
            var first = AiExecutorRules.Step(new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Follow, Leader)), world, Rolls, Steamwoven);
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
        var run = new AiGoalRun(Npc, new AiGoalFrame(AiGoalType.Follow, Leader));
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
        Assert.AreEqual(AiPerformKind.NotActor, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.Cast, null, null).Kind);
        Assert.AreEqual(0, world.Calls.Count);
    }

    [TestMethod]
    public void PerformRunsThePassActionsBeforeTheDangerState()
    {
        foreach (var (action, kind, call) in new[]
                 {
                     (AiPassAction.Cast, AiPerformKind.Cast, "cast"), (AiPassAction.UseItem, AiPerformKind.UseItem, "use item"),
                     (AiPassAction.UseSkill, AiPerformKind.UseSkill, "use skill"),
                 })
        {
            var world = Arena(1);
            Assert.AreEqual(kind, AiExecutorRules.Perform(world, Npc, Foe, action, null, null).Kind);
            CollectionAssert.AreEqual(new[] { call }, world.Calls);
        }
    }

    [TestMethod]
    public void CombatPerformTriesTheBackOffThenAGrenadeThenTheAttack()
    {
        var world = Arena(1);
        world.BackOffStart = true;
        Assert.AreEqual(AiPerformKind.BackOffStart, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Kind);
        world = Arena(1);
        world.Grenade = true;
        Assert.AreEqual(AiPerformKind.Grenade, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "back-off start?", "grenade?" }, world.Calls);
        world = Arena(1);
        var attack = AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, 12, null);
        Assert.AreEqual(AiPerformKind.Attack, attack.Kind);
        Assert.AreEqual((AiGoalType.Attack, Foe, 12), (attack.Run!.Bottom.Goal, attack.Run.Bottom.Target, attack.Run.Bottom.Sound));
        Assert.IsTrue(attack.Run.Flags.HasFlag(AiRunFlags.Running), "fatigue above 8");
    }

    [TestMethod]
    public void TheAttackStarterRefusesKeepsOrRetargets()
    {
        var world = Arena(1);
        Assert.AreEqual(AiStartRefusal.Self, AiExecutorRules.StartAttack(world, Npc, Npc, null, null).Refusal);
        world.Throws = true;
        Assert.AreEqual(AiPerformKind.ThrowWeapon, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Kind);
        world.Throws = false;
        var running = AttackRun();
        var same = AiExecutorRules.StartAttack(world, Npc, Foe, null, running);
        Assert.AreEqual((AiStartRefusal.AlreadyQueued, running), (same.Refusal, same.Run));
        world.At["oid:other"] = new(9, 10);
        var other = AiExecutorRules.StartAttack(world, Npc, "oid:other", null, running);
        Assert.AreEqual(("oid:other", AiStartRefusal.None), (other.Run!.Bottom.Target, other.Refusal));
        world.InterruptFails = true;
        Assert.AreEqual(AiStartRefusal.Interrupt, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        world.InterruptFails = false;
        world.Boomerang = true;
        Assert.AreEqual(AiStartRefusal.BoomerangInFlight, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        world.Boomerang = false;
        world.Attack = new(BodyOfAir: true);
        Assert.AreEqual(AiStartRefusal.BodyOfAir, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
        CollectionAssert.AreEqual(new[] { "throw at pc", "step apart", "step apart", "step apart", "step apart" }, world.Calls);
        world.Attack = new();
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 2;
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartAttack(world, Npc, Foe, null, null).Refusal);
    }

    [TestMethod]
    public void FleePerformStartsTheFlightWithItsFlag()
    {
        var world = Arena(1);
        world.State = DangerState.Flee;
        var flee = AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null);
        Assert.AreEqual((AiPerformKind.Flee, AiGoalType.Flee, AiRunFlags.Flight), (flee.Kind, flee.Run!.Bottom.Goal, flee.Run.Flags));
        world.Fleeing = new(false, Foe);
        Assert.IsNull(AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Run);
        world.State = DangerState.Surrender;
        Assert.AreEqual(AiPerformKind.Surrender, AiExecutorRules.Perform(world, Npc, Foe, AiPassAction.None, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "perform fleeing", "perform fleeing", "perform surrender" }, world.Calls);
    }

    [TestMethod]
    public void NonCombatPerformFollowsTheLeaderOrRunsTheRoutine()
    {
        var world = Arena(1);
        world.State = DangerState.None;
        Assert.AreEqual(AiPerformKind.Routine, AiExecutorRules.Perform(world, Npc, null, AiPassAction.None, null, null).Kind);
        world.Leaders[Npc] = Leader;
        world.At[Leader] = new(20, 10);
        var follow = AiExecutorRules.Perform(world, Npc, null, AiPassAction.None, null, null);
        Assert.AreEqual((AiPerformKind.Follow, AiGoalType.Follow, AiRunFlags.None), (follow.Kind, follow.Run!.Bottom.Goal, follow.Run.Flags));
        Assert.AreEqual(AiStartRefusal.AlreadyQueued, AiExecutorRules.Perform(world, Npc, null, AiPassAction.None, null, follow.Run).Refusal);
        world.At[Leader] = new(14, 10);
        Assert.AreEqual(AiStartRefusal.LeaderNear, AiExecutorRules.Perform(world, Npc, null, AiPassAction.None, null, null).Refusal);
        world.Waits.Add(Npc);
        Assert.AreEqual(AiPerformKind.Waiting, AiExecutorRules.Perform(world, Npc, null, AiPassAction.None, null, null).Kind);
        CollectionAssert.AreEqual(new[] { "leave combat mode", "routine", "leave combat mode", "loyalty", "leave combat mode", "loyalty",
            "leave combat mode", "loyalty", "leave combat mode" }, world.Calls);
    }

    [TestMethod]
    public void APushBeyondEightGoalsEndsTheRun()
    {
        var world = Arena(4);
        var frames = Enumerable.Range(0, AiExecutorRules.MaxDepth).Select(_ => new AiGoalFrame(AiGoalType.Attack, Foe)).ToArray();
        var run = new AiGoalRun(Npc, frames[0]) with { Frames = [.. frames] };
        Assert.AreEqual(AiRunEnd.Overflow, AiExecutorRules.Step(run, world, Rolls, Steamwoven).End);
    }
}
