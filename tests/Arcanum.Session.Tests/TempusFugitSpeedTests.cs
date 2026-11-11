using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
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
}
