using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1700 (leftovers of ARC-MAGIC-1658, ledger RM3b, issue #1700): the resistance changes of the hero's innate effects, the damage shift of a race or gender swap, and the limits the swap keeps. Records and numbers are invented.
public sealed partial class SessionSpellCasterTraitIndexTests
{
    // A half-orc's rule (effect 70): strength +1, normal +50, poison +10 and magic +5; a woman's (330): constitution +1 and fire +20.
    private static readonly SessionEffectDefinition HeroHalfOrcResist = new(70, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Resistance, 0, EffectChangeOperator.Add, 50),
            new(EffectAttributeKind.Resistance, 3, EffectChangeOperator.Add, 10), new(EffectAttributeKind.Resistance, 4, EffectChangeOperator.Add, 5)]),
        HeroFemaleResist = new(330, [new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Resistance, 1, EffectChangeOperator.Add, 20)]),
        PoisonSkin = new(303, [new(EffectAttributeKind.Resistance, 3, EffectChangeOperator.Add, 7)]);

    private static GameSessionOwner ResistSession()
    {
        var (owner, _) = InnateSession(HumanHero());
        owner.AttachInnateEffects([HeroFemaleResist, HeroHuman, HeroHalfOrcResist, HeroDwarf]);
        return owner;
    }

    private static (int Normal, int Fire, int Electrical, int Poison) Sheet(GameSessionOwner owner, int constitution = 4)
    {
        var damage = owner.QueryHeroSheetResistances(EquipmentResistances.None, constitution).Damage;
        return (damage.Normal, damage.Fire, damage.Electrical, damage.Poison);
    }

    private static int PoisonTick(GameSessionOwner owner)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetPoisonLevel(owner.Version, SessionLifeTarget.Hero, 600).Disposition);
        var before = owner.QueryHeroLife()!.Record.State.HitPointDamage;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PoisonDamageTick(owner.Version, SessionLifeTarget.Hero).Disposition);
        return owner.QueryHeroLife()!.Record.State.HitPointDamage - before;
    }

    [TestMethod]
    public void A_heros_innate_effects_give_their_resistance_changes_to_every_resistance_reader_and_a_swap_moves_them()
    {
        using var owner = ResistSession();
        Assert.AreEqual((0, 0, 0, 0), Sheet(owner));
        var armourBefore = owner.QueryHeroDollRatings(_ => true)!.Armour;
        // Race 6, a half-orc: normal +50, poison +10, magic +5 of effect 70.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        Assert.AreEqual((50, 0, 0, 10), Sheet(owner));
        Assert.AreEqual(5, owner.QueryHeroSheetResistances(EquipmentResistances.None, 4).Magic);
        Assert.AreEqual(5, owner.HeroSaveMagicResistance(), "the save of a spell reads it");
        var combatant = owner.QueryHeroAttack()!.Combatant.Resistances;
        var poisonBonus = Sheet(owner, Effective(owner, CharacterStat.Constitution)).Poison - 10;
        Assert.AreEqual((50, 0, 0, 10 + poisonBonus), (combatant.Normal, combatant.Fire, combatant.Electrical, combatant.Poison), "the combat resistances (the poison line adds the constitution's bonus)");
        Assert.IsTrue(owner.QueryHeroDollRatings(_ => true)!.Armour > armourBefore, "the doll's armour rating");
        // A woman's effect 330 adds fire +20 after the race entry; a durable effect lies over both and the stored array under them.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, true, 0).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, PoisonSkin, EffectCause.Spell).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroResistance(owner.Version, 3, 3).Disposition);
        Assert.AreEqual((50, 20, 0, 20), Sheet(owner), "poison 3 stored + 10 innate + 7 durable");
        // Back to a human: the half-orc's entry leaves with its resistances, the durable one and the woman's stay.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 0).Disposition);
        Assert.AreEqual((0, 20, 0, 10), Sheet(owner));
        Assert.AreEqual(0, owner.QueryHeroSheetResistances(EquipmentResistances.None, 4).Magic);
        // A man's gender adds none: the woman's fire +20 leaves.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, true, 1).Disposition);
        Assert.AreEqual((0, 0, 0, 10), Sheet(owner));
        // The rules are the host's, not state: detached, a hero's innate resistances read nothing (the limit of a session the host gave no rules).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        owner.AttachInnateEffects(null);
        Assert.AreEqual((0, 0, 0, 10), Sheet(owner));
    }

    [TestMethod]
    public void A_heros_innate_normal_resistance_reaches_its_life_facts_the_poison_tick_meets_and_a_swap_trial_reads_the_new_race_s()
    {
        // Poison level 600 hurts 3; normal resistance 50 takes 1 of it (the integer share of 3).
        using var human = ResistSession();
        Assert.AreEqual(3, PoisonTick(human));
        using var orc = ResistSession();
        Assert.AreEqual(GameSessionDisposition.Changed, orc.WriteHeroInnate(orc.Version, false, 6).Disposition);
        Assert.AreEqual(2, PoisonTick(orc));
        // The swap back takes the resistance with the entry: the tick hurts 3 again.
        using var back = ResistSession();
        Assert.AreEqual(GameSessionDisposition.Changed, back.WriteHeroInnate(back.Version, false, 6).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, back.WriteHeroInnate(back.Version, false, 0).Disposition);
        Assert.AreEqual(3, PoisonTick(back));
    }

    private static LifeStatus Status(GameSessionOwner owner) => owner.QueryHeroLife()!.Status;

    [TestMethod]
    public void A_race_swap_shifts_the_hit_point_damage_in_each_of_its_two_steps_and_clamps_it_at_zero_between_them()
    {
        var dwarf = HalfElf([8, 10, 8, 8, 8, 8, 8, 8, 0, 0]) with
        {
            Race = CharacterRace.Dwarf,
            RaceEffect = new StatEffectDefinition(65, [new(CharacterStat.Strength, StatModifierOperator.Add, 2)]),
        };
        var (owner, _) = InnateSession(dwarf);
        using var _o = owner;
        var max = Status(owner).MaxHitPoints;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 2 }).Disposition);
        // To a human: the dwarf's strength +2 leaves (the maximum falls by 4, the damage with it, 2 - 4 clamps at 0), and the human's entry changes no maximum: the hero is at its new maximum, 2 hit points better off than it was.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 0).Disposition);
        var human = Status(owner);
        Assert.AreEqual((max - 4, 0), (human.MaxHitPoints, owner.QueryHeroLife()!.Record.State.HitPointDamage));
        Assert.AreEqual(max - 4, human.HitPoints, "full: the clamp at zero cost the damage nothing to carry");
        // To a dwarf again: the maximum rises by 4 and the damage with it, so the current hit points stay.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 1).Disposition);
        Assert.AreEqual((max, 4, max - 4), (Status(owner).MaxHitPoints, owner.QueryHeroLife()!.Record.State.HitPointDamage, Status(owner).HitPoints));
        // The same race again: the removal drops the maximum by 4 (4 - 4 = 0) and the add raises it by 4 (0 + 4): the damage is 4 whatever it was between 0 and 4.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteHeroInnate(owner.Version, false, 1).Disposition, "a swap to the same effect writes nothing a reader sees");
        Assert.AreEqual(4, owner.QueryHeroLife()!.Record.State.HitPointDamage);
    }

    [TestMethod]
    public void A_gender_swap_shifts_the_fatigue_damage_by_the_constitution_it_adds_and_the_same_gender_again_clamps_between_its_steps()
    {
        var (man, _) = InnateSession(HumanHero());
        using var _m = man;
        var (woman, _w) = InnateSession(HumanHero() with { Gender = CharacterGender.Female, GenderEffect = FemaleEffect });
        using var _f = woman;
        var male = Status(man);
        var gain = Status(woman).MaxFatigue - male.MaxFatigue;
        Assert.IsTrue(gain > 0, "the woman's constitution adds fatigue");
        // To a woman: the effect's constitution +1 raises the maximum fatigue and the damage by the same amount, so the current fatigue stays.
        Assert.AreEqual(GameSessionDisposition.Changed, man.WriteHeroInnate(man.Version, true, 0).Disposition);
        Assert.AreEqual((male.Fatigue, male.MaxFatigue + gain, gain), (Status(man).Fatigue, Status(man).MaxFatigue, man.QueryHeroLife()!.Record.State.FatigueDamage));
        // A woman again, unharmed: the removal takes the gain off a damage of 0 (clamped at 0, nothing to carry), the add puts the whole gain on: the hero ends tired by the gain, as the original's two steps leave it.
        Assert.AreEqual(0, woman.QueryHeroLife()!.Record.State.FatigueDamage);
        Assert.AreEqual(GameSessionDisposition.Changed, woman.WriteHeroInnate(woman.Version, true, 0).Disposition);
        Assert.AreEqual((gain, Status(woman).MaxFatigue - gain), (woman.QueryHeroLife()!.Record.State.FatigueDamage, Status(woman).Fatigue));
        // With the damage already there the same swap changes nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, woman.WriteHeroInnate(woman.Version, true, 0).Disposition);
    }

    [TestMethod]
    public void A_race_swap_of_a_woman_keeps_the_race_entry_first_where_the_original_appends_it_after_the_gender_entry()
    {
        // Limit (issue #1700 item c): the original removes the race entry and appends the new one at the end of the list, so after a race swap of a woman the list is gender, race. The session's
        // innate list is derived from the state (race, then gender) and saves no order: only the display order differs, no reader depends on it.
        var (owner, _) = InnateSession(HumanHero());
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, true, 0).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 1).Disposition);
        CollectionAssert.AreEqual(new[] { (65, EffectCause.Race), (330, EffectCause.Gender) }, Innate(owner));
    }
}
