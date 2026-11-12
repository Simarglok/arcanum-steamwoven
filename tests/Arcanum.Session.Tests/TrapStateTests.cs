using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-076: the trap state owner (ledger TT1–TT9). Entities, scripts and flags are invented.
[TestClass]
public sealed class TrapStateTests
{
    private static readonly SessionTrapped Chest = new(Exterior, "oid:chest"), Plate = new(Exterior, "oid:plate");
    // An auto-removing nonmagical native trap on a chest, and a magical one on a trap object with a 5×5 square.
    private static readonly SessionTrapBaseline TrappedChest = new(TrapCarrier.TrappedObject, 30001,
        TrapRules.NonmagicalFlag | TrapRules.AutoRemoveFlag, 0, 0);
    private static readonly SessionTrapBaseline TrapPlate = new(TrapCarrier.TrapObject, 30000,
        TrapRules.MagicalFlag | TrapRules.AutoRemoveFlag | TrapRules.SquareTwoFlag, 0x05000000, TrapRules.DontDrawFlag);

    [TestMethod]
    public void An_untouched_trap_is_its_baseline()
    {
        using var owner = Create();
        Assert.AreEqual(new SessionTrapState(TrapClass.Nonmagical, 30001, 0, false), owner.QueryTrap(Chest, TrappedChest));
        Assert.IsFalse(owner.TrapKnownTo(Chest, TrappedChest, TrapObserver.Player));
        Assert.IsTrue(owner.TrapKnownTo(Chest, TrappedChest, TrapObserver.FreeNpc));
        Assert.AreEqual(TrapSpotGate.Roll, owner.TrapSpotGate(Chest, TrappedChest, TrapObserver.Player, 0));
        Assert.AreEqual(TrapSpotGate.NoTrap, owner.TrapSpotGate(Chest, TrappedChest with { HeaderFlags = 0 }, TrapObserver.Player, 0));
        Assert.AreEqual(0, owner.QueryMapChanges(Exterior).Count);
    }

    [TestMethod]
    public void A_spot_mark_is_recorded_once_and_only_for_the_pc_side()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.FreeNpc).Disposition);
        Changed(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.LedNpc), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.Player).Disposition);
        // The trap object turns drawn.
        Assert.AreEqual(new SessionTrapState(TrapClass.Magical, 30000, TrapRules.SpottedFlag, false), owner.QueryTrap(Plate, TrapPlate));
        Assert.IsTrue(owner.TrapKnownTo(Plate, TrapPlate, TrapObserver.Player));
        Assert.AreEqual(TrapSpotGate.Known, owner.TrapSpotGate(Plate, TrapPlate, TrapObserver.Player, 0));
        Assert.AreEqual(1L, owner.QueryMapChanges(Exterior).Single(c => c.Key.Property == GameSessionOwner.TrapStateProperty).Value);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest with { Script = 0 }, TrapObserver.Player).Disposition);
    }

    [TestMethod]
    public void A_use_that_spots_the_trap_is_cancelled_and_a_known_trap_fires_and_removes_itself()
    {
        using var owner = Create();
        var spotted = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual(SessionTrapOutcome.Spotted, spotted.Outcome);
        Changed(spotted.Mutation, owner, 1);
        Assert.IsTrue(spotted.State.Spotted);
        // Known now, so a spot attempt is not read: the trap fires and its auto-removal clears it.
        var fired = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual(SessionTrapOutcome.Fired, fired.Outcome);
        Changed(fired.Mutation, owner, 2);
        Assert.AreEqual(new NativeTrapRun(30001, NativeTrapEffect.Attack, 0, true, 3, 4, 5), fired.Run);
        Assert.AreEqual(new SessionTrapState(TrapClass.None, 0, TrapRules.SpottedFlag, true), fired.State);
        var after = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: false);
        Assert.AreEqual((SessionTrapOutcome.NoTrap, GameSessionDisposition.NoChange), (after.Outcome, after.Mutation.Disposition));
        // A chest is not destroyed by its trap.
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObjectCondition(Exterior, Chest.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Container));
    }

    [TestMethod]
    public void A_free_npc_that_spots_a_pc_trap_cancels_its_use_but_marks_nothing()
    {
        using var owner = Create();
        var placed = TrappedChest with { ObjectFlags = TrapRules.PlacedByPcFlag };
        var result = owner.TriggerTrap(owner.Version, Chest, placed, TrapObserver.FreeNpc, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual((SessionTrapOutcome.Spotted, GameSessionDisposition.NoChange), (result.Outcome, result.Mutation.Disposition));
        Assert.IsFalse(result.State.Spotted);
    }

    [TestMethod]
    public void A_step_fires_a_trap_object_unless_the_mover_floats_and_its_removal_destroys_the_object()
    {
        using var owner = Create();
        var over = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Step, false, moverSpellFlags: 0x2);
        Assert.AreEqual((SessionTrapOutcome.SteppedOver, GameSessionDisposition.NoChange), (over.Outcome, over.Mutation.Disposition));
        var fired = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Step, false, moverSpellFlags: 0x1);
        Assert.AreEqual(SessionTrapOutcome.Fired, fired.Outcome);
        Changed(fired.Mutation, owner, 1);
        Assert.AreEqual(new NativeTrapRun(30000, NativeTrapEffect.Spell, 2, true, 0, 1, 2), fired.Run);
        Assert.IsTrue(fired.State.Removed);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObjectCondition(Exterior, Plate.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Trap));
    }

    [TestMethod]
    public void An_attack_fires_the_trap_without_a_spot_and_never_removes_it()
    {
        using var owner = Create();
        var fired = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Attack, spottedNow: true);
        Assert.AreEqual((SessionTrapOutcome.Fired, GameSessionDisposition.NoChange), (fired.Outcome, fired.Mutation.Disposition));
        Assert.IsNotNull(fired.Run);
        Assert.IsTrue(owner.QueryTrap(Chest, TrappedChest).Trapped);
    }

    [TestMethod]
    public void An_ordinary_trap_script_fires_for_the_host_to_run_and_stays()
    {
        using var owner = Create();
        var ordinary = TrappedChest with { Script = 1234 };
        var fired = owner.TriggerTrap(owner.Version, Chest, ordinary, TrapObserver.Player, SessionTrapCause.Use, spottedNow: false);
        Assert.AreEqual((SessionTrapOutcome.Fired, GameSessionDisposition.NoChange, (NativeTrapRun?)null),
            (fired.Outcome, fired.Mutation.Disposition, fired.Run));
    }

    [TestMethod]
    public void Disarming_removes_a_known_trap_yields_the_device_or_springs_it()
    {
        using var owner = Create();
        var unknown = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, true, 3);
        Assert.AreEqual((TrapDisarm.NotKnown, GameSessionDisposition.NoChange), (unknown.Outcome, unknown.Mutation.Disposition));
        Changed(owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest, TrapObserver.Player), owner, 1);
        var failed = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, false, false, 3);
        Assert.AreEqual((TrapDisarm.Failed, GameSessionDisposition.NoChange), (failed.Outcome, failed.Mutation.Disposition));
        var device = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, true, 2);
        Assert.AreEqual((TrapDisarm.RemovedWithDevice, 6047), (device.Outcome, device.DevicePrototype));
        Changed(device.Mutation, owner, 2);
        Assert.IsTrue(device.State.Removed);
        Assert.AreEqual(TrapDisarm.NotTrapped, owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, false, 3).Outcome);

        // A critical failure springs the trap as a use: an auto-removing one clears.
        using var second = Create();
        Changed(second.MarkTrapSpotted(second.Version, Plate, TrapPlate, TrapObserver.Player), second, 1);
        var sprung = second.DisarmTrap(second.Version, Plate, TrapPlate, TrapObserver.Player, false, true, 0);
        Assert.AreEqual(TrapDisarm.Sprung, sprung.Outcome);
        Changed(sprung.Mutation, second, 2);
        Assert.AreEqual(NativeTrapEffect.Spell, sprung.Run!.Value.Effect);
        Assert.IsTrue(sprung.State.Removed);
    }

    [TestMethod]
    public void The_state_survives_a_snapshot_and_bad_input_is_refused()
    {
        using var owner = Create();
        Changed(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.Player), owner, 1);
        Changed(owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, false).Mutation, owner, 2);
        using var restored = Restore(owner.Capture());
        Assert.AreEqual(owner.QueryTrap(Plate, TrapPlate), restored.QueryTrap(Plate, TrapPlate));
        Assert.AreEqual(owner.QueryTrap(Chest, TrappedChest), restored.QueryTrap(Chest, TrappedChest));
        Assert.IsTrue(restored.QueryTrap(Chest, TrappedChest).Removed);

        Rejected(owner.MarkTrapSpotted(owner.Version, new(Exterior, "bad id"), TrapPlate, TrapObserver.Player), owner, GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate with { Script = -1 }, TrapObserver.Player), owner, GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, (SessionTrapCause)9, false).Mutation, owner,
            GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.DisarmTrap(owner.Version, Plate, TrapPlate with { Carrier = (TrapCarrier)0 }, TrapObserver.Player, true, false, 0).Mutation,
            owner, GameSessionFailure.InvalidTrap, 2);
        var stale = new GameSessionVersion(owner.Identity, 1);
        Rejected(owner.TriggerTrap(stale, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Use, false).Mutation, owner, GameSessionFailure.StaleVersion, 2);
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryTrap(new(Exterior, ""), TrapPlate));
    }

    [TestMethod]
    public void The_player_line_comes_with_the_players_spot_and_a_non_critter_spots_nothing()
    {
        using var owner = Create();
        var mine = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: true);
        Assert.IsTrue(mine.PlayerLine);
        using var other = Create();
        var led = other.TriggerTrap(other.Version, Chest, TrappedChest, TrapObserver.LedNpc, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual((SessionTrapOutcome.Spotted, false), (led.Outcome, led.PlayerLine));
        using var third = Create();
        var thing = third.TriggerTrap(third.Version, Chest, TrappedChest, TrapObserver.NotCritter, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual(SessionTrapOutcome.Fired, thing.Outcome);
        // The player's Detect Traps flag needs no roll: the caller passes spotted.
        Assert.AreEqual(TrapSpotGate.Detected, third.TrapSpotGate(Plate, TrapPlate, TrapObserver.Player, TrapRules.DetectTrapsSpellFlag));
    }

    [TestMethod]
    public void An_attack_never_destroys_a_trap_object_and_a_step_names_a_trap_object_only()
    {
        using var owner = Create();
        var fired = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Attack, false);
        Assert.AreEqual((SessionTrapOutcome.Fired, GameSessionDisposition.NoChange), (fired.Outcome, fired.Mutation.Disposition));
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObjectCondition(Exterior, Plate.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Trap));
        Rejected(owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Step, false).Mutation, owner,
            GameSessionFailure.InvalidTrap, 0);
        Rejected(owner.TriggerTrap(owner.Version, Plate, TrapPlate, (TrapObserver)9, SessionTrapCause.Use, false).Mutation, owner,
            GameSessionFailure.InvalidTrap, 0);
        Rejected(owner.DisarmTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, true, false, 4).Mutation, owner, GameSessionFailure.InvalidTrap, 0);
    }

    [TestMethod]
    public void Disarming_a_trap_object_destroys_it_and_a_non_expert_critical_success_yields_nothing()
    {
        using var owner = Create();
        Changed(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.Player), owner, 1);
        var removed = owner.DisarmTrap(owner.Version, Plate, TrapPlate with { Script = 30002 }, TrapObserver.Player, true, true, 1);
        Assert.AreEqual((TrapDisarm.Removed, 0), (removed.Outcome, removed.DevicePrototype));
        Changed(removed.Mutation, owner, 2);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObjectCondition(Exterior, Plate.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Trap));
        // The spotted flag stays through the removal (TT5); a trap object stays drawn.
        Assert.AreEqual(new SessionTrapState(TrapClass.None, 0, TrapRules.SpottedFlag, true), removed.State);
    }

    [TestMethod]
    public void A_trap_armed_again_after_a_removal_is_seen_and_keeps_the_spotted_mark()
    {
        using var owner = Create();
        Changed(owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest, TrapObserver.Player), owner, 1);
        Changed(owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, false, 3).Mutation, owner, 2);
        Assert.IsTrue(owner.QueryTrap(Chest, TrappedChest).Removed);
        // The host's baseline now names another trap script (a trap device armed on the emptied slot).
        var armed = TrappedChest with { Script = 30003 };
        Assert.AreEqual(new SessionTrapState(TrapClass.Nonmagical, 30003, TrapRules.SpottedFlag, false), owner.QueryTrap(Chest, armed));
        Assert.AreEqual(SessionTrapOutcome.Fired, owner.TriggerTrap(owner.Version, Chest, armed, TrapObserver.Player, SessionTrapCause.Use, false).Outcome);
    }

    [TestMethod]
    public void A_refused_write_reports_nothing_done()
    {
        using var owner = Create(new GameSessionLimits(maxChangesPerMap: 1));
        var disarm = owner.DisarmTrap(owner.Version, Plate, TrapPlate with { ObjectFlags = TrapRules.SpottedFlag }, TrapObserver.Player, true, true, 3);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (disarm.Mutation.Disposition, disarm.Mutation.Failure));
        Assert.AreEqual((TrapDisarm.NotTrapped, 0, (NativeTrapRun?)null), (disarm.Outcome, disarm.DevicePrototype, disarm.Run));
        var step = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Step, false);
        Assert.AreEqual((GameSessionDisposition.Rejected, SessionTrapOutcome.NoTrap, (NativeTrapRun?)null), (step.Mutation.Disposition, step.Outcome, step.Run));
        Assert.IsTrue(owner.QueryTrap(Plate, TrapPlate).Trapped);
    }

    // ARC-MAGIC-057 (ledger SP1): the hero's spot trap roll. Perception and the skill's points and training are invented.
    private static GameSessionOwner Spotter(int points, SkillTraining training, ulong seed = 0)
    {
        var stats = Defaults();
        for (var i = 0; i < stats.Length; i++) stats[i] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.SpotTrap] = points | (int)training << 6;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    [TestMethod]
    public void A_spot_roll_is_made_only_when_the_gate_asks_for_it()
    {
        using var owner = Spotter(5, SkillTraining.Master);
        var draws = owner.QueryRandom().Draws;
        var revision = owner.Version.Revision;
        // The detect flag, a known trap and an untrapped object need no roll: nothing is drawn.
        foreach (var (baseline, flags) in new[] { (TrappedChest, TrapRules.DetectTrapsSpellFlag), (TrappedChest with { Script = 0 }, 0),
            (TrappedChest with { ObjectFlags = TrapRules.SpottedFlag }, 0) })
        {
            var skipped = owner.RollHeroSpotTrap(owner.Version, Chest, baseline, flags);
            Assert.AreEqual((GameSessionDisposition.NoChange, false), (skipped.Disposition, skipped.Spotted));
        }
        Assert.AreEqual((draws, revision), (owner.QueryRandom().Draws, owner.Version.Revision));
        var rolled = owner.RollHeroSpotTrap(owner.Version, Chest, TrappedChest);
        Assert.AreEqual(GameSessionDisposition.Changed, rolled.Disposition);
        Assert.IsNotNull(rolled.Check);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision per roll");
        Assert.IsTrue(owner.QueryRandom().Draws >= draws + 2, "the check's two draws, and a master's second roll");
        Assert.IsFalse(owner.QueryTrap(Chest, TrappedChest).Spotted, "the roll marks nothing: the caller does");
    }

    [TestMethod]
    public void A_master_with_a_high_perception_spots_a_trap_and_an_unskilled_hero_mostly_does_not()
    {
        var master = 0;
        var untrained = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var strong = Spotter(5, SkillTraining.Master, seed);
            using var weak = Spotter(0, SkillTraining.None, seed);
            var a = strong.RollHeroSpotTrap(strong.Version, Chest, TrappedChest);
            var b = weak.RollHeroSpotTrap(weak.Version, Chest, TrappedChest);
            master += a.Spotted ? 1 : 0;
            untrained += b.Spotted ? 1 : 0;
            Assert.IsFalse(b.SecondRoll, "no second roll below master");
        }
        Assert.IsTrue(master > 150, "a master's two rolls: " + master);
        Assert.IsTrue(untrained < master, $"{untrained} against {master}");
    }

    [TestMethod]
    public void A_magical_trap_is_spotted_only_from_expert_on()
    {
        var apprentice = 0;
        var expert = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var low = Spotter(5, SkillTraining.Apprentice, seed);
            using var high = Spotter(5, SkillTraining.Expert, seed);
            apprentice += low.RollHeroSpotTrap(low.Version, Plate, TrapPlate).Spotted ? 1 : 0;
            expert += high.RollHeroSpotTrap(high.Version, Plate, TrapPlate).Spotted ? 1 : 0;
        }
        Assert.AreEqual(0, apprentice);
        Assert.IsTrue(expert > 0, "an expert spots some");
    }

    [TestMethod]
    public void A_spot_roll_refuses_without_a_hero_for_an_invalid_object_or_light_and_a_stale_version()
    {
        using var heroless = Create();
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, heroless.RollHeroSpotTrap(heroless.Version, Chest, TrappedChest).Failure);
        using var owner = Spotter(5, SkillTraining.Expert);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.RollHeroSpotTrap(owner.Version, Chest with { Entity = "" }, TrappedChest).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.RollHeroSpotTrap(owner.Version, Chest, TrappedChest, light: -1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RollHeroSpotTrap(new(owner.Version.Owner, owner.Version.Revision + 1), Chest, TrappedChest).Failure);
        Assert.AreEqual(0, owner.QueryRandom().Draws, "a refusal draws nothing");
    }

    // ARC-SKILLS-1156 (ledger SP3): the hero's disarm traps press. Dexterity 20 (the skill's key stats), the skill's points and training invented.
    private static GameSessionOwner Disarmer(int points, SkillTraining training, ulong seed = 0, GameSessionLimits? limits = null)
    {
        var stats = Defaults();
        for (var i = 0; i < stats.Length; i++) stats[i] = 20;
        int[] tech = new int[4];
        tech[SkillRules.FamilyIndex(Skill.DisarmTraps)] = points | (int)training << 6;
        var hero = HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { TechSkills = [.. tech] } };
        var created = GameSessionOwner.Create(Descriptor(limits: limits) with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    private static readonly SessionTrapBaseline KnownChest = TrappedChest with { ObjectFlags = TrapRules.SpottedFlag };
    private static readonly SessionTrapBaseline KnownPlate = TrapPlate with { ObjectFlags = TrapRules.SpottedFlag };

    [TestMethod]
    public void A_disarm_press_on_a_trap_the_hero_does_not_know_or_that_is_gone_rolls_and_charges_nothing()
    {
        using var owner = Disarmer(5, SkillTraining.Master);
        var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
        var unknown = owner.RollHeroDisarmTrap(owner.Version, Chest, TrappedChest);
        Assert.AreEqual((GameSessionDisposition.NoChange, TrapDisarm.NotKnown), (unknown.Disposition, unknown.Disarm!.Outcome));
        var none = owner.RollHeroDisarmTrap(owner.Version, Chest, KnownChest with { Script = 0 });
        Assert.AreEqual((GameSessionDisposition.NoChange, TrapDisarm.NotTrapped), (none.Disposition, none.Disarm!.Outcome));
        Assert.IsNull(unknown.Check);
        Assert.AreEqual((draws, revision), (owner.QueryRandom().Draws, owner.Version.Revision));
    }

    [TestMethod]
    public void A_disarm_roll_is_one_revision_and_a_success_removes_the_trap_a_failure_leaves_it()
    {
        var removed = 0;
        var failed = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var owner = Disarmer(5, SkillTraining.Expert, seed);
            var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
            var roll = owner.RollHeroDisarmTrap(owner.Version, Chest, KnownChest);
            Assert.AreEqual(GameSessionDisposition.Changed, roll.Disposition);
            Assert.IsNotNull(roll.Check);
            Assert.IsTrue(owner.QueryRandom().Draws >= draws + 2, "the check's two draws");
            Assert.IsFalse(roll.SecondRoll, "no second roll below master");
            Assert.AreEqual(revision + 1, owner.Version.Revision, "the roll and the disarm are one revision");
            var state = owner.QueryTrap(Chest, KnownChest);
            if (roll.Disarm!.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice) { removed++; Assert.IsTrue(state.Removed); }
            else if (roll.Disarm.Outcome == TrapDisarm.Failed) { failed++; Assert.IsFalse(state.Removed); }
            else { Assert.AreEqual(TrapDisarm.Sprung, roll.Disarm.Outcome); Assert.IsNotNull(roll.Disarm.Run, "a critical failure sets the trap off"); }
        }
        Assert.IsTrue(removed > 0 && failed > 0, $"{removed} removed, {failed} failed");
    }

    [TestMethod]
    public void A_master_rolls_a_second_time_and_disarms_more_often_than_an_expert()
    {
        int master = 0, expert = 0, seconds = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            using var m = Disarmer(2, SkillTraining.Master, seed);
            using var e = Disarmer(2, SkillTraining.Expert, seed);
            var a = m.RollHeroDisarmTrap(m.Version, Chest, KnownChest);
            var b = e.RollHeroDisarmTrap(e.Version, Chest, KnownChest);
            master += a.Disarm!.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice ? 1 : 0;
            expert += b.Disarm!.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice ? 1 : 0;
            seconds += a.SecondRoll ? 1 : 0;
            Assert.IsFalse(b.SecondRoll);
        }
        Assert.IsTrue(seconds > 0, "a failed master rolls the third roll");
        Assert.IsTrue(master > expert, $"{master} against {expert}");
    }

    [TestMethod]
    public void The_trap_objects_difficulty_makes_the_check_harder_and_another_carrier_has_none()
    {
        int easy = 0, hard = 0, chest = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            using var a = Disarmer(4, SkillTraining.Expert, seed);
            using var b = Disarmer(4, SkillTraining.Expert, seed);
            using var c = Disarmer(4, SkillTraining.Expert, seed);
            easy += a.RollHeroDisarmTrap(a.Version, Plate, KnownPlate, 0).Check!.Success ? 1 : 0;
            hard += b.RollHeroDisarmTrap(b.Version, Plate, KnownPlate, 60).Check!.Success ? 1 : 0;
            chest += c.RollHeroDisarmTrap(c.Version, Chest, KnownChest, 60).Check!.Success ? 1 : 0;
        }
        Assert.IsTrue(hard < easy, $"{hard} against {easy}");
        // A trapped chest has no trap difficulty term: the field is read for a trap object only (SP3).
        using var plain = Disarmer(4, SkillTraining.Expert, 7);
        using var withTerm = Disarmer(4, SkillTraining.Expert, 7);
        Assert.AreEqual(plain.RollHeroDisarmTrap(plain.Version, Chest, KnownChest, 0).Check!.Check.Difficulty,
            withTerm.RollHeroDisarmTrap(withTerm.Version, Chest, KnownChest, 60).Check!.Check.Difficulty);
        Assert.IsTrue(chest > hard);
    }

    [TestMethod]
    public void An_experts_critical_success_yields_the_device_a_critical_failure_sets_the_trap_off()
    {
        var device = 0;
        var sprung = 0;
        for (ulong seed = 0; seed < 600; seed++)
        {
            using var owner = Disarmer(5, SkillTraining.Expert, seed);
            var roll = owner.RollHeroDisarmTrap(owner.Version, Chest, KnownChest);
            if (roll.Disarm!.Outcome == TrapDisarm.RemovedWithDevice) { device++; Assert.AreEqual(6047, roll.Disarm.DevicePrototype, "script 30001's device"); Assert.IsTrue(roll.Check!.Critical); }
            if (roll.Disarm.Outcome == TrapDisarm.Sprung) { sprung++; Assert.IsTrue(roll.Check!.Critical && !roll.Check.Success); Assert.IsNotNull(roll.Disarm.Run); }
        }
        Assert.IsTrue(device > 0 && sprung > 0, $"{device} devices, {sprung} sprung");
    }

    [TestMethod]
    public void A_disarm_press_refuses_without_a_hero_for_an_invalid_object_difficulty_or_light_and_a_stale_version()
    {
        using var heroless = Create();
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, heroless.RollHeroDisarmTrap(heroless.Version, Chest, KnownChest).Failure);
        using var owner = Disarmer(5, SkillTraining.Expert);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.RollHeroDisarmTrap(owner.Version, Chest with { Entity = "" }, KnownChest).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.RollHeroDisarmTrap(owner.Version, Plate, KnownPlate, GameSessionOwner.MaxTrapDifficulty + 1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.RollHeroDisarmTrap(owner.Version, Chest, KnownChest, 0, -1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RollHeroDisarmTrap(new(owner.Version.Owner, owner.Version.Revision + 1), Chest, KnownChest).Failure);
        Assert.AreEqual((0, 0L), (owner.QueryRandom().Draws, owner.Version.Revision), "a refusal draws nothing");
    }

    [TestMethod]
    public void A_removal_the_session_cannot_write_takes_the_rolls_back()
    {
        var refused = 0;
        for (ulong seed = 0; seed < 100; seed++)
        {
            using var owner = Disarmer(5, SkillTraining.Master, seed, new GameSessionLimits(maxChangesPerMap: 1));
            var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
            // A trap object's removal records two changes: over the budget of one, it is refused as a whole.
            var roll = owner.RollHeroDisarmTrap(owner.Version, Plate, KnownPlate);
            if (roll.Disposition != GameSessionDisposition.Rejected) continue;
            refused++;
            Assert.AreEqual(GameSessionFailure.BudgetExceeded, roll.Failure);
            Assert.AreEqual((draws, revision), (owner.QueryRandom().Draws, owner.Version.Revision), "nothing happened");
            Assert.IsTrue(owner.QueryTrap(Plate, KnownPlate).Trapped);
        }
        Assert.IsTrue(refused > 0);
    }

    [TestMethod]
    public void In_a_round_the_press_pays_four_action_points_first_and_another_subturn_refuses_it()
    {
        using var owner = Disarmer(5, SkillTraining.Expert);
        var world = new RoundWorld();
        Assert.AreEqual(GameSessionFailure.None, owner.StartCombatRound(owner.Version, world).Failure);
        for (var i = 0; i < 4 && owner.QueryCombatRound()!.Actor != SessionCombatant.Hero; i++)
            owner.EndCombatTurn(owner.Version, owner.QueryCombatRound()!.Actor, world);
        var points = owner.QueryCombatRound()!.ActionPoints;
        var roll = owner.RollHeroDisarmTrap(owner.Version, Chest, KnownChest, world: world);
        Assert.AreEqual((SessionActionPointUse.Paid, points - TurnBasedRules.UseSkillCost, GameSessionDisposition.Changed),
            (roll.Charge!.Charge, owner.QueryCombatRound()!.ActionPoints, roll.Disposition));
        owner.EndCombatTurn(owner.Version, SessionCombatant.Hero, world);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var outOfTurn = owner.RollHeroDisarmTrap(owner.Version, Plate, KnownPlate, world: world);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.DisarmOutOfTurn, revision, draws),
            (outOfTurn.Disposition, outOfTurn.Failure, owner.Version.Revision, owner.QueryRandom().Draws));
    }

    private sealed class RoundWorld : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
            [new(SessionCombatant.Hero, new(0, 0)), new(SessionCombatant.Critter("oid:wolf"), new(105, 200))];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => new() { Speed = 7, Life = new LifeFacts() };
    }

    private static readonly SessionInsertCheck AcceptAll = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind DeviceKind = new(SessionItemType.Generic, 6047, 3000, 33000);

    [TestMethod]
    public void A_created_device_enters_the_heros_pack_or_lies_on_its_tile_when_the_hero_cannot_take_it()
    {
        using var owner = Disarmer(5, SkillTraining.Expert);
        var revision = owner.Version.Revision;
        var taken = owner.CreateItemForHero(owner.Version, DeviceKind, AcceptAll);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionInsertVerdict.Accepted, revision + 1), (taken.Disposition, taken.Verdict, owner.Version.Revision));
        Assert.AreEqual(SessionObjectPlace.HeroPack, owner.QueryObject(taken.Item!)!.Place, "in the hero's list");
        Assert.AreEqual(6047, owner.QueryMadeItems().Single(item => item.Entity == taken.Item).Kind.Prototype);
        var refused = owner.CreateItemForHero(owner.Version, DeviceKind, _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionInsertVerdict.TooHeavy), (refused.Disposition, refused.Verdict));
                Assert.AreEqual((SessionObjectPlace.Ground, owner.QueryActor().Cell), (owner.QueryObject(refused.Item!)!.Place, owner.QueryObject(refused.Item!)!.Cell), "left on the hero's tile");
    }

    [TestMethod]
    public void A_created_device_refuses_ammunition_a_missing_hero_and_an_old_version()
    {
        using var owner = Disarmer(5, SkillTraining.Expert);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.CreateItemForHero(owner.Version, new(SessionItemType.Ammo, 7059, 1, 2), AcceptAll).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.CreateItemForHero(new(owner.Version.Owner, owner.Version.Revision + 1), DeviceKind, AcceptAll).Failure);
        using var heroless = Create();
        Assert.AreEqual(GameSessionFailure.InvalidItem, heroless.CreateItemForHero(heroless.Version, DeviceKind, AcceptAll).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
    }
}
