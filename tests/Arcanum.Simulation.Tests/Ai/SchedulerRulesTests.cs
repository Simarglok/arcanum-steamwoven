using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-084 (AI slice I6): the tick's cadence (0x4AD5D0, 0x4AD610), the near test (0x4AD420), the catch-up (0x4AD4D0), the
/// tick's paths (0x4AD200), the pass (0x4A84F0), the gate (0x4A8570) with CL1, CL2 and CL5, and the subturn hook (0x4B7010), over
/// an invented world that logs every call in order.
/// </summary>
[TestClass]
public sealed class SchedulerRulesTests
{
    private const string Npc = "oid:wolf", Leader = "oid:sogg";
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;

    private sealed class Rolls(params int[] values) : IAiRollSource
    {
        private readonly Queue<int> _values = new(values);
        public readonly List<(AiRollPurpose Purpose, int Min, int Max)> Asked = [];

        public int Roll(AiRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return _values.Count > 0 ? _values.Dequeue() : max;
        }
    }

    /// <summary>One NPC, its leader and the PC; scripts answer from <see cref="Answers"/> and may run an action.</summary>
    private sealed class World : IAiSchedulerWorld
    {
        public readonly List<string> Calls = [];
        public readonly HashSet<string> Dead = [], InCombat = [];
        public readonly Dictionary<AiScriptPoint, bool> Answers = new() { [AiScriptPoint.FirstHeartbeat] = true, [AiScriptPoint.Heartbeat] = true };
        public Action<AiScriptPoint>? OnScript;
        public long Distance = 3;
        public string? LeaderId, Actor;
        public bool Round, Generator, Encounter, Loaded = true, Waypoints = true, Clearing, Asleep, Hide, PcOff, Stoned, Unconscious;
        public bool Callback, StunAnimation, Animating, Healing, Searching, IsLoading;
        public long? Spawned;
        public AiCatchUpFacts Facts = new(true, 40, false, false, true);
        public AiConversation Partner;
        public int Priority;
        public DangerState State = DangerState.CombatFocus;
        public string? Source = AiSchedulerRules.Pc;
        public DangerFlags Bits;
        public AiCritterFlags Flags;

        private T Log<T>(string call, T value) { Calls.Add(call); return value; }
        private void Log(string call) => Calls.Add(call);

        public bool IsDead(string id) => Dead.Contains(id);
        public bool IsUnconscious(string npc) => Unconscious;
        public bool IsStoned(string npc) => Stoned;
        public bool InRound => Round;
        public string? RoundActor => Actor;
        public bool InCombatMode(string id) => InCombat.Contains(id);
        public (DangerState State, string? Source) Danger(string npc) => (State, Source);
        public DangerFlags DangerBits(string npc) => Bits;
        public void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear) { Log($"bits +{set} -{clear}"); Bits = (Bits | set) & ~clear; }
        public AiCritterFlags CritterFlags(string npc) => Flags;
        public void WriteCritterFlags(string npc, AiCritterFlags flags) { Log($"flags {flags.Conditions} {flags.Danger}"); Flags = flags; }
        public void EndSubturn(string npc) => Log("end subturn");
        public long DistanceToPc(string npc) => Distance;

        public bool RunScript(AiScriptPoint point, string npc, string? triggerer)
        {
            Log($"script {(int)point} {triggerer ?? "-"}");
            OnScript?.Invoke(point);
            return Answers.GetValueOrDefault(point);
        }

        public bool RunWaypoints(string npc) => Log("waypoints", Waypoints);
        public void RunStandpoints(string npc) => Log("standpoints");
        public bool IsGenerator(string npc) => Generator;
        public long? Spawn(string npc) => Log("spawn", Spawned);
        public bool IsEncounterCreature(string npc) => Encounter;
        public void Destroy(string npc) => Log("destroy");
        public void InterruptAnimations(string npc) => Log("interrupt");
        public bool SectorLoaded(string npc) => Loaded;
        public AiCatchUpFacts CatchUp(string npc) => Log("catch-up facts", Facts);
        public void MoveToLeader(string npc) => Log("move to leader");
        public bool MapClearing => Clearing;
        public bool Sleeping(string npc) => Asleep;
        public bool Hidden(string npc) => Hide;
        public bool PcHidden => PcOff;
        public AiConversation Conversation(string npc) => Partner;
        public bool ConversationCallback => Callback;
        public void EndConversation(string npc) => Log("end conversation");
        public int AnimationPriority(string npc) => Priority;
        public bool StunAnimationRunning(string npc) => StunAnimation;
        public bool AnimationRunning(string npc) => Animating;
        public void ReWield(string npc, bool allSlots) => Log(allSlots ? "re-wield all" : "re-wield weapon");
        public string? Leader(string npc) => LeaderId;
        public void EndMaintainedSpells(string npc) => Log("demaintain");
        public bool Heal(string npc) => Log("heal", Healing);
        public bool SearchItems(string npc) => Log("item search", Searching);
        public void UpdateDanger(string npc) => Log("danger update");
        public void Perform(string npc) => Log("perform");
        public bool Loading => IsLoading;
    }

    private static readonly string[] PassCalls = ["heal", "item search", "danger update", "perform"];

    [TestMethod]
    public void TheTickDelayGrowsWithTheDistanceToThirtyTiles()
    {
        var expected = new (long Distance, long Delay)[] { (0, 250), (1, 408), (5, 1041), (10, 1833), (20, 3416), (29, 4841), (30, 5000), (31, 5000), (-1, 5000), (long.MaxValue, 5000) };
        foreach (var (distance, delay) in expected) Assert.AreEqual(delay, AiSchedulerRules.TickDelayMs(distance), $"distance {distance}");
        Assert.AreEqual((30, 30, 7), (AiSchedulerRules.TickDistance(-5), AiSchedulerRules.TickDistance(100), AiSchedulerRules.TickDistance(7)));

        // A wake-up adds 0..5000 ms; the tick it schedules carries no marker.
        var rolls = new Rolls(1234);
        Assert.AreEqual(1041 + 1234, AiSchedulerRules.NextTickDelayMs(5, true, rolls));
        CollectionAssert.AreEqual(new[] { (AiRollPurpose.TickWakeUpJitter, 0, 5000) }, rolls.Asked);
        Assert.AreEqual(1041, AiSchedulerRules.NextTickDelayMs(5, false, rolls));
        Assert.AreEqual(1, rolls.Asked.Count);
    }

    [TestMethod]
    public void TheNearTestCountsANegativeDistanceAsNearAndTheCatchUpOnlyAFarPcLeader()
    {
        Assert.IsTrue(AiSchedulerRules.IsNear(false, false, 30));
        Assert.IsTrue(AiSchedulerRules.IsNear(false, false, -1), "negative: near, unlike the delay");
        Assert.IsFalse(AiSchedulerRules.IsNear(false, false, 31));
        Assert.IsFalse(AiSchedulerRules.IsNear(true, false, 0));
        Assert.IsFalse(AiSchedulerRules.IsNear(false, true, 0));

        var far = new AiCatchUpFacts(true, 31, false, false, true);
        Assert.IsTrue(AiSchedulerRules.CatchesUp(false, false, far));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { LeaderDistance = 30 }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { LeaderDistance = -1 }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { HasPcLeader = false }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { Waiting = true }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { Paralyzed = true }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, false, far with { Active = false }));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(true, false, far));
        Assert.IsFalse(AiSchedulerRules.CatchesUp(false, true, far));
    }

    [TestMethod]
    public void AWakeUpSetsCheckWieldRunsWaypointsOrStandpointsAndTheFirstHeartbeatBeforeThePass()
    {
        var world = new World { Waypoints = false, State = DangerState.None, Source = null };
        var rolls = new Rolls(700);
        var tick = AiSchedulerRules.Tick(world, Npc, true, rolls, Steamwoven);
        string[] expected = ["bits +CheckWield -None", "waypoints", "standpoints", "script 10 oid:wolf", "script 19 oid:wolf",
            "re-wield all", "bits +None -CheckWield, CheckWeapon", .. PassCalls];
        CollectionAssert.AreEqual(expected, world.Calls);
        Assert.AreEqual(AiTickSteps.WakeUp | AiTickSteps.Waypoints | AiTickSteps.Standpoints | AiTickSteps.FirstHeartbeatContinued |
            AiTickSteps.Heartbeat | AiTickSteps.HeartbeatContinued | AiTickSteps.Pass, tick.Steps);
        Assert.AreEqual((AiTickNext.Delay, 250 + 4750 * 3 / 30 + 700L), (tick.Next, tick.DelayMs));
        Assert.AreEqual(AiPipelineChoice.DangerUpdate, tick.Pass!.Choice);

        // A led NPC, or one in combat mode, keeps to the first heartbeat; a waypoint run skips the standpoints.
        world = new World { LeaderId = Leader, Bits = DangerFlags.CheckWield, Answers = { [AiScriptPoint.Heartbeat] = false } };
        AiSchedulerRules.Tick(world, Npc, true, new Rolls(0), Steamwoven);
        CollectionAssert.AreEqual(new[] { "bits +CheckWield -None", "script 10 oid:wolf", "script 19 oid:wolf" }, world.Calls);
        world = new World { InCombat = { Npc }, Answers = { [AiScriptPoint.Heartbeat] = false } };
        AiSchedulerRules.Tick(world, Npc, true, new Rolls(0), Steamwoven);
        CollectionAssert.AreEqual(new[] { "bits +CheckWield -None", "script 10 oid:wolf", "script 19 oid:wolf" }, world.Calls);
        world = new World { Answers = { [AiScriptPoint.Heartbeat] = false } };
        AiSchedulerRules.Tick(world, Npc, true, new Rolls(0), Steamwoven);
        CollectionAssert.AreEqual(new[] { "bits +CheckWield -None", "waypoints", "script 10 oid:wolf", "script 19 oid:wolf" }, world.Calls);
    }

    [TestMethod]
    public void AHeartbeatThatStopsStillSchedulesAndAFirstHeartbeatThatStopsTakesTheFarPath()
    {
        var world = new World { Answers = { [AiScriptPoint.Heartbeat] = false } };
        var tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        CollectionAssert.AreEqual(new[] { "script 19 oid:wolf" }, world.Calls);
        Assert.AreEqual((AiTickSteps.Heartbeat, AiTickNext.Delay, 725L), (tick.Steps, tick.Next, tick.DelayMs));

        // The far path: animations interrupted, the catch-up asked, then the next tick (jittered after the wake-up).
        world = new World { LeaderId = Leader, Answers = { [AiScriptPoint.FirstHeartbeat] = false }, Facts = new(false, -1, false, false, true) };
        var rolls = new Rolls(5000);
        tick = AiSchedulerRules.Tick(world, Npc, true, rolls, Steamwoven);
        CollectionAssert.AreEqual(new[] { "bits +CheckWield -None", "script 10 oid:wolf", "interrupt", "catch-up facts" }, world.Calls);
        Assert.AreEqual((AiTickSteps.WakeUp | AiTickSteps.AnimationsInterrupted, AiTickNext.Delay, 725L + 5000), (tick.Steps, tick.Next, tick.DelayMs));
    }

    [TestMethod]
    public void AGeneratorTakesTheSpawnersTimeAndARefusalDestroysAnEncounterCreatureOutsideARound()
    {
        var world = new World { Generator = true, Spawned = 60_000 };
        var tick = AiSchedulerRules.Tick(world, Npc, true, new Rolls(), Steamwoven);
        Assert.AreEqual((AiTickNext.Spawner, 60_000L), (tick.Next, tick.DelayMs));
        Assert.IsTrue(tick.Steps.HasFlag(AiTickSteps.Spawned));
        CollectionAssert.DoesNotContain(world.Calls, "re-wield all", "a generator gets no pass");

        world = new World { Generator = true, Encounter = true };
        tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        Assert.AreEqual((AiTickSteps.Heartbeat | AiTickSteps.HeartbeatContinued | AiTickSteps.SpawnRefused | AiTickSteps.Destroyed, AiTickNext.None, 0L),
            (tick.Steps, tick.Next, tick.DelayMs));
        CollectionAssert.AreEqual(new[] { "script 19 oid:wolf", "spawn", "destroy" }, world.Calls);
        world = new World { Generator = true };
        Assert.AreEqual(AiTickNext.None, AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven).Next, "refused: no tick, not destroyed");
        CollectionAssert.DoesNotContain(world.Calls, "destroy");
    }

    [TestMethod]
    public void AFarNpcCatchesUpOrKeepsTicksWhileItsSectorIsLoaded()
    {
        // Far: a follower more than 30 tiles from its PC leader is moved to it and keeps ticking.
        var world = new World { Distance = 45, Loaded = false };
        var tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        CollectionAssert.AreEqual(new[] { "interrupt", "catch-up facts", "move to leader" }, world.Calls);
        Assert.AreEqual((AiTickSteps.AnimationsInterrupted | AiTickSteps.CaughtUp, AiTickNext.Delay, 5000L), (tick.Steps, tick.Next, tick.DelayMs));

        // No catch-up and an unloaded sector: no tick, an encounter creature destroyed (not in a round).
        world = new World { Distance = 45, Loaded = false, Encounter = true, Facts = new(true, 10, false, false, true) };
        tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        Assert.AreEqual((AiTickNext.None, true), (tick.Next, tick.Steps.HasFlag(AiTickSteps.Destroyed)));
        world = new World { Distance = 45, Facts = new(true, 10, false, false, true) };
        tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        Assert.AreEqual((AiTickNext.Delay, 5000L), (tick.Next, tick.DelayMs), "a loaded sector keeps the tick");

        // In a round: no thinking, no interrupt, no catch-up; the tick only reschedules (by the distance).
        world = new World { Round = true, Actor = Npc };
        tick = AiSchedulerRules.Tick(world, Npc, false, new Rolls(), Steamwoven);
        Assert.AreEqual((AiTickSteps.None, AiTickNext.Delay, 725L), (tick.Steps, tick.Next, tick.DelayMs));
        Assert.AreEqual(0, world.Calls.Count);

        // A dead NPC's wake-up sets nothing and runs no script; its tick keeps coming while the sector is loaded.
        world = new World { Dead = { Npc } };
        tick = AiSchedulerRules.Tick(world, Npc, true, new Rolls(10), Steamwoven);
        Assert.AreEqual((AiTickSteps.None, AiTickNext.Delay, 735L), (tick.Steps, tick.Next, tick.DelayMs));
        Assert.AreEqual(0, world.Calls.Count);
    }

    [TestMethod]
    public void TheGateStopsInTheOriginalsOrder()
    {
        AiGateStop Stop(World w) => AiSchedulerRules.Gate(w, Npc, (w.State, w.Source), new Rolls(), Steamwoven).Stop;
        Assert.AreEqual(AiGateStop.Dead, Stop(new World { Dead = { Npc }, Hide = true }));
        Assert.AreEqual(AiGateStop.Hidden, Stop(new World { Hide = true, PcOff = true }));
        Assert.AreEqual(AiGateStop.PcHidden, Stop(new World { Hide = true, Asleep = true, PcOff = true }), "a sleeping NPC passes the hidden test");
        Assert.AreEqual(AiGateStop.Stoned, Stop(new World { Stoned = true, Partner = AiConversation.Other }));
        Assert.AreEqual(AiGateStop.Conversation, Stop(new World { Partner = AiConversation.Other, InCombat = { Npc } }));
        Assert.AreEqual(AiGateStop.NotOwnTurn, Stop(new World { Round = true, Actor = Leader, Priority = 3 }));
        Assert.AreEqual(AiGateStop.Busy, Stop(new World { Round = true, Actor = Npc, Priority = 3, Unconscious = true }));
        Assert.AreEqual(AiGateStop.Passed, Stop(new World { Priority = 2 }));
        Assert.AreEqual(AiGateStop.Stunned, Stop(new World { Flags = new(LifeConditions.Stunned, 0, false, false), StunAnimation = true, Unconscious = true }));
        Assert.AreEqual(AiGateStop.Unconscious, Stop(new World { Unconscious = true, Generator = true }));
        Assert.AreEqual(AiGateStop.Generator, Stop(new World { Generator = true, Bits = DangerFlags.CheckWield }));

        // The conversation ends only with the PC as partner, the callback set and a fight or flight going on.
        foreach (var (partner, callback, state, ends) in new[]
        {
            (AiConversation.Pc, true, DangerState.CombatFocus, true), (AiConversation.Pc, true, DangerState.Flee, true),
            (AiConversation.Pc, true, DangerState.Surrender, false), (AiConversation.Pc, true, DangerState.None, false),
            (AiConversation.Pc, false, DangerState.CombatFocus, false), (AiConversation.Other, true, DangerState.CombatFocus, false),
        })
        {
            var world = new World { Partner = partner, Callback = callback, State = state };
            var gate = AiSchedulerRules.Gate(world, Npc, (state, null), new Rolls(), Steamwoven);
            Assert.AreEqual((AiGateStop.Conversation, ends), (gate.Stop, gate.Steps.HasFlag(AiGateSteps.EndsConversation)), $"{partner} {callback} {state}");
        }
    }

    [TestMethod]
    public void CombatModeRunsTheCombatScriptsWithTheSourceBeforeTheTurnTest()
    {
        var world = new World { InCombat = { Npc }, Source = Leader };
        var gate = AiSchedulerRules.Gate(world, Npc, (DangerState.CombatFocus, Leader), new Rolls(), Steamwoven);
        CollectionAssert.AreEqual(new[] { "script 16 oid:sogg", "script 15 oid:sogg" }, world.Calls);
        Assert.AreEqual(AiGateSteps.EndCombatScript | AiGateSteps.StartCombatScript, gate.Steps);

        // In a round only point 15 runs, also when it is not the NPC's turn (P5); a missing source is no triggerer.
        world = new World { InCombat = { Npc }, Round = true, Actor = Leader };
        gate = AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), new Rolls(), Steamwoven);
        CollectionAssert.AreEqual(new[] { "script 15 -" }, world.Calls);
        Assert.AreEqual(AiGateStop.NotOwnTurn, gate.Stop);
    }

    [TestMethod]
    public void Cl1AStunnedNpcNeverPassesAndItsStunClearsOnlyWhenTheAnimationEnded()
    {
        var stunned = new AiCritterFlags(LifeConditions.Stunned | LifeConditions.Blinded, DangerFlags.Fleeing, true, false);
        var world = new World { Flags = stunned, StunAnimation = true };
        var gate = AiSchedulerRules.Gate(world, Npc, (DangerState.Flee, null), new Rolls(2), Steamwoven);
        Assert.AreEqual((AiGateStop.Stunned, AiGateSteps.None, stunned), (gate.Stop, gate.Steps, world.Flags));

        world = new World { Flags = stunned };
        gate = AiSchedulerRules.Gate(world, Npc, (DangerState.Flee, null), new Rolls(2), Steamwoven);
        Assert.AreEqual((AiGateStop.Stunned, AiGateSteps.StunCleared), (gate.Stop, gate.Steps));
        Assert.AreEqual(stunned with { Conditions = LifeConditions.Blinded }, world.Flags);
        CollectionAssert.DoesNotContain(world.Calls, "heal");
    }

    [TestMethod]
    public void TheRecoveriesRollOneIn160AndOneIn8000()
    {
        var crippled = new AiCritterFlags(LifeConditions.Blinded | AiCritterFlags.Crippled, DangerFlags.None, false, false);
        var world = new World { Flags = crippled };
        var rolls = new Rolls(1, 1);
        var gate = AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), rolls, Steamwoven);
        Assert.AreEqual((AiGateStop.Passed, AiGateSteps.BlindnessCleared | AiGateSteps.CripplingCleared, LifeConditions.None),
            (gate.Stop, gate.Steps, world.Flags.Conditions));
        CollectionAssert.AreEqual(new[] { (AiRollPurpose.GateBlindRecovery, 1, 160), (AiRollPurpose.GateCrippleRecovery, 1, 8000) }, rolls.Asked);

        world = new World { Flags = crippled with { Conditions = LifeConditions.CrippledArmOne } };
        rolls = new Rolls(2);
        gate = AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), rolls, Steamwoven);
        Assert.AreEqual((AiGateSteps.None, LifeConditions.CrippledArmOne), (gate.Steps, world.Flags.Conditions));
        CollectionAssert.AreEqual(new[] { (AiRollPurpose.GateCrippleRecovery, 1, 8000) }, rolls.Asked, "no blindness: no blind roll");
    }

    [TestMethod]
    public void Cl5TheOriginalRecoveryWritesBackTheFlagsReadBeforeTheCombatScripts()
    {
        foreach (var (profile, keeps) in new[] { (Steamwoven, true), (Original, false) })
        {
            // A start-combat script makes the NPC flee (FLEEING is a bit of the critter-flags word); the stun recovery follows.
            var world = new World { InCombat = { Npc }, Flags = new(LifeConditions.Stunned, DangerFlags.None, true, false) };
            world.OnScript = point => { if (point == AiScriptPoint.StartCombat) world.Flags = world.Flags with { Danger = DangerFlags.Fleeing, Concealed = true }; };
            var gate = AiSchedulerRules.Gate(world, Npc, (DangerState.CombatFocus, null), new Rolls(), profile);
            Assert.AreEqual(AiGateSteps.EndCombatScript | AiGateSteps.StartCombatScript | AiGateSteps.StunCleared, gate.Steps, profile.Name);
            Assert.AreEqual(LifeConditions.None, world.Flags.Conditions, profile.Name);
            Assert.AreEqual(keeps, world.Flags.Danger == DangerFlags.Fleeing && world.Flags.Concealed, profile.Name);
        }
    }

    [TestMethod]
    public void Cl2TheFullReWieldClearsBothCheckFlagsAndDemaintainNeedsAnIdleLeader()
    {
        var world = new World { Bits = DangerFlags.CheckWield | DangerFlags.CheckWeapon };
        Assert.AreEqual(AiGateSteps.ReWieldAll, AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), new Rolls(), Steamwoven).Steps);
        Assert.AreEqual(DangerFlags.None, world.Bits);
        world = new World { Bits = DangerFlags.CheckWeapon | DangerFlags.Fighting };
        Assert.AreEqual(AiGateSteps.ReWieldWeapon, AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), new Rolls(), Steamwoven).Steps);
        Assert.AreEqual(DangerFlags.Fighting, world.Bits);
        CollectionAssert.AreEqual(new[] { "re-wield weapon", "bits +None -CheckWeapon" }, world.Calls);

        // DEMAINTAIN_SPELLS: kept while the leader lives in combat mode; otherwise cleared and the spells end.
        foreach (var (leader, dead, combat, ends) in new (string?, bool, bool, bool)[]
            { (null, false, false, true), (Leader, true, true, true), (Leader, false, false, true), (Leader, false, true, false), (AiSchedulerRules.Pc, false, true, false) })
        {
            world = new World { Bits = DangerFlags.DemaintainSpells, LeaderId = leader };
            if (leader is not null && dead) world.Dead.Add(leader);
            if (leader is not null && combat) world.InCombat.Add(leader);
            var gate = AiSchedulerRules.Gate(world, Npc, (DangerState.None, null), new Rolls(), Steamwoven);
            Assert.AreEqual((ends, !ends), (gate.Steps.HasFlag(AiGateSteps.Demaintained), world.Bits.HasFlag(DangerFlags.DemaintainSpells)), $"{leader} {dead} {combat}");
            Assert.AreEqual(ends, world.Calls.Contains("demaintain"));
        }
    }

    [TestMethod]
    public void ThePassRunsHealElseItemSearchElseTheDangerUpdateThenPerform()
    {
        var world = new World { Healing = true };
        Assert.AreEqual(AiPipelineChoice.Heal, AiSchedulerRules.Pass(world, Npc, new Rolls(), Steamwoven).Choice);
        CollectionAssert.AreEqual(new[] { "heal", "perform" }, world.Calls);
        world = new World { Searching = true };
        Assert.AreEqual(AiPipelineChoice.ItemSearch, AiSchedulerRules.Pass(world, Npc, new Rolls(), Steamwoven).Choice);
        CollectionAssert.AreEqual(new[] { "heal", "item search", "perform" }, world.Calls);

        world = new World { Clearing = true, Dead = { Npc } };
        var pass = AiSchedulerRules.Pass(world, Npc, new Rolls(), Steamwoven);
        Assert.AreEqual((true, (AiGateResult?)null, false), (pass.MapClearing, pass.Gate, pass.Performed));
        world = new World { Dead = { Npc } };
        pass = AiSchedulerRules.Pass(world, Npc, new Rolls(), Steamwoven);
        Assert.AreEqual((AiGateStop.Dead, false, 0), (pass.Gate!.Stop, pass.Performed, world.Calls.Count));
    }

    [TestMethod]
    public void TheSubturnHookRunsTheActorsHeartbeatAndPassAndEndsItsSubturnWithoutAnimation()
    {
        var world = new World { Round = true, Actor = Npc };
        var hook = AiSchedulerRules.SubturnHook(world, Npc, new Rolls(), Steamwoven);
        Assert.AreEqual((true, true, true), (hook.Acts, hook.HeartbeatContinued, hook.EndsSubturn));
        string[] expected = ["script 19 oid:wolf", .. PassCalls, "end subturn"];
        CollectionAssert.AreEqual(expected, world.Calls);

        // A stopping heartbeat skips the pass but still ends the subturn; a running animation keeps it.
        world = new World { Round = true, Actor = Npc, Answers = { [AiScriptPoint.Heartbeat] = false }, Animating = true };
        hook = AiSchedulerRules.SubturnHook(world, Npc, new Rolls(), Steamwoven);
        Assert.AreEqual((true, false, (AiPassResult?)null, false), (hook.Acts, hook.HeartbeatContinued, hook.Pass, hook.EndsSubturn));
        CollectionAssert.AreEqual(new[] { "script 19 oid:wolf" }, world.Calls);

        // Not the actor, the PC, or a load: nothing.
        foreach (var w in new[] { new World { Round = true, Actor = Leader }, new World { Round = true, Actor = AiSchedulerRules.Pc }, new World { Round = true, Actor = Npc, IsLoading = true } })
        {
            var who = w.Actor == AiSchedulerRules.Pc ? AiSchedulerRules.Pc : Npc;
            Assert.IsFalse(AiSchedulerRules.SubturnHook(w, who, new Rolls(), Steamwoven).Acts);
            Assert.AreEqual(0, w.Calls.Count);
        }
    }
}
