using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's event queue and the life timers on it (ARC-WORLD-024). Entity ids and facts are invented.
[TestClass]
public sealed class SessionEventTests
{
    private const long T0 = 54_000_000; // 15:00 on day 0, the fixture's start hour.

    // ST 8, CN 8, WP 8, level 1: 30 hit points, heal rate 3, poison recovery 8.
    private static readonly LifeFacts Wolf = new();

    private sealed class World : ISessionEventWorld
    {
        public Dictionary<(int Map, string Entity), LifeFacts> Present { get; } = new();
        public LifeFacts? CritterFacts(SessionMapId map, string entity) =>
            Present.TryGetValue((map.RuntimeMap, entity), out var facts) ? facts : null;
    }

    private static SessionLifeTarget Npc(string entity) => SessionLifeTarget.Critter(entity, Wolf, LifeState.Unharmed);

    private static GameSessionOwner Session(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Advance(GameSessionOwner owner, long ms, ClockAdvanceReason reason = ClockAdvanceReason.OrdinaryAdvance) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, reason).Disposition);

    private static SessionEventPassResult Pass(GameSessionOwner owner, ISessionEventWorld world)
    {
        var result = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static string[] Pending(GameSessionOwner owner) =>
        owner.QueryEvents().Select(e => $"{e.Kind}@{e.DeadlineMs}" + string.Concat(e.Payload.Entities.Select(x => $":{x.Map}/{x.Code}"))).ToArray();

    private static void Damage(GameSessionOwner owner, SessionLifeTarget target, LifeDamage damage) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, target, damage).Disposition);

    [TestMethod]
    public void HeroFatigueRecoversEveryEightySecondsAndAJumpCountsTheElapsedTime()
    {
        using var owner = Session();
        var world = new World();
        Damage(owner, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 10 });
        CollectionAssert.AreEqual(new[] { $"life.hero.fatigue@{T0 + 80_000}" }, Pending(owner));
        Assert.AreEqual(T0, owner.QueryEvents()[0].Payload.Values[0], "the timer keeps the game time it was set at");
        var revision = owner.Version.Revision;

        Advance(owner, 79_999);
        Assert.AreEqual(0, Pass(owner, world).Life.Count);
        Advance(owner, 1);
        var first = Pass(owner, world);
        // One step of heal rate 2 (half-elf CN 7); a positive write sets the next timer 80 s on.
        Assert.AreEqual(8, owner.QueryHeroLife()!.Record.State.FatigueDamage);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (first.Life.Single().Life.Disposition, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { $"life.hero.fatigue@{T0 + 160_000}" }, Pending(owner));

        // A wait of 800 s: the timer fires once, late, with 880 elapsed seconds = 11 steps, and recovery ends.
        Advance(owner, 800_000, ClockAdvanceReason.ExplicitWait);
        Assert.AreEqual(1, Pass(owner, world).Life.Count);
        Assert.AreEqual(0, owner.QueryHeroLife()!.Record.State.FatigueDamage);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }

    [TestMethod]
    public void PoisonTicksDamageAndRecoverWithOneTimerOfEachKind()
    {
        using var owner = Session(hero: false);
        var world = new World { Present = { [(79, "oid:wolf")] = Wolf } };
        Damage(owner, Npc("oid:wolf"), new LifeDamage { Poison = 300 });
        CollectionAssert.AreEqual(new[] { $"life.npc.poison-damage@{T0 + 15_000}:79/oid:wolf",
            $"life.npc.poison-recovery@{T0 + 120_000}:79/oid:wolf" }, Pending(owner));

        Advance(owner, 15_000);
        Pass(owner, world);
        // Level 300: 2 damage. The hit-point write sets the hourly resting timer; the damage tick sets the next one.
        Assert.AreEqual((2, 300), (owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage, owner.QueryLifeRecord("oid:wolf")!.Value.State.PoisonLevel));
        CollectionAssert.AreEqual(new[] { $"life.npc.poison-damage@{T0 + 30_000}:79/oid:wolf",
            $"life.npc.poison-recovery@{T0 + 120_000}:79/oid:wolf", $"life.npc.resting@{T0 + 3_615_000}:79/oid:wolf" }, Pending(owner));

        // At 120 s the late damage tick fires once (not six times), then the recovery tick (poison recovery 8).
        Advance(owner, 105_000);
        var pass = Pass(owner, world);
        CollectionAssert.AreEqual(new[] { "life.npc.poison-damage", "life.npc.poison-recovery" }, pass.Life.Select(f => f.Event.Kind).ToArray());
        Assert.AreEqual((4, 292), (owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage, owner.QueryLifeRecord("oid:wolf")!.Value.State.PoisonLevel));
        // One recovery timer: the source's handler adds a second one (anomaly TE10), the project keeps one.
        CollectionAssert.AreEqual(new[] { $"life.npc.poison-damage@{T0 + 135_000}:79/oid:wolf",
            $"life.npc.poison-recovery@{T0 + 240_000}:79/oid:wolf", $"life.npc.resting@{T0 + 3_615_000}:79/oid:wolf" }, Pending(owner));

        // Cured: the next ticks find no poison and set nothing more; resting goes on while hit points are missing.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Heal(owner.Version, Npc("oid:wolf"), new LifeHeal { Poison = 1000 }).Disposition);
        Advance(owner, 120_000);
        Assert.AreEqual(0, Pass(owner, world).Life.Count);
        CollectionAssert.AreEqual(new[] { $"life.npc.resting@{T0 + 3_615_000}:79/oid:wolf" }, Pending(owner));
    }

    [TestMethod]
    public void NpcTimersWaitForTheirMapAndFollowersCarryThemAlong()
    {
        using var owner = Session(hero: false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Damage(owner, Npc("oid:wolf"), new LifeDamage { HitPoints = 5 });
        Damage(owner, Npc("virgil"), new LifeDamage { Fatigue = 10 });
        CollectionAssert.AreEqual(new[] { $"life.npc.fatigue@{T0 + 80_000}:79/virgil", $"life.npc.resting@{T0 + 3_600_000}:79/oid:wolf" },
            Pending(owner));

        // The PC goes into the cave with Virgil: his timer goes along, the wolf's stays with the exterior.
        var revision = owner.Version.Revision;
        Changed(owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)), owner, revision + 1);
        CollectionAssert.AreEqual(new[] { $"life.npc.fatigue@{T0 + 80_000}:80/virgil", $"life.npc.resting@{T0 + 3_600_000}:79/oid:wolf" },
            Pending(owner));

        var world = new World { Present = { [(80, "virgil")] = Wolf, [(79, "oid:wolf")] = Wolf } };
        Advance(owner, 2 * 3_600_000, ClockAdvanceReason.ExplicitWait);
        var cave = Pass(owner, world);
        Assert.AreEqual("virgil", cave.Life.Single().Event.Payload.Entities[0].Code);
        Assert.AreEqual(0, owner.QueryLifeRecord("virgil")!.Value.State.FatigueDamage, "90 steps of heal rate 3");
        Assert.AreEqual(5, owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage, "the exterior's timer waits");

        // Back outside, the parked timer fires late with the hours since it was set (2 × 3 = 6 healed).
        revision = owner.Version.Revision;
        Changed(owner.PlaceActor(owner.Version, Start), owner, revision + 1);
        Pass(owner, world);
        Assert.AreEqual(0, owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }

    [TestMethod]
    public void RestingSkipsFollowersAndTheDeadAndMissingEntitiesAreDropped()
    {
        using var owner = Session(hero: false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Damage(owner, Npc("virgil"), new LifeDamage { HitPoints = 5 });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc("oid:wolf")).Disposition);
        Damage(owner, Npc("oid:gone"), new LifeDamage { HitPoints = 1 });
        Assert.AreEqual(3, owner.QueryEvents().Count);

        var world = new World { Present = { [(79, "virgil")] = Wolf, [(79, "oid:wolf")] = Wolf } };
        Advance(owner, 3_600_000);
        var revision = owner.Version.Revision;
        var pass = Pass(owner, world);
        // Virgil has a PC leader and the wolf is dead: no heal and no next timer; the missing NPC's timer does not run.
        Assert.AreEqual(0, pass.Life.Count);
        CollectionAssert.AreEqual(new[] { GameEventOutcomeKind.Fired, GameEventOutcomeKind.Fired, GameEventOutcomeKind.EntityMissing },
            pass.Pass!.Outcomes.Select(o => o.Kind).ToArray());
        Assert.AreEqual("oid:gone", pass.Pass.Outcomes[2].Event.Payload.Entities[0].Code);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }

    [TestMethod]
    public void APassFiresDueTimersWhilePausedAndRefusesABadBudget()
    {
        using var owner = Session();
        var world = new World();
        Damage(owner, SessionLifeTarget.Hero, new LifeDamage { Poison = 100 });
        Advance(owner, 15_000);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.TurnBased)).Disposition);
        Assert.AreEqual(1, Pass(owner, world).Life.Count, "pausing stops the clock, not the timers already due");

        foreach (var budget in new[] { 0, 501 })
        {
            var refused = owner.ProcessEvents(world, budget);
            Assert.AreEqual((GameSessionFailure.InvalidEventPass, (GameEventPassResult?)null), (refused.Failure, refused.Pass));
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ProcessEvents(null!));
    }

    [TestMethod]
    public void TheQueueHoldsEveryTimerTheLifeRecordsCanNeed()
    {
        using var owner = Session();
        // PolicyMaxLifeRecords NPC records with four timers each, then the hero's three: exactly the session's queue size without the
        // hero's encumbrance timer (ARC-WORLD-087).
        for (var i = 0; i < GameSessionLimits.PolicyMaxLifeRecords; i++)
            Damage(owner, Npc($"oid:n{i}"), new LifeDamage { HitPoints = 1, Fatigue = 1, Poison = 10 });
        Damage(owner, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1, Fatigue = 1, Poison = 10 });
        Assert.AreEqual(SessionEventKinds.MaxPending - SessionEventKinds.MaxScriptCalls - SessionEffectLimits.MaxTimedEffects - SessionEventKinds.MaxLockResets
            - SessionEventKinds.MaxWaitTimers - SessionEventKinds.MaxObjectRespawns - SessionEventKinds.MaxSpellRunEvents - SessionEventKinds.MaxItemDecays
            - SessionEventKinds.MaxAiTicks - SessionEventKinds.MaxRestocks - SessionEventKinds.MaxRealSteps - SessionEventKinds.MaxGoalSteps - 1, owner.QueryEvents().Count);
        // ARC-SCRIPT-010: the deferred SCR calls have their own share of the queue on top.
        for (var i = 0; i < SessionEventKinds.MaxScriptCalls; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(i + 1, 0, null, null), 1000).Disposition);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.ScheduleScriptCall(owner.Version, new(1, 0, null, null), 1000).Failure);
        // ARC-WORLD-029: and so do the timed effects' ENDs (each entry removed again, so only the ENDs stay).
        var effect = new SessionEffectDefinition(120, [new(Arcanum.Data.Characters.EffectAttributeKind.BasicSkill, 1, Arcanum.Data.Characters.EffectChangeOperator.Add, 1)]);
        for (var i = 0; i < SessionEffectLimits.MaxTimedEffects; i++)
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroTimedEffect(owner.Version, effect, Arcanum.Simulation.Character.EffectCause.Spell, 1, 1000).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 120).Disposition);
        }
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded,
            owner.AddHeroTimedEffect(owner.Version, effect, Arcanum.Simulation.Character.EffectCause.Spell, 1, 1000).Failure);
        // ARC-WORLD-050: and so do the Lock events (each unlock of a locked door adds one).
        var door = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Portal, Arcanum.Simulation.Locks.LockRules.LockedBit, 0, 0);
        for (var i = 0; i < SessionEventKinds.MaxLockResets; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetOpenableLocked(owner.Version, new(Exterior, $"door:{i}"), door, false).Disposition);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.SetOpenableLocked(owner.Version, new(Exterior, "door:x"), door, false).Failure);
        Assert.IsNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity("door:x", GameSessionOwner.FlagsProperty)));
        // ARC-WORLD-045: and so do the wait timers, one per recorded companion.
        var wait = new PartyWaitFacts(3, Arcanum.Simulation.Party.PersuasionTraining.None, 8, false);
        for (var i = 0; i < SessionEventKinds.MaxWaitTimers; i++)
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, $"oid:w{i}").Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, $"oid:w{i}", wait).Disposition);
        }
        // ARC-COMBAT-008: and so do the respawn events of respawning scenery an attack broke (no hit points: every hit breaks it).
        SessionObjectTarget Barrel(string entity) => new(Exterior, entity, new SessionObjectBaseline
        {
            Kind = Arcanum.Simulation.Combat.DamageableObjectKind.Scenery, Respawning = true, RespawnDelay = 10,
        });
        for (var i = 0; i < SessionEventKinds.MaxObjectRespawns; i++)
            Assert.AreEqual(GameSessionFailure.None, owner.AttackObject(owner.Version, new() { Target = Barrel($"barrel:{i}") }).Failure);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.AttackObject(owner.Version, new() { Target = Barrel("barrel:x") }).Failure);
        // ARC-WORLD-084: and so do the AI ticks, one per NPC (a second one replaces the first).
        for (var i = 0; i < SessionEventKinds.MaxAiTicks; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = $"oid:t{i}" }).Disposition);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.ScheduleAiTick(owner.Version, new() { Entity = "oid:tx" }).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = "oid:t0", Start = SessionAiTickStart.Delayed }).Disposition);
        // ARC-WORLD-094: and so do the restock events, one per holder (a second one replaces the first).
        for (var i = 0; i < SessionEventKinds.MaxRestocks; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, $"chest:{i}", true).Disposition);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.ScheduleRestock(owner.Version, "chest:x", true).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, "chest:0", true).Disposition);
        // ARC-MAGIC-010: the magic-tech events have a share too (not saved; empty here), and so do the real-time steps (MIG12 S8; not saved); so do the Item Decay events (ARC-WORLD-062) and the
        // hero's encumbrance timer (ARC-WORLD-087).
        Assert.AreEqual(SessionEventKinds.MaxPending - SessionEventKinds.MaxSpellRunEvents - SessionEventKinds.MaxItemDecays - SessionEventKinds.MaxRealSteps - SessionEventKinds.MaxGoalSteps - 1, owner.QueryEvents().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, Npc("oid:n0"), new LifeDamage { HitPoints = 1 }).Disposition);
        // A save of a full queue restores.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(SessionEventKinds.MaxPending - SessionEventKinds.MaxSpellRunEvents - SessionEventKinds.MaxItemDecays - SessionEventKinds.MaxRealSteps - SessionEventKinds.MaxGoalSteps - 1, restored.QueryEvents().Count);
    }

    [TestMethod]
    public void AFatigueTimerOfADeadCritterEndsAndATimerFollowsItsNpcToAnotherMap()
    {
        using var owner = Session(hero: false);
        Damage(owner, Npc("oid:wolf"), new LifeDamage { Fatigue = 10 });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc("oid:wolf")).Disposition);
        Damage(owner, Npc("oid:fox"), new LifeDamage { Fatigue = 10 });
        var world = new World { Present = { [(79, "oid:wolf")] = Wolf, [(80, "oid:fox")] = Wolf } };

        // The fox (not a follower) turns up in the cave: a step there takes its pending timer along, one per kind.
        var revision = owner.Version.Revision;
        Changed(owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)), owner, revision + 1);
        Damage(owner, Npc("oid:fox"), new LifeDamage { Fatigue = 5 });
        CollectionAssert.AreEqual(new[] { $"life.npc.fatigue@{T0 + 80_000}:79/oid:wolf", $"life.npc.fatigue@{T0 + 80_000}:80/oid:fox",
            $"life.npc.resting@{T0 + 3_600_000}:79/oid:wolf" }, Pending(owner));

        Advance(owner, 80_000);
        Pass(owner, world);
        revision = owner.Version.Revision;
        Changed(owner.PlaceActor(owner.Version, Start), owner, revision + 1);
        var exterior = Pass(owner, world);
        // The dead wolf's timer fires without a step and sets no next one (the source returns early for the dead).
        Assert.AreEqual(0, exterior.Life.Count);
        CollectionAssert.AreEqual(new[] { $"life.npc.fatigue@{T0 + 160_000}:80/oid:fox", $"life.npc.resting@{T0 + 3_600_000}:79/oid:wolf" },
            Pending(owner));
    }

    [TestMethod]
    public void AMapTransferStepCarriesFollowersTimersButNotWaitingOnes()
    {
        using var owner = Session(hero: false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "sogg").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "sogg").Disposition);
        Damage(owner, Npc("virgil"), new LifeDamage { Fatigue = 10 });
        Damage(owner, Npc("sogg"), new LifeDamage { Fatigue = 10 });

        var revision = owner.Version.Revision;
        Changed(owner.ApplyChanges(owner.Version, new(Cave, new(5, 5), Start.Facing),
            [new(Cave, MapChangeKey.ForEntity("virgil", "npc.map"), 80)]), owner, revision + 1);
        CollectionAssert.AreEqual(new[] { $"life.npc.fatigue@{T0 + 80_000}:80/virgil", $"life.npc.fatigue@{T0 + 80_000}:79/sogg" },
            Pending(owner));

        // A call back from the host's world port during a pass cannot move the PC.
        var world = new CallbackWorld(owner);
        Advance(owner, 80_000);
        Pass(owner, world);
        Assert.AreEqual(GameSessionFailure.InvalidEventPass, world.Placement!.Value.Failure);
        Assert.AreEqual(Cave, owner.QueryActor().Map);
    }

    private sealed class CallbackWorld(GameSessionOwner owner) : ISessionEventWorld
    {
        public GameSessionMutationResult? Placement { get; private set; }
        public LifeFacts? CritterFacts(SessionMapId map, string entity)
        {
            Placement ??= owner.PlaceActor(owner.Version, Start);
            return Wolf;
        }
    }

    [TestMethod]
    public void TheOriginalModeRecoversPoisonTwicePerPeriodAfterTheFirstTick()
    {
        // TE10: from the second tick on the source runs two recovery events per 120 s; the mode is read when the timer fires.
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Session(hero: false);
            Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.SelectRules(mode));
            var world = new World { Present = { [(79, "oid:wolf")] = Wolf } };
            Damage(owner, Npc("oid:wolf"), new LifeDamage { Poison = 300 });
            Assert.AreEqual(1, owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.NpcPoisonRecovery).Payload.Values[1]);
            Advance(owner, 120_000);
            Pass(owner, world);
            Assert.AreEqual(292, owner.QueryLifeRecord("oid:wolf")!.Value.State.PoisonLevel, mode.ToString());
            Assert.AreEqual(2, owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.NpcPoisonRecovery).Payload.Values[1]);
            Advance(owner, 120_000);
            Pass(owner, world);
            Assert.AreEqual(mode == RuleMode.Original ? 276 : 284, owner.QueryLifeRecord("oid:wolf")!.Value.State.PoisonLevel, mode.ToString());
        }
    }

    [TestMethod]
    public void PendingTimersRoundTripThroughCanonicalTextAndContinueAfterARestore()
    {
        using var owner = Session(hero: false);
        Damage(owner, Npc("oid:wolf"), new LifeDamage { HitPoints = 2, Fatigue = 6 });
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string events = ",\"events\":{\"registry\":1,\"nextSequence\":3,\"pending\":[" +
            "{\"sequence\":1,\"kind\":\"life.npc.fatigue\",\"deadlineMs\":54080000,\"entities\":[{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000000]}," +
            "{\"sequence\":2,\"kind\":\"life.npc.resting\",\"deadlineMs\":57600000,\"entities\":[{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000000]}]}";
        StringAssert.Contains(text, events + NoHotkeys + NoScripts + NoResidency + ",\"lighting\":null}");

        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var restored = Restore(Decode(text), RestoreOptions() with { Rules = mode });
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), mode.ToString());
            CollectionAssert.AreEqual(owner.QueryEvents().ToArray(), restored.QueryEvents().ToArray());
        }

        // The saved session and the restored one fire the same timers to the same state.
        using var copy = Restore(Decode(text));
        var world = new World { Present = { [(79, "oid:wolf")] = Wolf } };
        foreach (var session in new[] { owner, copy })
        {
            Advance(session, 3_600_000, ClockAdvanceReason.ExplicitWait);
            Pass(session, world);
        }
        Assert.AreEqual(owner.QueryLifeRecord("oid:wolf"), copy.QueryLifeRecord("oid:wolf"));
        CollectionAssert.AreEqual(owner.QueryEvents().ToArray(), copy.QueryEvents().ToArray());
    }

    [TestMethod]
    public void DecodeAndRestoreRefuseEventsNoSessionCanProduce()
    {
        using var owner = Session(hero: false);
        Damage(owner, Npc("oid:wolf"), new LifeDamage { HitPoints = 2, Fatigue = 6 });
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());

        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            Assert.IsTrue(text.Contains(from, StringComparison.Ordinal), from);
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        GameSessionFailure Restored(string from, string to)
        {
            Assert.IsTrue(text.Contains(from, StringComparison.Ordinal), from);
            var result = GameSessionOwner.Restore(Decode(text.Replace(from, to, StringComparison.Ordinal)), RestoreOptions());
            result.Owner?.Dispose();
            return result.Failure;
        }

        Fails("\"registry\":1", "\"registry\":0", SnapshotDecodeFailure.OutOfRange, "$.events.registry");
        Fails("\"nextSequence\":3", "\"nextSequence\":0", SnapshotDecodeFailure.OutOfRange, "$.events.nextSequence");
        Fails("\"kind\":\"life.npc.fatigue\"", "\"kind\":\"life npc\"", SnapshotDecodeFailure.OutOfRange, "$.events.pending[0].kind");
        Fails("{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000000]},", "{\"map\":0,\"entity\":\"oid:wolf\"}],\"values\":[54000000]},",
            SnapshotDecodeFailure.OutOfRange, "$.events.pending[0].entities[0].map");
        Fails("\"values\":[54000000]},", "\"values\":[1,2,3,4,5,6,7,8,9]},", SnapshotDecodeFailure.BudgetExceeded, "$.events.pending[0].values");
        Fails("}],\"values\":[54000000]},", "}]},", SnapshotDecodeFailure.MissingField, "$.events.pending[0].values");
        Fails("\"pending\":[", "\"x\":0,\"pending\":[", SnapshotDecodeFailure.UnknownField, "$.events.x");

        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"registry\":1", "\"registry\":2"));
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"kind\":\"life.npc.fatigue\"", "\"kind\":\"life.npc.unknown\""));
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored(
            "\"kind\":\"life.npc.fatigue\",\"deadlineMs\":54080000,\"entities\":[{\"map\":79,\"entity\":\"oid:wolf\"}]",
            "\"kind\":\"life.hero.fatigue\",\"deadlineMs\":54080000,\"entities\":[]"), "a hero timer without a hero");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"deadlineMs\":54080000", "\"deadlineMs\":54080001"),
            "deadline and scheduling time disagree by more than the delay");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"deadlineMs\":54080000,\"entities\":[{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000000]",
            "\"deadlineMs\":54080001,\"entities\":[{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000001]"), "set after the saved clock");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("{\"map\":79,\"entity\":\"oid:wolf\"}],\"values\":[54000000]},",
            "{\"map\":79,\"entity\":\"oid:fox\"}],\"values\":[54000000]},"), "an NPC without a life record");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"sequence\":2", "\"sequence\":1"));
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"nextSequence\":3", "\"nextSequence\":2"));
        Assert.AreEqual(GameSessionFailure.InvalidEvents, Restored("\"values\":[54000000]},", "\"values\":[]},"));
        Assert.AreEqual(GameSessionFailure.None, Restored("\"nextSequence\":3", "\"nextSequence\":9"));
    }
}
