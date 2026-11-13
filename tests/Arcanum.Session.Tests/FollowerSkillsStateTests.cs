using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1776 (Follower Skills slice 1, ledger PL1-PL5, SP3, TT2, SC12, SC14): an NPC, a follower or not, as the user of a pick-locks and of a disarm-traps check.
// The NPC "virgil", the door, the chest and the lockpicks are invented.
[TestClass]
public sealed class FollowerSkillsStateTests
{
    private const string Npc = "virgil", OwnPicks = "oid:picks";
    private static readonly SessionOpenable Door = new(Exterior, "door:1");
    private static readonly SessionOpenableBaseline LockedDoor = new(OpenableKind.Portal, LockRules.LockedBit, 0, 0);
    private static readonly SessionTrapped Chest = new(Exterior, "oid:chest"), Plate = new(Exterior, "oid:plate");
    private static readonly SessionTrapBaseline TrappedChest = new(TrapCarrier.TrappedObject, 30001, TrapRules.NonmagicalFlag | TrapRules.AutoRemoveFlag, 0, 0);
    private static readonly SessionTrapBaseline KnownChest = TrappedChest with { ObjectFlags = TrapRules.SpottedFlag };
    private static readonly SessionTrapBaseline TrapPlate = new(TrapCarrier.TrapObject, 30000,
        TrapRules.MagicalFlag | TrapRules.AutoRemoveFlag | TrapRules.SquareTwoFlag, 0x05000000, TrapRules.DontDrawFlag);
    private static readonly SessionTrapBaseline KnownPlate = TrapPlate with { ObjectFlags = TrapRules.SpottedFlag };
    private static readonly ItemCondition Sound = new(10, 0, 0, false);

    private static SessionLockpick Lockpick(int bonus = 0, uint flags = SessionLockpick.LockpickFlag, int[]? scripts = null, string entity = OwnPicks) =>
        new(entity, flags, bonus, Sound, scripts ?? []);

    private static SessionPickLockTarget Target(int difficulty = 0, int light = SessionPickLockTarget.FullLight) => new(Door, LockedDoor, difficulty, light);

    // The NPC's sheet: every primary stat 20, the pick-locks and disarm-traps points with their training as asked.
    private static SessionNpcSkillUser User(int points, SkillTraining training, bool led = true, string entity = Npc, LifeState? state = null)
    {
        int[] tech = new int[4];
        tech[SkillRules.FamilyIndex(Skill.PickLocks)] = points | (int)training << 6;
        tech[SkillRules.FamilyIndex(Skill.DisarmTraps)] = points | (int)training << 6;
        var sheet = new SessionNpcAdvancement(entity, new(CharacterRace.Human, false, [20, 20, 20, 20, 20, 20, 20, 20], 0, 0, 1, 0,
            [.. new int[12]], [.. tech], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0);
        return new(SessionLifeTarget.Critter(entity, new LifeFacts { HitPointPoints = 10 }, state ?? new LifeState(0, 0, 0, LifeConditions.None, 0)), sheet, AdvancementModifiers.None, led);
    }

    // A session with a hero whose own skills are high (a master of both, to tell the NPC's own level from it) and the game difficulty asked for.
    private static GameSessionOwner Owner(ulong seed = 0, GameDifficulty difficulty = GameDifficulty.Normal, GameSessionLimits? limits = null, bool hero = true, int heroPoints = 20)
    {
        var stats = Defaults();
        for (var i = 0; i < stats.Length; i++) stats[i] = 20;
        int[] tech = new int[4];
        tech[SkillRules.FamilyIndex(Skill.PickLocks)] = heroPoints | (int)SkillTraining.Master << 6;
        tech[SkillRules.FamilyIndex(Skill.DisarmTraps)] = heroPoints | (int)SkillTraining.Master << 6;
        var heroDefinition = HalfElf(stats) with
        {
            Kit = [.. Kit, new(new(SessionItemType.Generic, 15200, 6000, 6001))],
            Advancement = SessionHeroAdvancement.None with { TechSkills = [.. tech] },
        };
        var descriptor = Descriptor(limits) with { RandomSeed = seed, Difficulty = difficulty };
        if (hero) descriptor = descriptor with { Hero = heroDefinition };
        var created = GameSessionOwner.Create(descriptor);
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    [TestMethod]
    public void An_npc_unlocks_a_locked_door_with_its_own_lockpicks_in_one_revision_and_the_door_relocks_an_hour_later()
    {
        using var owner = Owner();
        var (before, draws, hero) = (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryHero());
        var result = owner.PickLockByNpc(owner.Version, User(20, SkillTraining.Master), Target(difficulty: 20), Lockpick(bonus: 30));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.IsTrue(result.Check!.Success);
        Assert.IsNull(result.Charge, "an NPC's pick charges no action points of the hero");
        Assert.AreEqual(new PickLockOutcome(PickLockResult.Unlocked, true, false), result.Outcome);
        Assert.AreEqual((false, before + 1, draws + 2), (result.State!.Value.Locked, owner.Version.Revision, owner.QueryRandom().Draws));
        Assert.AreEqual(hero, owner.QueryHero(), "the hero is untouched (no fate, no condition)");
        var relock = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.LockReset);
        Assert.AreEqual(LockRules.UnlockResetDelayMs, relock.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs);
    }

    [TestMethod]
    public void The_check_is_the_npcs_own_skill_without_the_local_pc_difficulty_term_and_with_no_fate()
    {
        // Two stored points are level 8, so pick locks 6 x 8 + 10 for the extraordinary intelligence (stat 20) = 58 (SC3). Easy adds half to the hero's effectiveness (SC2); an NPC has no such term.
        using var owner = Owner(difficulty: GameDifficulty.Easy, heroPoints: 2);
        var npc = owner.PickLockByNpc(owner.Version, User(2, SkillTraining.None), Target(), null);
        Assert.AreEqual(58, npc.Check!.Check.Effectiveness, "the NPC's own level, no local-PC term");
        Assert.IsFalse(npc.FateUsed);
        using var other = Owner(difficulty: GameDifficulty.Easy, heroPoints: 2);
        var own = other.PickLock(other.Version, Target(), null);
        Assert.AreEqual(58 + 29, own.Check!.Check.Effectiveness, "the hero's reads the easy term");
    }

    [TestMethod]
    public void The_lockpicks_bonus_lowers_the_difficulty_and_none_is_allowed_for_an_npc()
    {
        using var owner = Owner();
        var with = owner.PickLockByNpc(owner.Version, User(5, SkillTraining.None), Target(difficulty: 40), Lockpick(bonus: 25));
        var without = owner.PickLockByNpc(owner.Version, User(5, SkillTraining.None), Target(difficulty: 40), null);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionDisposition.Changed), (with.Disposition, without.Disposition), "a follower, the AI or a script picks without lockpicks (PL4)");
        Assert.AreEqual(without.Check!.Check.Difficulty - 25, with.Check!.Check.Difficulty, "the lockpicks' bonus as stored");
    }

    [TestMethod]
    public void An_untrained_npcs_critical_failure_jams_the_lock_for_a_day()
    {
        for (ulong seed = 0; seed < 400; seed++)
        {
            using var owner = Owner(seed);
            var result = owner.PickLockByNpc(owner.Version, User(0, SkillTraining.None), Target(), Lockpick());
            Assert.IsFalse(result.Check!.Success);
            if (!result.Check.Critical) continue;
            Assert.AreEqual(PickLockResult.Jammed, result.Outcome!.Value.Result);
            Assert.AreEqual(LockRules.JammedBit, result.State!.Value.Flags & LockRules.JammedBit);
            var relock = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.LockReset);
            Assert.AreEqual(LockRules.JamResetDelayMs, relock.DeadlineMs - owner.QueryClockSnapshot().GameTimestampMs);
            return;
        }
        Assert.Fail("no critical failure in 400 seeds");
    }

    [TestMethod]
    public void A_pick_works_in_a_session_without_a_hero()
    {
        using var owner = Owner(hero: false);
        var result = owner.PickLockByNpc(owner.Version, User(20, SkillTraining.Master), Target(), null);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, $"{result.Failure}");
        Assert.IsTrue(result.Check!.Success);
    }

    [TestMethod]
    public void A_pick_is_refused_before_anything_is_rolled()
    {
        using var owner = Owner();
        void Refused(SessionNpcSkillUser user, SessionPickLockTarget target, SessionLockpick? lockpick, GameSessionFailure failure, GameSessionVersion? version = null)
        {
            var result = owner.PickLockByNpc(version ?? owner.Version, user, target, lockpick);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0, 0L), (owner.QueryRandom().Draws, owner.Version.Revision), "a refusal draws nothing and writes nothing");
        }
        var good = User(10, SkillTraining.Expert);
        Refused(good with { User = SessionLifeTarget.Hero }, Target(), null, GameSessionFailure.PickLockUnavailable);
        Refused(good with { Loaded = good.Loaded with { Entity = "oid:other" } }, Target(), null, GameSessionFailure.PickLockUnavailable);
        Refused(User(10, SkillTraining.Expert, state: new LifeState(1000, 0, 0, LifeConditions.None, 0)), Target(), null, GameSessionFailure.PickLockUnavailable);
        Refused(good, Target(difficulty: GameSessionOwner.MaxLockDifficulty + 1), null, GameSessionFailure.InvalidOpenable);
        Refused(good, Target(light: -1), null, GameSessionFailure.InvalidOpenable);
        Refused(good, Target(), Lockpick(flags: 0), GameSessionFailure.InvalidLockpick);
        Refused(good, Target(), Lockpick(bonus: GameSessionOwner.MaxLockpickBonus + 1), GameSessionFailure.InvalidLockpick);
        Refused(good, Target(), Lockpick(scripts: [SessionLockpick.UseScript]), GameSessionFailure.ItemScriptUnsupported);
        Refused(good, Target(), Lockpick(entity: owner.QueryHeroKit().Last().Entity), GameSessionFailure.ItemNotAvailable, null);
        Refused(good, Target(), null, GameSessionFailure.StaleVersion, new(owner.Version.Owner, owner.Version.Revision + 1));
    }

    [TestMethod]
    public void A_led_npc_disarms_a_trap_the_pc_spotted_in_one_revision_and_a_success_removes_it()
    {
        var removed = 0;
        var failed = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var owner = Owner(seed);
            var (draws, revision, hero) = (owner.QueryRandom().Draws, owner.Version.Revision, owner.QueryHero());
            var roll = owner.RollNpcDisarmTrap(owner.Version, User(5, SkillTraining.Expert), Chest, KnownChest);
            Assert.AreEqual(GameSessionDisposition.Changed, roll.Disposition);
            Assert.IsNull(roll.Charge, "an NPC's disarm charges no action points here");
            Assert.IsFalse(roll.Fated);
            Assert.IsTrue(owner.QueryRandom().Draws >= draws + 2, "the check's two draws");
            Assert.AreEqual(revision + 1, owner.Version.Revision, "the roll and the disarm are one revision");
            Assert.AreEqual(hero, owner.QueryHero());
            var state = owner.QueryTrap(Chest, KnownChest);
            if (roll.Disarm!.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice) { removed++; Assert.IsTrue(state.Removed); }
            else if (roll.Disarm.Outcome == TrapDisarm.Failed) { failed++; Assert.IsFalse(state.Removed); }
            else Assert.AreEqual(TrapDisarm.Sprung, roll.Disarm.Outcome);
        }
        Assert.IsTrue(removed > 0 && failed > 0, $"{removed} removed, {failed} failed");
    }

    [TestMethod]
    public void The_observer_rules_of_an_npc_decide_which_trap_it_knows()
    {
        using var owner = Owner();
        var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
        // A led NPC knows what the PC spotted or placed (TT2): an untouched trap is not known to it, nothing is rolled or charged.
        var led = owner.RollNpcDisarmTrap(owner.Version, User(5, SkillTraining.Master), Chest, TrappedChest);
        Assert.AreEqual((GameSessionDisposition.NoChange, TrapDisarm.NotKnown), (led.Disposition, led.Disarm!.Outcome));
        Assert.IsNull(led.Check);
        var none = owner.RollNpcDisarmTrap(owner.Version, User(5, SkillTraining.Master), Chest, KnownChest with { Script = 0 });
        Assert.AreEqual((GameSessionDisposition.NoChange, TrapDisarm.NotTrapped), (none.Disposition, none.Disarm!.Outcome));
        Assert.AreEqual((draws, revision), (owner.QueryRandom().Draws, owner.Version.Revision));
        // A free NPC knows every trap the PC did not place.
        var free = owner.RollNpcDisarmTrap(owner.Version, User(5, SkillTraining.Master, led: false), Chest, TrappedChest);
        Assert.AreEqual(GameSessionDisposition.Changed, free.Disposition);
        Assert.IsNotNull(free.Check);
    }

    [TestMethod]
    public void The_disarm_check_is_the_npcs_own_skill_with_no_local_pc_term_and_the_trap_objects_difficulty()
    {
        // Two stored points are level 8, so disarm traps 4 x 8 + 20 + 10 = 62 (SC3); the hero's Easy adds half.
        using var easy = Owner(difficulty: GameDifficulty.Easy, heroPoints: 2);
        var npc = easy.RollNpcDisarmTrap(easy.Version, User(2, SkillTraining.None), Chest, KnownChest);
        Assert.AreEqual(62, npc.Check!.Check.Effectiveness, "disarm traps 4L + 20, no easy term");
        using var other = Owner(difficulty: GameDifficulty.Easy, heroPoints: 2);
        var own = other.RollHeroDisarmTrap(other.Version, Chest, KnownChest);
        Assert.AreEqual(62 + 31, own.Check!.Check.Effectiveness);
        int easyHits = 0, hardHits = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            using var a = Owner(seed);
            using var b = Owner(seed);
            easyHits += a.RollNpcDisarmTrap(a.Version, User(4, SkillTraining.Expert), Plate, KnownPlate, 0).Check!.Success ? 1 : 0;
            hardHits += b.RollNpcDisarmTrap(b.Version, User(4, SkillTraining.Expert), Plate, KnownPlate, 60).Check!.Success ? 1 : 0;
        }
        Assert.IsTrue(hardHits < easyHits, $"{hardHits} against {easyHits}");
    }

    [TestMethod]
    public void A_disarm_is_refused_for_the_hero_a_dead_npc_an_invalid_object_difficulty_or_light_and_a_stale_version()
    {
        using var owner = Owner();
        var good = User(5, SkillTraining.Expert);
        void Refused(GameSessionFailure failure, SessionNpcSkillUser user, SessionTrapped trapped, int difficulty = 0, int light = SessionPickLockTarget.FullLight, GameSessionVersion? version = null) =>
            Assert.AreEqual(failure, owner.RollNpcDisarmTrap(version ?? owner.Version, user, trapped, KnownChest, difficulty, light).Failure);
        Refused(GameSessionFailure.SkillsUnavailable, good with { User = SessionLifeTarget.Hero }, Chest);
        Refused(GameSessionFailure.SkillsUnavailable, User(5, SkillTraining.Expert, state: new LifeState(1000, 0, 0, LifeConditions.None, 0)), Chest);
        Refused(GameSessionFailure.InvalidTrap, good, Chest with { Entity = "" });
        Refused(GameSessionFailure.InvalidTrap, good, Plate, GameSessionOwner.MaxTrapDifficulty + 1);
        Refused(GameSessionFailure.InvalidTrap, good, Chest, 0, -1);
        Refused(GameSessionFailure.StaleVersion, good, Chest, version: new(owner.Version.Owner, owner.Version.Revision + 1));
        Assert.AreEqual((0, 0L), (owner.QueryRandom().Draws, owner.Version.Revision), "a refusal draws nothing");
    }

    [TestMethod]
    public void A_removal_the_session_cannot_write_takes_an_npcs_rolls_back_in_a_session_without_a_hero()
    {
        var refused = 0;
        for (ulong seed = 0; seed < 100; seed++)
        {
            using var owner = Owner(seed, limits: new GameSessionLimits(maxChangesPerMap: 1), hero: false);
            var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
            // A trap object's removal records two changes: over the budget of one, it is refused as a whole.
            var roll = owner.RollNpcDisarmTrap(owner.Version, User(20, SkillTraining.Master), Plate, KnownPlate);
            if (roll.Disposition != GameSessionDisposition.Rejected) continue;
            refused++;
            Assert.AreEqual((draws, revision), (owner.QueryRandom().Draws, owner.Version.Revision), "the rolls went back");
        }
        Assert.IsTrue(refused > 0);
    }
}
