using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-MAGIC-056 (MA-9 of the magic audit ARC-RES-103, ledger XR6, owner decision on XR14 (a)): Tempus Fugit's speed change. While the spell is held, a holder gains 10 speed and every other critter
/// loses 10 (stat getter 0x4B0490); the original tests only the local player's flag, here any critter's flag counts. Spell flag OSF_TEMPUS_FUGIT is bit 0x4000000. The stats are the fixtures'.
/// </summary>
[TestClass]
public sealed class TempusFugitSpeedTests
{
    private const int Flag = 0x4000000;
    private const string Wolf = "oid:wolf", Bear = "oid:bear", Hero = "pc";

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf([8, 15, 8, 8, 8, 8, 8, 8, 10, 0]), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Hold(GameSessionOwner owner, string who, bool on = true) =>
        Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.WriteObjectFlag(owner.Version, who, SessionObjectFlagField.Spell, Flag, on).Disposition);

    private static SessionCritterCombat Npc(GameSessionOwner owner, string entity = Wolf)
    {
        var build = SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: AttackStateTests.Stats(dexterity: 15, constitution: 3)));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return owner.WithNpcEffects(build.Combat!);
    }

    private static int Speed(SessionCritterCombat npc) => npc.EffectiveStat(CharacterStat.Speed, LifeState.Unharmed);

    private static int HeroSpeed(GameSessionOwner owner) => owner.QueryHeroStats(owner.HeroStatContext())!.Value.Snapshot!.GetEffective(CharacterStat.Speed);

    [TestMethod]
    public void The_speed_shift_reaches_the_stat_through_the_context_before_the_effects_and_with_the_crippled_legs()
    {
        using var owner = Owner();
        var plain = owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Speed);
        Assert.IsTrue(plain is > 10 and < 90, "the fixture speed leaves room for the shift");
        int At(int shift, bool legs) => owner.QueryHeroStats(new(0, 0, legs, SpeedShift: shift))!.Value.Snapshot!.GetEffective(CharacterStat.Speed);
        Assert.AreEqual(plain + 10, At(10, false));
        Assert.AreEqual(plain - 10, At(-10, false));
        Assert.AreEqual(plain + 5, At(10, true), "+10 and the crippled legs' -5");
    }

    [TestMethod]
    public void With_the_hero_holding_it_the_hero_gains_ten_and_an_npc_loses_ten_as_in_the_original()
    {
        using var owner = Owner();
        var hero = HeroSpeed(owner);
        var wolf = Speed(Npc(owner));
        Hold(owner, Hero);
        Assert.AreEqual(hero + 10, HeroSpeed(owner));
        Assert.AreEqual(wolf - 10, Speed(Npc(owner)));
        // A companion that holds it too gains.
        Hold(owner, Bear);
        Assert.AreEqual(wolf + 10, Speed(Npc(owner, Bear)));
        Assert.AreEqual(wolf - 10, Speed(Npc(owner)), "an NPC outside the spell still loses");
        // Ending the spell gives everything back.
        Hold(owner, Hero, false);
        Hold(owner, Bear, false);
        Assert.AreEqual(hero, HeroSpeed(owner));
        Assert.AreEqual(wolf, Speed(Npc(owner)));
    }

    [TestMethod]
    public void An_npc_holding_it_works_without_the_player_holding_it_the_owner_s_fix_of_the_original_s_bug()
    {
        using var owner = Owner();
        var hero = HeroSpeed(owner);
        var wolf = Speed(Npc(owner));
        Hold(owner, Wolf);
        Assert.AreEqual(wolf + 10, Speed(Npc(owner)), "the caster gains");
        Assert.AreEqual(hero - 10, HeroSpeed(owner), "the player, outside the spell, loses");
        Assert.AreEqual(wolf - 10, Speed(Npc(owner, Bear)), "another NPC loses");
        Hold(owner, Wolf, false);
        Assert.AreEqual(hero, HeroSpeed(owner));
    }

    [TestMethod]
    public void The_shift_reaches_the_turn_based_facts_and_the_attack_pause_of_the_hero_and_the_npc()
    {
        using var owner = Owner();
        var heroAttack = owner.QueryHeroAttack()!.Speed;
        var npcAttack = Npc(owner).AttackValues(new LifeStatus(10, 10, 10, 10, false, false, true), LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Speed;
        Hold(owner, Hero);
        Assert.AreEqual(heroAttack + 10, owner.QueryHeroAttack()!.Speed);
        Assert.AreEqual(npcAttack - 10, Npc(owner).AttackValues(new LifeStatus(10, 10, 10, 10, false, false, true), LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Speed);
    }

    [TestMethod]
    public void The_stat_range_still_clamps_the_shifted_speed()
    {
        using var owner = Owner();
        var low = owner.QueryHeroStats(new(0, SpeedShift: -1000))!.Value.Snapshot!.GetEffective(CharacterStat.Speed);
        var high = owner.QueryHeroStats(new(0, SpeedShift: 1000))!.Value.Snapshot!.GetEffective(CharacterStat.Speed);
        Assert.AreEqual((1, 100), (low, high));
    }

    [TestMethod]
    public void The_shift_survives_an_effect_list_and_the_copies_of_the_combat_values()
    {
        using var owner = Owner();
        var wolf = SessionCritterCombat.From(Wolf, AttackStateTests.Npc(stats: AttackStateTests.Stats(dexterity: 15, constitution: 3))).Combat!;
        var plain = Speed(wolf);
        // Speed +20 (Add) in an effect list, then the hero holds Tempus Fugit: the effect list's clone still reads the shift (base + shift, then the effect).
        var swift = new SessionEffectDefinition(156, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 20)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, wolf.Target, swift, EffectCause.Spell).Disposition);
        Assert.AreEqual(plain + 20, Speed(owner.WithNpcEffects(wolf)));
        Hold(owner, Hero);
        var under = owner.WithNpcEffects(wolf);
        Assert.AreEqual(plain + 20 - 10, Speed(under), "effects, then the shift: both apply");
        // A copy keeps the shift it was given, whatever else it carries.
        Assert.AreEqual(plain + 20 - 5, Speed(under.WithSpeedShift(-5)));
        Assert.AreEqual(plain + 20 - 10, Speed(under.WithSpeedShift(-5).WithSpeedShift(-10)));
    }

    [TestMethod]
    public void A_flag_in_the_data_of_an_npc_nobody_cast_starts_nothing_and_the_flag_cache_follows_every_write()
    {
        using var owner = Owner();
        var wolf = Speed(Npc(owner));
        Assert.AreEqual(0, owner.TempusFugitShiftOf(Wolf, Flag), "a data bit alone: nobody cast it, no shift");
        Hold(owner, Bear);
        Assert.AreEqual(10, owner.TempusFugitShiftOf(Wolf, Flag), "with a written holder, the critter whose data has the bit gains too");
        Assert.AreEqual(-10, owner.TempusFugitShiftOf(Wolf, 0));
        Hold(owner, Bear, false);
        Assert.AreEqual(wolf, Speed(Npc(owner)), "after the write is undone the cache answers 'nobody'");
        Assert.AreEqual(0, owner.HeroTempusFugitShift());
    }

    private static bool Holds(GameSessionOwner owner, string who) => (owner.QueryObjectFlags(who).Spell(0) & Flag) != 0;

    // ARC-MAGIC-1190 (XR7, TF3): the join tail 0x45DE90 copies the leader's flag by a plain write, the removal 0x45E040 -> 0x459EA0 clears the leaver's.

    [TestMethod]
    public void A_companion_that_joins_after_the_cast_gains_the_flag_and_the_ten()
    {
        using var owner = Owner();
        var wolf = Speed(Npc(owner));
        Hold(owner, Hero);
        Assert.AreEqual(wolf - 10, Speed(Npc(owner, Bear)), "outside the spell: slowed");
        var before = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Bear).Disposition);
        Assert.AreEqual(before + 1, owner.Version.Revision, "one revision for the join, the flag write inside it");
        Assert.IsTrue(Holds(owner, Bear));
        Assert.AreEqual(wolf + 10, Speed(Npc(owner, Bear)), "the joiner gains the 10 at once");
        Assert.AreEqual(wolf - 10, Speed(Npc(owner)), "a critter that did not join still loses");
        // A checked join (the follow check, as the dialog's JO) copies it too.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Wolf, PartyStateTests.Facts(), false).Disposition);
        Assert.IsTrue(Holds(owner, Wolf));
    }

    [TestMethod]
    public void A_join_without_the_leader_holding_it_writes_nothing_and_a_holding_joiner_is_left_alone()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Bear).Disposition);
        Assert.IsTrue(owner.QueryObjectFlags(Bear).IsEmpty, "the hero does not hold it: nothing is copied");
        // An NPC holding it that joins keeps its written flag as it is (no second write), and the hero holding too changes nothing more.
        Hold(owner, Wolf);
        var flags = owner.QueryObjectFlags(Wolf);
        Hold(owner, Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Wolf).Disposition);
        Assert.AreEqual(flags, owner.QueryObjectFlags(Wolf));
    }

    [TestMethod]
    public void An_entity_the_written_flags_do_not_cover_joins_without_a_write_and_a_full_flag_bound_refuses_the_join()
    {
        using var owner = Owner();
        Hold(owner, Hero);
        // A static object's positional id is no mobile object: the join works, no flag is written (the written flags cover the hero and mobile objects).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "static:pal").Disposition);
        Assert.IsTrue(owner.QueryObjectFlags("static:pal").IsEmpty);
        // The written-flags bound full: the join that would need a new entry is refused with nothing changed; a critter that already has an entry still joins.
        for (int i = 0, held = owner.QueryAllObjectFlags().Count; held < GameSessionOwner.MaxObjectFlagCritters; i++, held++) Hold(owner, $"oid:filler{i}");
        var before = owner.Version;
        var refused = owner.ForceJoinParty(before, Bear);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
        Assert.AreEqual(before, owner.Version);
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Bear).Standing);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:filler0").Disposition);
        Assert.IsTrue(Holds(owner, "oid:filler0"));
    }

    [TestMethod]
    public void A_companion_that_leaves_loses_the_flag_at_once_whatever_the_leader_holds()
    {
        using var owner = Owner();
        var wolf = Speed(Npc(owner));
        Hold(owner, Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Bear).Disposition);
        Assert.AreEqual(wolf + 10, Speed(Npc(owner, Bear)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Bear, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(Holds(owner, Bear));
        Assert.AreEqual(wolf - 10, Speed(Npc(owner, Bear)), "the leaver is outside the spell again while the hero holds it");
        Assert.IsTrue(Holds(owner, Hero), "the leader keeps it");
        // A follower that kept a flag after the spell ended loses it on leaving too.
        Hold(owner, Hero, false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Wolf).Disposition);
        Hold(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Wolf, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(Holds(owner, Wolf));
        Assert.AreEqual(0, owner.TempusFugitShiftOf(Wolf, 0), "nobody holds it any more: no shift");
    }

    [TestMethod]
    public void A_wait_is_no_leave_the_waiter_keeps_the_flag_and_an_unwait_is_a_join()
    {
        using var owner = Owner();
        Hold(owner, Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Bear).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Bear).Disposition);
        Assert.IsTrue(Holds(owner, Bear), "the waiter keeps its leader and its flag (the original's wait is no removal)");
        // The spell ends while it waits: the waiter is outside the party array, so it keeps the stale flag (the original does the same); the unwait joins and copies nothing now.
        Hold(owner, Hero, false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UnwaitParty(owner.Version, Bear, PartyStateTests.Facts(), force: true).Disposition);
        Assert.IsTrue(Holds(owner, Bear));
        // Waiting while the hero holds it again, a waiter that never had it gains it when it is brought back.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Wolf).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Wolf).Disposition);
        Hold(owner, Hero);
        Assert.IsFalse(Holds(owner, Wolf));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Wolf).Disposition);
        Assert.IsTrue(Holds(owner, Wolf));
    }

    [TestMethod]
    public void The_loyalty_step_that_makes_a_follower_leave_clears_the_flag_and_the_save_round_trips()
    {
        using var owner = Owner();
        Hold(owner, Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Wolf, PartyStateTests.Facts(), false).Disposition);
        Assert.IsTrue(Holds(owner, Wolf));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, Wolf, new(50, CharacterRace.Human, false, false), new(CharacterRace.Human, 0, 0, [], [], [], false), -5).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ChangeHeroAlignment(owner.Version, 301).Disposition);
        var facts = PartyStateTests.Facts(reaction: 50) with { PcAlignment = 301, PcCharisma = 8 };
        Assert.AreEqual(LoyaltyStepKind.Left, owner.RunLoyaltyStep(owner.Version, Wolf, facts, false).Step!.Value.Kind);
        Assert.IsFalse(Holds(owner, Wolf), "the follower that left through the loyalty step lost the flag");
        Assert.IsTrue(Holds(owner, Hero));
    }
}
