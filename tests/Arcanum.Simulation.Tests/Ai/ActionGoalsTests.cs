using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-089 (AI slice I8b): perform's cast (0x4AC250), the item-use (0x435450), skill (0x4350F0, 0x436220), throw (0x434F80) and
/// pickup (0x4356C0) starters, the item use (0x462CC0), and the casting (26, 27, 82, 83), item-use (39), skill (33, 34, 86, 67),
/// throw (37) and pickup (15, 16) goals with their action points and end steps, over an invented open grid whose planner walks
/// straight lines.
/// </summary>
[TestClass]
public sealed class ActionGoalsTests
{
    private const string Npc = "oid:mage", Foe = "pc", Kit = "oid:kit";
    private const int Run = 7;
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    private sealed class NoRolls : IAiRollSource
    {
        public int Roll(AiRollPurpose purpose, int min, int max) => min;
    }

    /// <summary>An open grid with a call log; every answer is a field the test sets.</summary>
    private sealed class World : IAiPerformWorld
    {
        public readonly Dictionary<string, CellCoord> At = [];
        public readonly Dictionary<string, AiMoverFacts> Movers = [];
        public readonly Dictionary<string, int> Points = [];
        public readonly HashSet<string> GoneSet = [];
        public readonly HashSet<CellCoord> Blocked = [];
        public readonly List<string> Calls = [];
        public bool Round, InterruptFails, PickupsOn = true, SkillArt = true, Faces = true, Releases = true, ActionFrame = true;
        public bool Throws = true, SkipsAnimation, ScriptRuns = true, Lockables;
        public string? Actor, Holder = Npc;
        public AiCastStart CastAnswer = new(Run);
        public AiCastGoalStart CastStart = AiCastGoalStart.Started;
        public AiCastSwing Swing = new(true, 400, 300);
        public AiActionSwing ActionSwing = new(AiActionStart.Started, 200, 100);
        public AiUsedItem Item = new();
        public AiGrenadeResult GrenadeAnswer = new(AiGrenadeStop.NoCheck);

        public CellCoord? Tile(string id) => At.TryGetValue(id, out var t) ? t : null;
        public bool Gone(string id) => GoneSet.Contains(id);
        public bool DeadCritter(string id) => false;
        public AiMoverFacts Mover(string id) => Movers.TryGetValue(id, out var m) ? m : new(Pc: id == Foe, Fatigue: 20, MaxFatigue: 20);
        public bool FirstStepClear(string id, string toward, bool ignoreTarget) => !Blocked.Contains(At[toward]) || ignoreTarget;
        public AiAttackFacts AttackFacts(string id, string target) => new();
        public AiReachFacts Reach(string id, string target) => new(AiExecutorRules.Distance(At[id], At[target]), 0, false, true);
        public int AttackCost(string id) => 3;

        public AiPath? PlanPath(string id, AiPathRequest request)
        {
            Calls.Add($"plan {request.Kind}{(request.Running ? " run" : "")}{(request.NearOccupied ? " near-occupied" : "")}");
            var to = request.Tile ?? At[request.Target!];
            var steps = new List<CellCoord>();
            for (var cell = At[id]; AiExecutorRules.Distance(cell, to) > request.Range && steps.Count < 40;)
            {
                cell = new(cell.X + Math.Sign(to.X - cell.X), cell.Y + Math.Sign(to.Y - cell.Y));
                steps.Add(cell);
            }
            return steps.Count == 0 ? null : new([.. steps]);
        }

        public AiStepBlock StepBlocked(string id, CellCoord next, bool flight) => Blocked.Contains(next) ? AiStepBlock.CannotStart : AiStepBlock.Clear;
        public int BeginStep(string id, CellCoord next, bool running) { Calls.Add($"step {next.X},{next.Y}{(running ? " run" : "")}"); At[id] = next; return 100; }
        public int GetUp(string id) { Calls.Add("get up"); Movers[id] = Mover(id) with { KnockedDown = false }; return 300; }
        // ARC-DEV-137: the get-up is the real goal 63 now; its start shows animation 8, which stands the fake critter up.
        public void ShowArt(string id, int animation, int frame) { if (animation == AiExecutorRules.GetUpAnimation) { Calls.Add("get up"); Movers[id] = Mover(id) with { KnockedDown = false }; } }
        public int Unconceal(string id) { Calls.Add("unconceal"); Movers[id] = Mover(id) with { Concealed = false }; return 200; }
        public void Face(string id, string toward) => Calls.Add($"face {toward}");
        public void PlaySound(string id, int sound) => Calls.Add($"sound {sound}");
        public AiAttackSwing BeginAttack(string id, string target) => new(400, 600);
        public void Hit(string id, string target) => Calls.Add($"hit {target}");
        public void SetNoFlee(string id) => Calls.Add("no-flee");

        public AiCastGoalStart StartCast(string id, int run) { Calls.Add($"start cast {run}"); return CastStart; }
        public bool FaceCast(string id, int run) { Calls.Add("face cast"); return Faces; }
        public AiCastSwing BeginCast(string id, int run, bool castAnimation) { Calls.Add(castAnimation ? "play cast animation" : "play cast"); return Swing; }
        public bool CastActionFrame(string id, int run) { Calls.Add("action frame"); return ActionFrame; }
        public bool ReleaseCast(string id, int run) { Calls.Add("release"); return Releases; }
        public void EndCast(string id, int run, bool refused) => Calls.Add(refused ? "end cast refused" : "end cast");
        public bool FaceForAction(string id, string? target, CellCoord? tile) { Calls.Add($"turn {target ?? $"{tile!.Value.X},{tile.Value.Y}"}"); return Faces; }
        public AiActionSwing BeginAction(string id, AiActionAnimation animation) { Calls.Add($"animate {animation}"); return ActionSwing; }
        public bool AttemptSkill(string id, string target, int skill, string? item) { Calls.Add($"skill {skill} on {target} with {item}"); return SkipsAnimation; }
        public void AttemptPickLocks(string id, string target, string? item) => Calls.Add($"pick locks {target}");
        public void AttemptSkillWithItem(string id, string target, string? item, int skill, int modifier) => Calls.Add($"skill {skill} modifier {modifier} on {target} with {item ?? "none"}");
        public bool ThrowItem(string id, string item, CellCoord tile) { Calls.Add($"throw {item} at {tile.X},{tile.Y}"); return Throws; }
        public void PickUp(string id, string item) => Calls.Add($"pick up {item}");
        public string? ItemHolder(string item) => Holder;
        public AiUsedItem UsedItem(string user, string item) => Item;
        public bool RunUseScript(string user, string item, string? target) { Calls.Add("use script"); return ScriptRuns; }
        public void OpenTargeting(string user, string item) => Calls.Add("targeting");
        public void UseTrapDevice(string user, string item, string? target) => Calls.Add("use trap device");
        public void ReadWritten(string user, string item) => Calls.Add("read");
        public bool Lockable(string target) => Lockables;
        public void StartSkillWithItem(string user, string item, string target, int skill) => Calls.Add($"skill {skill} with {item} on {target}");
        public void UseItemMagic(string user, string item, string? target) => Calls.Add($"magic {item}");
        public void SpendItem(string item) => Calls.Add($"spend {item}");

        public bool InRound => Round;
        public string? RoundActor => Actor;
        public int ActionPoints(string id) => Points.GetValueOrDefault(id);
        public int PendingCost { get; set; }
        public void Consume(string id, int cost) { Calls.Add($"ap -{cost}"); Points[id] = Math.Max(Points.GetValueOrDefault(id) - cost, 0); }
        public void EndSubturn(string id) => Calls.Add("end subturn");

        public bool Active(string id) => true;
        public DangerState ReadDanger(string npc) => DangerState.CombatFocus;
        public DangerFlags DangerBits(string npc) => DangerFlags.None;
        public BackOffRun BackOff(string npc, string? target) => new(BackOffRunStep.NotBackingOff, 0);
        public CellCoord? AwayTile(string npc, string? target, int range) => null;
        public void EndBackOff(string npc) { }
        public AiCastStart Cast(string npc, AiPassChoice choice) { Calls.Add($"cast {choice.Spell} with {choice.Item}"); return CastAnswer; }
        public void LeaveCombatMode(string npc) { }
        public string? Leader(string npc) => null;
        public bool Waiting(string npc) => false;
        public void LoyaltyCheck(string npc, bool followStarted) { }
        public void RunRoutine(string npc) { }
        public bool StartBackOff(string npc, string? target) => false;
        public AiGrenadeResult Grenade(string npc, string target) { Calls.Add("grenade?"); return GrenadeAnswer; }
        public string? ThrowableWeapon(string npc) => null;
        public void Unwield(string npc, string item) { }
        public bool HasSkillArt(string npc) => SkillArt;
        public bool NpcPickupsOn => PickupsOn;
        public bool BoomerangInFlight(string npc) => false;
        public bool InterruptLowerAnimations(string id) => !InterruptFails;
        public AiFleeing PerformFleeing(string npc) => new(false, null);
        public void PerformSurrender(string npc) { }
    }

    private static World Arena(int distance)
    {
        var world = new World();
        world.At[Npc] = new(10, 10);
        world.At[Foe] = new(10 + distance, 10);
        return world;
    }

    private static void InRound(World world, int points)
    {
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = points;
    }

    /// <summary>Steps a run until it ends (at most 60 steps).</summary>
    private static List<AiGoalStepResult> Drive(GoalRun run, World world, CombatAnomalyProfile profile)
    {
        var steps = new List<AiGoalStepResult>();
        for (var i = 0; i < 60; i++)
        {
            var step = AiExecutorRules.Step(run, world, new NoRolls(), profile, steps.Count > 0 ? steps[^1].Reason : AiExecutorReason.None);
            steps.Add(step);
            if (step.Ended) return steps;
            run = step.Run!;
        }
        Assert.Fail("The run did not end.");
        return steps;
    }

    private static AiPassChoice Choice(AiPassAction action, int spell = 11, int skill = -1, string? item = null) => new(action, spell, skill, item, Foe);

    // ---- casting ----

    [TestMethod]
    public void PerformsCastStartsTheCastingGoalOfTheMagicRun()
    {
        var world = Arena(3);
        var perform = AiExecutorRules.Perform(world, Npc, Foe, Choice(AiPassAction.Cast, item: "oid:wand"), null, null);
        Assert.AreEqual((AiPerformKind.Cast, true, GoalType.CastSpell, Foe, (int?)Run),
            (perform.Kind, perform.Started, perform.Run!.Bottom.Goal, perform.Run.Bottom.Target, perform.Run.Bottom.SpellRun));
        CollectionAssert.AreEqual(new[] { "cast 11 with oid:wand" }, world.Calls);

        world.CastAnswer = new(Run, CastAnimation: true);
        Assert.AreEqual(GoalType.CastSpellWithAnimation, AiExecutorRules.Perform(world, Npc, Foe, Choice(AiPassAction.Cast), null, null).Run!.Bottom.Goal);
        var kept = new GoalRun(Npc, default, new GoalFrame(GoalType.Attack, Foe));
        world.CastAnswer = new(null);
        var refused = AiExecutorRules.Perform(world, Npc, Foe, Choice(AiPassAction.Cast), null, kept);
        Assert.AreEqual((AiStartRefusal.NoRun, kept, false), (refused.Refusal, refused.Run, refused.Started));
        world.CastAnswer = new(null, Queued: true);
        Assert.AreSame(kept, AiExecutorRules.Perform(world, Npc, Foe, Choice(AiPassAction.Cast), null, kept).Run, "a queued cast is the host's");
    }

    [TestMethod]
    public void ACastStartsPlaysReleasesAndEndsItsRunOnThePop()
    {
        var world = Arena(3);
        var steps = Drive(CastRun(), world, Original);
        CollectionAssert.AreEqual(new[] { "start cast 7", "face cast", "play cast", "action frame", "end cast" }, world.Calls);
        Assert.AreEqual((400, 300), (steps[0].DelayMs, steps[1].DelayMs));
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.CastReleased), (steps[^1].End, steps[^1].Reason));

        world = Arena(3);
        Drive(CastRun(GoalType.CastSpellWithAnimation), world, Steamwoven);
        CollectionAssert.Contains(world.Calls, "play cast animation");
    }

    [TestMethod]
    public void ACastGetsUpAndUnconcealsFirst()
    {
        var world = Arena(3);
        world.Movers[Npc] = new(KnockedDown: true, Concealed: true, Fatigue: 20, MaxFatigue: 20);
        Drive(CastRun(), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "get up", "unconceal", "start cast 7" }, world.Calls.Take(3).ToArray());
    }

    [TestMethod]
    public void ARefusedOrUnturnableCastEndsItsRunAsRefused()
    {
        foreach (var (start, faces) in new[] { (AiCastGoalStart.Refused, true), (AiCastGoalStart.TargetGone, true), (AiCastGoalStart.Started, false) })
        {
            var world = Arena(3);
            world.CastStart = start;
            world.Faces = faces;
            var step = AiExecutorRules.Step(CastRun(), world, new NoRolls(), Steamwoven);
            Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.CastRefused), (step.End, step.Reason));
            Assert.AreEqual(1, world.Calls.Count(c => c.StartsWith("end cast", StringComparison.Ordinal)));
            Assert.AreEqual("end cast refused", world.Calls[^1]);
        }
    }

    [TestMethod]
    public void ACastWithoutAPlayIsReleasedAtOnceAndEndsWithTheEndStep()
    {
        var world = Arena(3);
        InRound(world, 6);
        world.PendingCost = 2;
        world.Swing = new(false);
        var steps = Drive(CastRun(), world, Original);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.CastReleased), (steps[^1].End, steps[^1].Reason));
        CollectionAssert.AreEqual(new[] { "start cast 7", "face cast", "play cast", "release", "ap -2", "end cast" }, world.Calls,
            "EX6 charges the pending cost again, then the attempt goal's end node runs");

        world = Arena(3);
        world.Swing = new(false);
        world.Releases = false;
        Assert.AreEqual(AiExecutorReason.CastNotReleased, Drive(CastRun(), world, Steamwoven)[^1].Reason);
    }

    [TestMethod]
    public void AnUnreleasedActionFramePopsAtOnce()
    {
        var world = Arena(3);
        world.ActionFrame = false;
        var steps = Drive(CastRun(), world, Steamwoven);
        Assert.AreEqual((2, AiRunEnd.Finished, AiExecutorReason.CastNotReleased), (steps.Count, steps[^1].End, steps[^1].Reason));
        Assert.AreEqual("end cast", world.Calls[^1]);
    }

    [TestMethod]
    public void AQueuedCastingGoalAtTheBottomPlaysOnceTheGoalsAboveItHavePopped()
    {
        // ARC-WORLD-104 (SU13, QC3): the host inserts a queued goal 26 beneath the playing one; the attempt goal's pop restarts it.
        var world = Arena(3);
        var playing = AiExecutorRules.Step(CastRun(), world, new NoRolls(), Steamwoven).Run!;
        var queued = playing.WithFrames(playing.Frames.Insert(0, new GoalFrame(GoalType.CastSpell, Foe) { SpellRun = 8 }));
        Assert.AreEqual(GoalType.AttemptSpell, queued.Top.Goal, "the playing goal stays current");
        var steps = Drive(queued, world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "start cast 7", "face cast", "play cast", "action frame", "end cast", "start cast 8", "face cast",
            "play cast", "action frame", "end cast" }, world.Calls);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);

        // A queued goal refused at its start ends through the end step (QC4).
        world = Arena(3);
        playing = AiExecutorRules.Step(CastRun(), world, new NoRolls(), Steamwoven).Run!;
        world.CastStart = AiCastGoalStart.Refused;
        queued = playing.WithFrames(playing.Frames.Insert(0, new GoalFrame(GoalType.CastSpell, Foe) { SpellRun = 8 }));
        var refused = Drive(queued, world, Steamwoven)[^1];
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.CastRefused), (refused.End, refused.Reason));
        CollectionAssert.AreEqual(new[] { "play cast", "action frame", "end cast", "start cast 8", "end cast refused" }, world.Calls.Skip(2).ToArray(),
            "the playing goal played and ended before the queued goal's refused start");
    }

    [TestMethod]
    public void EndRunEndsTheMagicRunOfAReplacedCastOnce()
    {
        var world = Arena(3);
        var playing = AiExecutorRules.Step(CastRun(), world, new NoRolls(), Steamwoven).Run!;
        Assert.AreEqual(GoalType.AttemptSpell, playing.Top.Goal);
        var attack = AiExecutorRules.StartAttack(world, Npc, Foe, null, playing);
        Assert.IsTrue(attack.Started);
        AiExecutorRules.EndRun(playing, world);
        Assert.AreEqual(1, world.Calls.Count(c => c == "end cast"));

        world = Arena(3);
        AiExecutorRules.EndRun(CastRun(), world);
        Assert.AreEqual(0, world.Calls.Count, "goal 26 has no magic end node");
    }

    [TestMethod]
    public void PerformTakesTheActionsTargetFromTheChoice()
    {
        var world = Arena(3);
        var self = AiExecutorRules.Perform(world, Npc, Foe, new AiPassChoice(AiPassAction.UseItem, -1, -1, Kit, Npc), null, null);
        Assert.AreEqual(Npc, self.Run!.Bottom.Target);
        var fallback = AiExecutorRules.Perform(world, Npc, Foe, new AiPassChoice(AiPassAction.UseItem, -1, -1, Kit, null), null, null);
        Assert.AreEqual(Foe, fallback.Run!.Bottom.Target);
    }

    [TestMethod]
    public void ASpreadOutRunWalksNearRunningAfterARunNear()
    {
        var world = Arena(6);
        world.Movers[Npc] = new(SpreadOut: true, Fatigue: 20, MaxFatigue: 20);
        AiExecutorRules.Step(new GoalRun(Npc, default, new GoalFrame(GoalType.SpreadOut, Foe), GoalRunFlags.Running), world, new NoRolls(), Steamwoven);
        Assert.AreEqual("plan Near run", world.Calls[0], "AG16: the walk near keeps the run's gait");
    }

    private static GoalRun CastRun(GoalType goal = GoalType.CastSpell) => new(Npc, default, new GoalFrame(goal, Foe) { SpellRun = Run });

    // ---- the item use ----

    [TestMethod]
    public void TheItemUseStarterNeedsBothObjectsAndPassesTheGateWithFour()
    {
        var world = Arena(3);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartUseItem(world, Npc, null, Kit, null).Refusal);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartUseItem(world, Npc, Foe, null, null).Refusal);
        var started = AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null);
        Assert.AreEqual((GoalType.UseItem, Foe, Kit, GoalRunFlags.NearOccupied | GoalRunFlags.Running),
            (started.Run!.Bottom.Goal, started.Run.Bottom.Target, started.Run.Bottom.Item, started.Run.Flags));
        world.Movers[Npc] = new(Fatigue: 8, MaxFatigue: 20);
        Assert.AreEqual(GoalRunFlags.NearOccupied, AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null).Run!.Flags, "running only above 8");
        InRound(world, 3);
        Assert.IsTrue(AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null).Started, "fatigue 8 of 20 is above 35 %");
        world.Movers[Npc] = new(Fatigue: 7, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null).Refusal, "4 above 3 points, fatigue 7 of 20");
    }

    // ---- the use of an item with a skill (ARC-SCRIPT-2752, ledger SX3, SX4) ----

    [TestMethod]
    public void TheUseItemWithSkillStarterNeedsATargetPassesTheGateWithFourAndKeepsTheModifier()
    {
        var world = Arena(3);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartUseItemWithSkill(world, Npc, null, Kit, 14, 5, null).Refusal, "nothing is queued for a null target");
        var started = AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 5, null);
        Assert.AreEqual((AiPerformKind.UseItemWithSkill, GoalType.UseItemWithSkill, Foe, Kit, 14, 5, GoalRunFlags.NearOccupied | GoalRunFlags.Running),
            (started.Kind, started.Run!.Bottom.Goal, started.Run.Bottom.Target, started.Run.Bottom.Item, started.Run.Bottom.Skill, started.Run.Bottom.Modifier, started.Run.Flags));
        Assert.IsTrue(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, null, 14, -10, null).Started, "a null item is allowed");
        world.Movers[Npc] = new(Fatigue: 8, MaxFatigue: 20);
        Assert.AreEqual(GoalRunFlags.NearOccupied, AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 0, null).Run!.Flags, "running only above 8");
        InRound(world, 3);
        world.Movers[Npc] = new(Fatigue: 7, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 0, null).Refusal, "4 above 3 points, fatigue 7 of 20");
        InRound(world, 4);
        Assert.IsTrue(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 0, null).Started);
        Assert.IsFalse(world.Calls.Any(c => c.StartsWith("ap ", StringComparison.Ordinal)), "the starter charges no action points");
    }

    [TestMethod]
    public void TheUseItemWithSkillGoalWalksNearThenRunsTheInvocationOnceAndEnds()
    {
        var world = Arena(4);
        var run = AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 5, null).Run!;
        var steps = Drive(run, world, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.SkillAttempted), (steps[^1].End, steps[^1].Reason));
        CollectionAssert.AreEqual(new[] { "plan Near run near-occupied", "step 11,10 run", "step 12,10 run", "step 13,10 run", $"skill 14 modifier 5 on {Foe} with {Kit}" }, world.Calls,
            "the walk keeps the goal's frame, so the modifier survives it, and the invocation is one call");

        world = Arena(1);
        Assert.AreEqual(AiExecutorReason.SkillAttempted, Drive(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, null, 14, -7, null).Run!, world, Steamwoven)[^1].Reason);
        CollectionAssert.Contains(world.Calls, $"skill 14 modifier -7 on {Foe} with none");
    }

    [TestMethod]
    public void TheUseItemWithSkillGoalRefusesAGoneTargetAnItemGoneOrNotHeldAndRunsNoInvocation()
    {
        var world = Arena(1);
        world.GoneSet.Add(Foe);
        Assert.AreEqual(AiExecutorReason.ObjectGone, Drive(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 5, null).Run!, world, Steamwoven)[^1].Reason);
        world = Arena(1);
        world.GoneSet.Add(Kit);
        Assert.AreEqual(AiExecutorReason.ItemNotHeld, Drive(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 5, null).Run!, world, Steamwoven)[^1].Reason);
        world = Arena(1);
        world.Holder = "oid:chest";
        Assert.AreEqual(AiExecutorReason.ItemNotHeld, Drive(AiExecutorRules.StartUseItemWithSkill(world, Npc, Foe, Kit, 14, 5, null).Run!, world, Steamwoven)[^1].Reason, "an item the user does not hold directly");
        Assert.IsFalse(world.Calls.Any(c => c.StartsWith("skill ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TheItemUseGoalWalksNearRunningThenUsesTheItemAndEnds()
    {
        var world = Arena(4);
        world.Item = new(AiItemKind.Food, Magical: true);
        var run = AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null).Run!;
        var steps = Drive(run, world, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.ItemUsed), (steps[^1].End, steps[^1].Reason));
        CollectionAssert.AreEqual(new[] { "plan Near run near-occupied", "step 11,10 run", "step 12,10 run", "step 13,10 run", "use script", "magic oid:kit", "spend oid:kit" },
            world.Calls);

        world = Arena(1);
        world.Holder = "oid:chest";
        Assert.AreEqual(AiExecutorReason.ItemNotHeld, Drive(AiExecutorRules.StartUseItem(world, Npc, Foe, Kit, null).Run!, world, Steamwoven)[^1].Reason);
        CollectionAssert.DoesNotContain(world.Calls, "use script");
    }

    [TestMethod]
    public void TheItemUseChargesFourOnlyWhenTheScriptStopsItOrForAPlainItem()
    {
        AiItemUseResult Use(AiUsedItem item, bool script = true, string? target = Foe, bool lockable = false, bool round = true)
        {
            var world = Arena(1);
            if (round) InRound(world, 9);
            world.Item = item;
            world.ScriptRuns = script;
            world.Lockables = lockable;
            return AiExecutorRules.UseItemNow(world, Npc, Kit, target);
        }

        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.ScrollTooHard, false, false), Use(new(AiItemKind.Scroll, ScrollTooHard: true)));
        Assert.AreEqual(AiItemUseStep.Spent, Use(new(AiItemKind.Food, ScrollTooHard: true)).Step, "only a scroll's intelligence is tested");
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Targeting, false, false), Use(new(NeedsTarget: true), target: null));
        Assert.AreEqual(AiItemUseStep.Nothing, Use(new(NeedsTarget: true)).Step, "a target skips the location use");
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.TrapDevice, false, false), Use(new(TrapDevice: true, Magical: true)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.ScriptStopped, true, false), Use(new(Magical: true), script: false));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Magic, false, true), Use(new(AiItemKind.Scroll, Magical: true)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Magic, false, false), Use(new(Charges: 3)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Read, true, false), Use(new(AiItemKind.Written)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Lockpick, true, false), Use(new(AiItemKind.Generic, Lockpick: true, HealingKit: true), lockable: true));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.NotLockable, true, false), Use(new(AiItemKind.Generic, Lockpick: true)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.HealingKit, true, false), Use(new(AiItemKind.Generic, HealingKit: true)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Spent, true, true), Use(new(AiItemKind.Food)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Nothing, true, false), Use(new(AiItemKind.Generic)));
        Assert.AreEqual(new AiItemUseResult(AiItemUseStep.Nothing, false, false), Use(new(), round: false), "outside a round nothing is charged");

        var kit = Arena(1);
        InRound(kit, 9);
        kit.Item = new(AiItemKind.Generic, HealingKit: true);
        AiExecutorRules.UseItemNow(kit, Npc, Kit, Foe);
        CollectionAssert.AreEqual(new[] { "use script", "ap -4", $"skill 10 with {Kit} on {Foe}" }, kit.Calls);
    }

    // ---- skills ----

    [TestMethod]
    public void TheSkillStarterSendsPickLocksToItsOwnStarter()
    {
        var world = Arena(3);
        var heal = AiExecutorRules.StartUseSkill(world, Npc, Foe, AiPassChoice.HealSkill, Kit, null);
        Assert.AreEqual((GoalType.UseSkill, (int?)10, Kit, GoalRunFlags.NearOccupied | GoalRunFlags.Running),
            (heal.Run!.Bottom.Goal, heal.Run.Bottom.Skill, heal.Run.Bottom.Item, heal.Run.Flags));
        var locks = AiExecutorRules.StartUseSkill(world, Npc, "oid:door", AiExecutorRules.PickLocksSkill, null, null);
        Assert.AreEqual(((int?)14, GoalRunFlags.NearOccupied), (locks.Run!.Bottom.Skill, locks.Run.Flags), "pick locks never runs");
        world.SkillArt = false;
        Assert.AreEqual(AiStartRefusal.NoArt, AiExecutorRules.StartUseSkill(world, Npc, "oid:door", 14, null, null).Refusal);
        Assert.IsNotNull(AiExecutorRules.StartUseSkill(world, Npc, Foe, 10, null, null).Run, "only pick locks asks the art");
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartUseSkill(world, Npc, null, 10, null, null).Refusal);
        InRound(world, 3);
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartUseSkill(world, Npc, Foe, 10, null, null).Refusal);
    }

    // ARC-SCRIPT-1789 (ledger HLR10, SK8): the dialog service's flag 0x2000 reaches the starter and no goal it queues reads it.
    [TestMethod]
    public void TheNoPcLeaderRequestFlagReachesTheSkillStarterAndChangesNoRun()
    {
        var world = Arena(3);
        foreach (var skill in new[] { AiPassChoice.HealSkill, AiExecutorRules.PickpocketSkill, 3, AiExecutorRules.PickLocksSkill })
        {
            var plain = AiExecutorRules.StartUseSkill(world, Npc, Foe, skill, Kit, null);
            var flagged = AiExecutorRules.StartUseSkill(world, Npc, Foe, skill, Kit, null, AiExecutorRules.NoPcLeaderRequestFlag);
            Assert.IsTrue(flagged.Started, $"skill {skill}");
            Assert.AreEqual(plain.Run!.Bottom, flagged.Run!.Bottom, $"skill {skill}: the same goal frame");
            Assert.AreEqual(plain.Run.Flags, flagged.Run.Flags, $"skill {skill}: the same run flags");
        }
    }

    [TestMethod]
    public void TheHealSkillWalksToThePatientAndAttemptsAtTheActionFrame()
    {
        var world = Arena(3);
        InRound(world, 20);
        var run = AiExecutorRules.StartUseSkill(world, Npc, Foe, AiPassChoice.HealSkill, Kit, null).Run!;
        var steps = Drive(run, world, Original);
        CollectionAssert.AreEqual(new[] { "plan Near run near-occupied", "ap -1", "step 11,10 run", "ap -1", "step 12,10 run", $"turn {Foe}", "animate Skill", "ap -4", $"skill 10 on {Foe} with {Kit}" },
            world.Calls);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.SkillAttempted), (steps[^1].End, steps[^1].Reason), "the attempt goal pops: no end step");
        Assert.AreEqual(14, world.Points[Npc]);
    }

    [TestMethod]
    public void ABlockedStepEndsTheSkillApproach()
    {
        var world = Arena(3);
        world.Blocked.Add(new(12, 10));
        var steps = Drive(AiExecutorRules.StartUseSkill(world, Npc, Foe, 10, null, null).Run!, world, Steamwoven);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.StepBlocked), (steps[^1].End, steps[^1].Reason), "the plain walk (EX20)");
    }

    [TestMethod]
    public void ASkillAttemptEndsOnARefusedTurnArtStartTargetOrPayment()
    {
        AiGoalStepResult Last(Action<World> set, int points = 20)
        {
            var world = Arena(1);
            InRound(world, points);
            set(world);
            var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.AttemptSkill, Foe) { Skill = 10 }), world, Steamwoven);
            return steps[^1];
        }

        Assert.AreEqual(AiRunEnd.EndStep, Last(w => w.Faces = false).End);
        Assert.AreEqual(AiRunEnd.EndStep, Last(w => w.ActionSwing = new(AiActionStart.NoArt)).End);
        var world = Arena(1);
        InRound(world, 20);
        world.ActionSwing = new(AiActionStart.CannotStart);
        AiExecutorRules.Step(new GoalRun(Npc, default, new GoalFrame(GoalType.AttemptSkill, Foe) { Skill = 10 }), world, new NoRolls(), Steamwoven);
        CollectionAssert.Contains(world.Calls, "ap -2", "an animation that cannot start charges 2");
        Assert.AreEqual(AiRunEnd.EndStep, Last(w => w.GoneSet.Add(Foe)).End);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.ActionRefused), (Last(_ => { }, points: 3).End, Last(_ => { }, points: 3).Reason),
            "the consume refuses 4 of 3 points");
    }

    [TestMethod]
    public void APickpocketAttemptsBeforeItsAnimationAndMaySkipIt()
    {
        var world = Arena(1);
        world.SkipsAnimation = true;
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.UseSkill, Foe) { Skill = AiExecutorRules.PickpocketSkill }), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { $"turn {Foe}", $"skill 5 on {Foe} with " }, world.Calls);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);

        world = Arena(1);
        steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.AttemptPickpocket, Foe) { Skill = 5 }), world, Steamwoven);
        CollectionAssert.AreEqual(new[] { $"turn {Foe}", $"skill 5 on {Foe} with ", "animate Skill" }, world.Calls);
        Assert.AreEqual((300, AiRunEnd.Finished), (steps[0].DelayMs, steps[^1].End));
    }

    [TestMethod]
    public void PickLocksAttemptsAfterItsLoopsWithoutActionPoints()
    {
        var world = Arena(1);
        InRound(world, 9);
        var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.UseSkill, "pc") { Skill = 14 }), world, Original);
        CollectionAssert.AreEqual(new[] { $"turn {Foe}", "animate PickLocks", $"pick locks {Foe}" }, world.Calls);
        Assert.AreEqual((AiRunEnd.Finished, 9), (steps[^1].End, world.Points[Npc]));

        world = Arena(1);
        world.ActionSwing = new(AiActionStart.CannotStart);
        Assert.AreEqual(AiRunEnd.Finished, Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.AttemptPickLocks, Foe)), world, Steamwoven)[^1].End,
            "no loop left: the goal pops");
        CollectionAssert.DoesNotContain(world.Calls, $"pick locks {Foe}", "no attempt without the loops");
    }

    // ---- the throw ----

    [TestMethod]
    public void TheThrowStarterGatesWithZeroAndRefusesAnEqualThrow()
    {
        var world = Arena(3);
        CellCoord tile = new(13, 10);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartThrow(world, Npc, null, tile, null).Refusal);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartThrow(world, Npc, "oid:grenade", null, null).Refusal);
        var started = AiExecutorRules.StartThrow(world, Npc, "oid:grenade", tile, null);
        Assert.AreEqual((GoalType.ThrowItem, (CellCoord?)tile, GoalRunFlags.None), (started.Run!.Bottom.Goal, started.Run.Bottom.Tile, started.Run.Flags));
        Assert.AreEqual(AiStartRefusal.AlreadyQueued, AiExecutorRules.StartThrow(world, Npc, "oid:grenade", tile, started.Run).Refusal);
        Assert.IsTrue(AiExecutorRules.StartThrow(world, Npc, "oid:grenade", new(12, 10), started.Run).Started, "another tile");
        world.InterruptFails = true;
        Assert.AreEqual(AiStartRefusal.Interrupt, AiExecutorRules.StartThrow(world, Npc, "oid:grenade", tile, null).Refusal);
        world.InterruptFails = false;
        InRound(world, 0);
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        Assert.IsTrue(AiExecutorRules.StartThrow(world, Npc, "oid:grenade", tile, null).Started, "cost 0 fits no points");
    }

    [TestMethod]
    public void AThrowEndsWithTwoActionPointsAndTheEndStepOnEveryPath()
    {
        foreach (var (set, thrown) in new (Action<World>, bool)[]
                 {
                     (_ => { }, true), (w => w.Faces = false, false), (w => w.ActionSwing = new(AiActionStart.NoArt), false),
                     (w => w.ActionSwing = new(AiActionStart.CannotStart), false), (w => w.Throws = false, false),
                 })
        {
            var world = Arena(3);
            InRound(world, 9);
            world.PendingCost = 1;
            set(world);
            var steps = Drive(new GoalRun(Npc, default, new GoalFrame(GoalType.ThrowItem, tile: new CellCoord(13, 10)) { Item = "oid:grenade" }), world, Original);
            Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
            Assert.AreEqual(thrown, world.Calls.Contains("throw oid:grenade at 13,10") && steps[^1].Reason == AiExecutorReason.Thrown);
            CollectionAssert.AreEqual(new[] { "ap -2", "ap -1" }, world.Calls.Where(c => c.StartsWith("ap", StringComparison.Ordinal)).ToArray(),
                "2 at the end, then EX6's pending cost");
        }
    }

    [TestMethod]
    public void CombatPerformsGrenadeRunsTheThrowStarterAndGoesNoFurther()
    {
        var world = Arena(4);
        world.GrenadeAnswer = new(AiGrenadeStop.Throw, "oid:grenade");
        var perform = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.Grenade, (CellCoord?)world.At[Foe], true), (perform.Kind, perform.Run!.Bottom.Tile, perform.Started));
        world.InterruptFails = true;
        var refused = AiExecutorRules.Perform(world, Npc, Foe, null, null, null);
        Assert.AreEqual((AiPerformKind.Grenade, AiStartRefusal.Interrupt, (GoalRun?)null), (refused.Kind, refused.Refusal, refused.Run),
            "a refused throw still ends combat perform (0x4AC910 answers 1)");
        world.GrenadeAnswer = new(AiGrenadeStop.TooClose, "oid:grenade");
        Assert.AreEqual(AiPerformKind.Attack, AiExecutorRules.Perform(world, Npc, Foe, null, null, null).Kind);
    }

    // ---- the pickup ----

    [TestMethod]
    public void ThePickupStarterGatesWithFourAndHonoursTheSwitch()
    {
        var world = Arena(3);
        Assert.AreEqual(AiStartRefusal.NoObject, AiExecutorRules.StartPickup(world, Npc, null, null).Refusal);
        var started = AiExecutorRules.StartPickup(world, Npc, "oid:arrows", null);
        Assert.AreEqual((GoalType.PickupItem, "oid:arrows", GoalRunFlags.NearOccupied), (started.Run!.Bottom.Goal, started.Run.Bottom.Target, started.Run.Flags));
        world.PickupsOn = false;
        Assert.AreEqual(AiStartRefusal.PickupsOff, AiExecutorRules.StartPickup(world, Npc, "oid:arrows", null).Refusal);
        world.PickupsOn = true;
        InRound(world, 3);
        world.Movers[Npc] = new(Fatigue: 1, MaxFatigue: 20);
        Assert.AreEqual(AiStartRefusal.Gate, AiExecutorRules.StartPickup(world, Npc, "oid:arrows", null).Refusal);
    }

    [TestMethod]
    public void ThePickupWalksOntoTheItemAndEndsWithTheEndStep()
    {
        var world = Arena(3);
        world.At["oid:arrows"] = new(12, 12);
        var steps = Drive(AiExecutorRules.StartPickup(world, Npc, "oid:arrows", null).Run!, world, Steamwoven);
        CollectionAssert.AreEqual(new[] { "plan Near near-occupied", "step 11,11", "step 12,12", "pick up oid:arrows" }, world.Calls);
        Assert.AreEqual((AiRunEnd.EndStep, AiExecutorReason.PickedUp), (steps[^1].End, steps[^1].Reason));

        world = Arena(3);
        world.At["oid:arrows"] = world.At[Npc];
        world.GoneSet.Add("oid:arrows");
        Assert.AreEqual(AiRunEnd.EndStep, Drive(AiExecutorRules.StartPickup(world, Npc, "oid:arrows", null).Run!, world, Steamwoven)[^1].End);
        Assert.AreEqual(0, world.Calls.Count, "an item destroyed or off is not taken");
    }
}
