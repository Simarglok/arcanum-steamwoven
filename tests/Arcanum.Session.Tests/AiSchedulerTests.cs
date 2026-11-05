using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The AI tick on the session's queue, the pass and the subturn hook over the session's state (ARC-WORLD-084, AI slice I6). The
// hero is the half-elf fixture; NPC ids and facts are invented.
[TestClass]
public sealed class AiSchedulerTests
{
    private const long T0 = 54_000_000; // 15:00 on day 0, the fixture's start hour.
    private const string Wolf = "oid:wolf", Dog = "oid:dog";
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };

    /// <summary>A host with critters on the exterior: loaded life states, a distance to the PC and scripted answers.</summary>
    internal sealed class World : ISessionAiWorld
    {
        public readonly Dictionary<string, LifeState> Loaded = new(StringComparer.Ordinal);
        public readonly List<string> Calls = [];
        public Action<AiScriptPoint, string>? OnScript;
        public long Distance = 10;
        public bool Animating;

        public World With(string entity, LifeState loaded = default) { Loaded[entity] = loaded; return this; }

        public SessionLifeTarget? AiLifeTarget(string entity) =>
            Loaded.TryGetValue(entity, out var state) ? SessionLifeTarget.Critter(entity, new LifeFacts(), state) : null;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => Loaded.ContainsKey(entity) ? new LifeFacts() : null;
        public bool ObjectExists(SessionMapId map, string entity) => Loaded.ContainsKey(entity);
        public bool RunScriptCall(SessionScriptCall call) => true;
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
            [new(SessionCombatant.Critter(Wolf), new(101, 200)), new(SessionCombatant.Hero, new(100, 200)), new(SessionCombatant.Critter(Dog), new(102, 200))];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) =>
            Loaded.TryGetValue(entity, out var state) ? new() { Speed = entity == Wolf ? 9 : 6, Life = new LifeFacts(), Loaded = state } : null;

        public long DistanceToPc(string npc) => Distance;
        public bool RunScript(AiScriptPoint point, string npc, string? triggerer)
        {
            Calls.Add($"script {(int)point} {npc} {triggerer ?? "-"}");
            OnScript?.Invoke(point, npc);
            return true;
        }
        public bool RunWaypoints(string npc) { Calls.Add("waypoints"); return true; }
        public void RunStandpoints(string npc) => Calls.Add("standpoints");
        public bool IsGenerator(string npc) => false;
        public long? Spawn(string npc) => null;
        public bool IsEncounterCreature(string npc) => false;
        public void Destroy(string npc) => Calls.Add("destroy");
        public void InterruptAnimations(string npc) => Calls.Add("interrupt");
        public bool SectorLoaded(string npc) => true;
        public AiCatchUpFacts CatchUp(string npc) => new(false, -1, false, false, true);
        public void MoveToLeader(string npc) => Calls.Add("move to leader");
        public bool MapClearing => false;
        public bool Sleeping(string npc) => false;
        public bool Hidden(string npc) => false;
        public bool PcHidden => false;
        public AiConversation Conversation(string npc) => AiConversation.None;
        public bool ConversationCallback => false;
        public void EndConversation(string npc) => Calls.Add("end conversation");
        public int AnimationPriority(string npc) => 0;
        public bool StunAnimationRunning(string npc) => false;
        public bool AnimationRunning(string npc) => Animating;
        public void ReWield(string npc, bool allSlots) => Calls.Add($"re-wield {npc} {(allSlots ? "all" : "weapon")}");
        public string? Leader(string npc) => null;
        public void EndMaintainedSpells(string npc) => Calls.Add("demaintain");
        public bool Heal(string npc) { Calls.Add($"heal {npc}"); return false; }
        public bool SearchItems(string npc) => false;
        public void UpdateDanger(string npc) => Calls.Add($"danger update {npc}");
        public void Perform(string npc) => Calls.Add($"perform {npc}");
        public bool Loading => false;
    }

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void Schedule(GameSessionOwner owner, string entity, SessionAiTickStart start = SessionAiTickStart.WakeUp, long distance = 0) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = entity, Start = start, DistanceToPc = distance }).Disposition);

    [TestMethod]
    public void TicksReplaceEachOtherRoundTripAndAreRebuiltAsWakeUpsUnderTheOriginalSetting()
    {
        using var owner = Owner();
        Schedule(owner, Wolf);
        Assert.AreEqual((T0 + 1, true), owner.QueryAiTick(Wolf));
        Schedule(owner, Wolf, SessionAiTickStart.Delayed, 10);
        Assert.AreEqual((T0 + 1833, false), owner.QueryAiTick(Wolf), "the delayed tick replaces the wake-up");
        Schedule(owner, Dog, SessionAiTickStart.Delayed, -1);
        Assert.AreEqual((T0 + 5000, false), owner.QueryAiTick(Dog));
        Assert.AreEqual(2, owner.QueryEvents().Count);

        Assert.AreEqual(GameSessionFailure.InvalidEvents, owner.ScheduleAiTick(owner.Version, new() { Entity = "bad id" }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, owner.ScheduleAiTick(owner.Version, new() { Entity = "-" }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, owner.ScheduleAiTick(owner.Version, new() { Entity = AiSchedulerRules.Pc }).Failure, "never the PC");
        Assert.AreEqual(GameSessionFailure.InvalidEvents,
            owner.ScheduleAiTick(owner.Version, new() { Entity = Wolf, Start = (SessionAiTickStart)7 }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion,
            owner.ScheduleAiTick(new GameSessionVersion(owner.Identity, 0), new() { Entity = Wolf }).Failure);

        // Steamwoven keeps the saved timing (OQ2); the Original setting rebuilds every tick as a wake-up 1 ms after the clock.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"ai.tick\"");
        using (var kept = Restore(Decode(text)))
        {
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(kept.Capture()));
            Assert.AreEqual((T0 + 1833, false), kept.QueryAiTick(Wolf));
        }
        using var rebuilt = Restore(Decode(text), RestoreOptions() with { Rules = RuleMode.Original });
        Assert.AreEqual((T0 + 1, true), rebuilt.QueryAiTick(Wolf));
        Assert.AreEqual((T0 + 1, true), rebuilt.QueryAiTick(Dog));

        // A saved tick off the actor's map, or with a deadline its delay does not give, is refused.
        Assert.AreEqual(GameSessionFailure.InvalidEvents,
            GameSessionOwner.Restore(Decode(text.Replace("[{\"map\":79,\"entity\":\"oid:wolf\"}]", "[{\"map\":80,\"entity\":\"oid:wolf\"}]")), RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEvents,
            GameSessionOwner.Restore(Decode(text.Replace($"\"deadlineMs\":{T0 + 1833}", $"\"deadlineMs\":{T0 + 1834}")), RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEvents,
            GameSessionOwner.Restore(Decode(text.Replace("\"entity\":\"oid:dog\"", "\"entity\":\"pc\"")), RestoreOptions()).Failure, "never the PC");
    }

    [TestMethod]
    public void ATickRunsThePassAndSchedulesTheNextByTheDistance()
    {
        using var owner = Owner();
        var world = new World().With(Wolf);
        Schedule(owner, Wolf);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        var firing = pass.AiTicks.Single();
        Assert.AreEqual((Wolf, true, GameSessionFailure.None), (firing.Entity, firing.WakeUp, firing.WriteFailure));
        Assert.AreEqual(AiTickSteps.WakeUp | AiTickSteps.Waypoints | AiTickSteps.FirstHeartbeatContinued | AiTickSteps.Heartbeat |
            AiTickSteps.HeartbeatContinued | AiTickSteps.Pass, firing.Tick!.Steps);
        CollectionAssert.AreEqual(new[] { "waypoints", "script 10 oid:wolf oid:wolf", "script 19 oid:wolf oid:wolf", "re-wield oid:wolf all",
            "heal oid:wolf", "danger update oid:wolf", "perform oid:wolf" }, world.Calls);
        Assert.AreEqual(DangerFlags.None, owner.QueryDanger(Wolf).Flags, "the wake-up's check wield is cleared by the re-wield");

        // The next tick: 1833 ms at 10 tiles plus the wake-up's jitter, without the marker.
        var (deadline, wakeUp) = owner.QueryAiTick(Wolf)!.Value;
        Assert.IsFalse(wakeUp);
        Assert.AreEqual(T0 + 1 + firing.Tick.DelayMs, deadline);
        Assert.IsTrue(firing.Tick.DelayMs is >= 1833 and <= 1833 + 5000, firing.Tick.DelayMs.ToString());
        Assert.AreEqual(owner.Version, pass.Version);

        // A plain script host consumes a due tick without running it; nothing follows.
        Advance(owner, firing.Tick.DelayMs);
        var plain = owner.ProcessEvents(new PlainWorld());
        Assert.IsNull(plain.AiTicks.Single().Tick);
        Assert.IsNull(owner.QueryAiTick(Wolf));
    }

    private sealed class PlainWorld : ISessionScriptWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call) => true;
    }

    [TestMethod]
    public void AMapChangeRemovesEveryTickAndTicksWaitDuringAPass()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Dog).Disposition);
        Schedule(owner, Wolf);
        Schedule(owner, Dog);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start with { Cell = new(101, 200) }).Disposition);
        Assert.AreEqual(2, owner.QueryEvents().Count, "a step on the same map keeps them");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(0, owner.QueryEvents().Count, "the follower's tick goes too: the new map's fold wakes it");

        // A tick cannot be scheduled from inside a pass (it reschedules itself).
        Schedule(owner, Wolf);
        Advance(owner, 1);
        var world = new World().With(Wolf);
        world.OnScript = (point, _) =>
        {
            if (point == AiScriptPoint.Heartbeat)
                Assert.AreEqual(GameSessionFailure.InvalidEventPass, owner.ScheduleAiTick(owner.Version, new() { Entity = Dog }).Failure);
        };
        Assert.AreEqual(1, owner.ProcessEvents(world).AiTicks.Count);
    }

    [TestMethod]
    public void Cl5TheStunRecoveryOfTheOriginalSettingUndoesAFlightTheStartCombatScriptBegan()
    {
        foreach (var (rules, keeps) in new[] { (RuleMode.Steamwoven, true), (RuleMode.Original, false) })
        {
            using var owner = Owner(rules);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, SessionCombatant.Critter(Wolf), true, Living).Disposition);
            var world = new World().With(Wolf, new LifeState(0, 0, 0, LifeConditions.Stunned | LifeConditions.Blinded, 0));
            world.OnScript = (point, npc) =>
            {
                if (point == AiScriptPoint.StartCombat)
                    Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version,
                        new SessionDangerRequest { Entity = npc, State = DangerState.Flee, Target = SessionCombatant.Hero }).Disposition);
            };
            var result = owner.RunAiPass(owner.Version, Wolf, world);
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.WriteFailure), rules.ToString());
            Assert.AreEqual(AiGateStop.Stunned, result.Pass!.Gate!.Stop);
            Assert.IsTrue(result.Pass.Gate.Steps.HasFlag(AiGateSteps.StunCleared));
            Assert.AreEqual(LifeConditions.Blinded & owner.QueryLifeRecord(Wolf)!.Value.State.Conditions, owner.QueryLifeRecord(Wolf)!.Value.State.Conditions,
                "the stun is cleared, blindness may have been cleared by its roll");
            Assert.IsFalse(owner.QueryLifeRecord(Wolf)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned));
            Assert.AreEqual(keeps, owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.Fleeing), rules.ToString());
            Assert.AreEqual(result.Version, owner.Version);
        }
    }

    [TestMethod]
    public void Cl2CheckWeaponReWieldsTheWeaponAndRoundTripsByName()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version,
            new SessionDangerRequest { Entity = Wolf, State = DangerState.CombatFocus, Target = SessionCombatant.Critter(Dog) }).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("[\"Fighting\",\"CheckWield\",\"CheckGrenade\"]", "[\"Fighting\",\"CheckGrenade\",\"CheckWeapon\"]");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(DangerFlags.Fighting | DangerFlags.CheckGrenade | DangerFlags.CheckWeapon, restored.QueryDanger(Wolf).Flags);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(restored.Capture()), "\"flags\":[\"Fighting\",\"CheckGrenade\",\"CheckWeapon\"]");

        var world = new World().With(Wolf);
        var revision = restored.Version.Revision;
        var result = restored.RunAiPass(restored.Version, Wolf, world);
        Assert.AreEqual(AiGateSteps.ReWieldWeapon, result.Pass!.Gate!.Steps);
        CollectionAssert.Contains(world.Calls, "re-wield oid:wolf weapon");
        Assert.AreEqual(DangerFlags.Fighting | DangerFlags.CheckGrenade, restored.QueryDanger(Wolf).Flags);
        Assert.AreEqual(revision + 1, restored.Version.Revision, "one revision for the bit");

        Assert.AreEqual(GameSessionFailure.StaleVersion, restored.RunAiPass(new GameSessionVersion(restored.Identity, revision), Wolf, world).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, restored.RunAiPass(restored.Version, AiSchedulerRules.Pc, world).Failure);
        // A gone NPC counts as dead: the gate stops at once.
        Assert.AreEqual(AiGateStop.Dead, restored.RunAiPass(restored.Version, "oid:gone", world).Pass!.Gate!.Stop);
    }

    [TestMethod]
    public void TheSubturnHookRunsTheNpcActorAndPassesItsTurnWhenNoAnimationRuns()
    {
        using var owner = Owner();
        var world = new World().With(Wolf).With(Dog);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, world).Disposition);
        Assert.AreEqual(SessionCombatant.Critter(Wolf), owner.QueryCombatRound()!.Actor);

        var hero = owner.RunAiSubturnHook(owner.Version, SessionCombatant.Hero, world);
        Assert.IsFalse(hero.Hook!.Acts, "never for the PC");
        var dog = owner.RunAiSubturnHook(owner.Version, SessionCombatant.Critter(Dog), world);
        Assert.IsFalse(dog.Hook!.Acts, "only for the actor");

        world.Animating = true;
        var busy = owner.RunAiSubturnHook(owner.Version, SessionCombatant.Critter(Wolf), world);
        Assert.AreEqual((true, true, false), (busy.Hook!.Acts, busy.Hook.HeartbeatContinued, busy.Hook.EndsSubturn));
        Assert.AreEqual(SessionCombatant.Critter(Wolf), owner.QueryCombatRound()!.Actor, "an animation keeps the subturn");
        world.Animating = false;
        world.Calls.Clear();
        var done = owner.RunAiSubturnHook(owner.Version, SessionCombatant.Critter(Wolf), world);
        Assert.AreEqual((true, true), (done.Hook!.EndsSubturn, done.Pass!.Performed));
        CollectionAssert.AreEqual(new[] { "script 19 oid:wolf oid:wolf", "heal oid:wolf", "danger update oid:wolf", "perform oid:wolf" }, world.Calls);
        Assert.AreNotEqual(SessionCombatant.Critter(Wolf), owner.QueryCombatRound()!.Actor);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (done.Round!.Disposition, done.WriteFailure));
        Assert.IsTrue(done.Round.Events.Any(e => e.Kind == SessionCombatEventKind.SubturnStarted), "the next subturn's start reaches the host");
        Assert.IsNull(busy.Round);
    }

    [TestMethod]
    public void AMapChangeDropsDangerRecordsThatHoldOnlyTheCheckBits()
    {
        using var owner = Owner();
        var world = new World { Distance = 45 }.With(Wolf).With(Dog);
        // Two far wake-ups: "check wield" set, no pass to clear it; the dog also fights the wolf.
        Schedule(owner, Wolf);
        Schedule(owner, Dog);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version,
            new SessionDangerRequest { Entity = Dog, State = DangerState.CombatFocus, Target = SessionCombatant.Critter(Wolf) }).Disposition);
        Advance(owner, 1);
        Assert.AreEqual(2, owner.ProcessEvents(world).AiTicks.Count);
        Assert.AreEqual(DangerFlags.CheckWield, owner.QueryDanger(Wolf).Flags);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(DangerFlags.None, owner.QueryDanger(Wolf).Flags, "the next fold's wake-up sets it again");
        Assert.IsFalse(GameSessionSnapshotCodec.Encode(owner.Capture()).Contains("\"entity\":\"oid:wolf\",\"flags\""), "no record left");
        Assert.IsTrue(owner.QueryDanger(Dog).Flags.HasFlag(DangerFlags.CheckWield), "a record with more than the check bits stays whole");
    }

    // ARC-DEV-134 (MIG13 S4): the AI tick's pass starts goals inside the pass (the original's heartbeat starts its goal there); the run's first step is the next pass's.
    [TestMethod]
    public void AGoalStartedInsideTheTicksPassStepsInTheNextPass()
    {
        using var owner = Owner();
        var world = new World().With(Wolf);
        SessionGoalStartResult? started = null;
        world.OnScript = (_, npc) => started ??= owner.StartGoal(
            new Arcanum.Simulation.Goals.GoalStartRequest(npc, Arcanum.Simulation.Goals.GoalStarters.NoInterrupt, new Arcanum.Simulation.Goals.GoalFrame(Arcanum.Simulation.Goals.GoalType.Attack, "pc")),
            new OpenPorts());
        Schedule(owner, Wolf);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.IsNotNull(started);
        Assert.AreEqual(SessionGoalFailure.None, started.Failure, "a handler may start a run");
        Assert.IsTrue(started.Started);
        Assert.AreEqual(0, pass.GoalSteps.Count, "the step it queued waits for the next pass");
        Assert.IsNotNull(owner.QueryGoalStep(started.Handle!.Value));
        Assert.IsTrue(owner.QueryGoals().TryGet(started.Handle.Value, out var run) && run.Owner == Wolf);
    }

    private sealed class OpenPorts : Arcanum.Simulation.Goals.IGoalStartPorts
    {
        public bool GatePasses(Arcanum.Simulation.Goals.GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
