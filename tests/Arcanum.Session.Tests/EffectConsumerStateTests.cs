using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-MAGIC-027 (MA-2 of the magic audit ARC-RES-103): the consumers of an effect list's resistance and stat changes. The effective resistance function (0x43D6D0, ledger EQ8, RE1) puts the
/// resistance effects over the stored value, the armour and the poison bonus and under the 0..95 clamp; the NPC's stats under its list (speed, strength) reach its combat values. The effect
/// numbers and values are invented; the resistance order of the rules is normal 0, fire 1, electrical 2, poison 3, magic 4.
/// </summary>
[TestClass]
public sealed class EffectConsumerStateTests
{
    private const string Wolf = "oid:wolf";

    private static EffectChange Resist(int attribute, int value, EffectChangeOperator op = EffectChangeOperator.Add) => new(EffectAttributeKind.Resistance, attribute, op, value);

    // Resistances: fire +30, normal +50 % (the percent form of the rules).
    private static readonly SessionEffectDefinition Shield = new(155, [Resist(1, 30), Resist(0, 50, EffectChangeOperator.Percent)]);
    // Fire −50 and magic +25.
    private static readonly SessionEffectDefinition Weakness = new(154, [Resist(1, -50), Resist(4, 25)]);
    // Speed −50 % (Congeal Time's effect 57 has this form) and fire +40.
    private static readonly SessionEffectDefinition Slow = new(57, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Percent, -50), Resist(1, 40)]);

    // The fixture hero has no stored resistance and CN 7: the poison bonus 5 × (7 − 4).
    private static readonly DamageResistances Plain = new(0, 15, 0, 0);

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static DamageResistances HeroResistances(GameSessionOwner owner) => owner.QueryHeroAttack()!.Combatant.Resistances;

    private static SessionCritterCombat Npc(int[]? resistances = null, uint critterFlags = 0, int constitution = 3, string entity = Wolf)
    {
        var build = SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: AttackStateTests.Stats(constitution: constitution), critterFlags: critterFlags, resistances: resistances));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static DamageResistances Defends(SessionCritterCombat npc) => npc.Combatant(new LifeStatus(10, 10, 10, 10, false, false, true), LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Resistances;

    [TestMethod]
    public void The_resistance_changes_of_the_hero_s_effects_reach_what_it_defends_with()
    {
        using var owner = Owner();
        Assert.AreEqual(Plain, HeroResistances(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Shield, EffectCause.Spell, 2).Disposition);
        // Two entries: fire +30 twice, normal +50 % of 0 stays 0 (the percent works on the sum, which is 0).
        Assert.AreEqual(Plain with { Fire = 60 }, HeroResistances(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Weakness, EffectCause.Spell).Disposition);
        // Fire −50 joins the 60: 10. Magic is kept for the sheet and is no part of the defence.
        Assert.AreEqual(10, HeroResistances(owner).Fire);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 155, 2).Disposition);
        // A negative sum is cut at 0 (a PC's resistance is clamped to 0..95): −50.
        Assert.AreEqual(Plain, HeroResistances(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 154).Disposition);
        Assert.AreEqual(Plain, HeroResistances(owner));
    }

    [TestMethod]
    public void A_resistance_effect_is_cut_at_95_for_a_pc_after_the_effects_are_summed()
    {
        using var owner = Owner();
        var big = new SessionEffectDefinition(156, [Resist(1, 80), Resist(2, 70)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, big, EffectCause.Spell, 3).Disposition);
        Assert.AreEqual(Plain with { Electrical = 95, Fire = 95 }, HeroResistances(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 156, 2).Disposition);
        Assert.AreEqual(Plain with { Electrical = 70, Fire = 80 }, HeroResistances(owner), "one entry left, nothing is cut");
    }

    [TestMethod]
    public void A_resistance_change_of_an_unknown_attribute_or_a_huge_value_is_refused()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidEffect, owner.AddHeroEffect(owner.Version, new(158, [Resist(5, 1)]), EffectCause.Spell).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidEffect, owner.AddHeroEffect(owner.Version, new(158, [Resist(1, 1001)]), EffectCause.Spell).Failure,
            "a resistance change is modelled now, so it keeps within 1000 like the stats");
    }

    [TestMethod]
    public void An_NPC_defends_under_the_resistance_changes_of_its_session_list_and_loses_them_with_it()
    {
        using var owner = Owner();
        var wolf = Npc(resistances: [10, 20, 0, 0, 0]);
        Assert.AreEqual(new DamageResistances(10, 0, 0, 20), Defends(owner.WithNpcEffects(wolf)), "no list, the NPC itself");
        Assert.AreSame(wolf, owner.WithNpcEffects(wolf));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, wolf.Target, Shield, EffectCause.Spell).Disposition);
        // Fire 20 + 30 = 50 and normal 10 + 50 % = 15 (the percent is over the stored sum).
        Assert.AreEqual(new DamageResistances(15, 0, 0, 50), Defends(owner.WithNpcEffects(wolf)));
        // The shared build is never touched.
        Assert.AreEqual(new DamageResistances(10, 0, 0, 20), Defends(wolf));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveNpcEffect(owner.Version, wolf.Target, 155).Disposition);
        Assert.AreEqual(new DamageResistances(10, 0, 0, 20), Defends(owner.WithNpcEffects(wolf)));
    }

    [TestMethod]
    public void A_monstrous_NPC_is_not_clamped_and_an_ordinary_one_is()
    {
        using var owner = Owner();
        var huge = new SessionEffectDefinition(159, [Resist(1, 200)]);
        var person = Npc(critterFlags: 0);
        var monster = Npc(critterFlags: 0x8000, entity: "oid:troll");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, person.Target, huge, EffectCause.Spell).Disposition);
        Assert.AreEqual(95, Defends(owner.WithNpcEffects(person)).Fire);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, monster.Target, huge, EffectCause.Spell).Disposition);
        Assert.AreEqual(200, Defends(owner.WithNpcEffects(monster)).Fire, "0x43D6D0 returns a monstrous NPC's sum unclamped");
    }

    [TestMethod]
    public void The_stat_changes_of_an_NPC_s_effects_reach_its_stats_and_the_shared_owner_stays_untouched()
    {
        using var owner = Owner();
        var wolf = Npc();
        var plain = wolf.EffectiveStat(CharacterStat.Speed, LifeState.Unharmed);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, wolf.Target, Slow, EffectCause.Spell).Disposition);
        var slowed = owner.WithNpcEffects(wolf);
        Assert.IsTrue(plain >= 4, "the fixture's speed leaves room to halve");
        Assert.AreEqual(plain / 2, slowed.EffectiveStat(CharacterStat.Speed, LifeState.Unharmed), "speed -50 % of Congeal Time");
        Assert.AreEqual(plain, wolf.EffectiveStat(CharacterStat.Speed, LifeState.Unharmed));
        Assert.AreEqual(plain / 2, slowed.AttackValues(new LifeStatus(10, 10, 10, 10, false, false, true), LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Speed,
            "the attack pause reads the effective speed");
        Assert.AreEqual(40, Defends(slowed).Fire, "the same entry's resistance change");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveNpcEffect(owner.Version, wolf.Target, 57).Disposition);
        Assert.AreEqual(plain, owner.WithNpcEffects(wolf).EffectiveStat(CharacterStat.Speed, LifeState.Unharmed));
    }

    [TestMethod]
    public void A_loaded_list_applies_only_through_the_rules_the_host_gives()
    {
        using var owner = Owner();
        var fields = AttackStateTests.Npc();
        fields = fields with { Critter = fields.Critter! with { Effects = [new Arcanum.Data.Combat.CombatEffectEntry(155, (int)EffectCause.Spell)] } };
        var loaded = SessionCritterCombat.From(Wolf, fields).Combat!;
        Assert.AreEqual(1, loaded.StoredEffects.Count);
        // No rule for the loaded number: the entry has no changes, the NPC is the same.
        Assert.AreEqual(DamageResistances.None, Defends(owner.WithNpcEffects(loaded)));
        owner.NpcEffectRules = number => number == Shield.Number ? Shield : null;
        Assert.AreEqual(new DamageResistances(0, 0, 0, 30), Defends(owner.WithNpcEffects(loaded)), "the host's rule gives the loaded entry its changes: fire +30");
    }
}
