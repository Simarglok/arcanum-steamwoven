using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1723 (leftover of ARC-MAGIC-1700, ledger RM3b, issue #1723): the special changes of the hero's innate effects (maximum hit points and fatigue, the three reaction values, experience gain), read over the innate entries as the durable ones are.
// Records and numbers are invented.
public sealed partial class SessionSpellCasterTraitIndexTests
{
    // A half-orc's rule (effect 70) that carries special changes only: maximum hit points +10, maximum fatigue +6, reaction +3, bad reaction -4, good reaction +2 and experience gain +50 percent.
    private static readonly SessionEffectDefinition HeroOrcSpecial = new(70, [new(EffectAttributeKind.Special, 0, EffectChangeOperator.Add, 10), new(EffectAttributeKind.Special, 1, EffectChangeOperator.Add, 6),
            new(EffectAttributeKind.Special, 2, EffectChangeOperator.Add, 3), new(EffectAttributeKind.Special, 7, EffectChangeOperator.Add, -4), new(EffectAttributeKind.Special, 8, EffectChangeOperator.Add, 2),
            new(EffectAttributeKind.Special, 9, EffectChangeOperator.Percent, 50)]),
        DurableCharm = new(301, [new(EffectAttributeKind.Special, 2, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Special, 7, EffectChangeOperator.Add, -1)]);

    private static GameSessionOwner SpecialSession(bool experience = false, SessionHeroDefinition? hero = null)
    {
        var (owner, _) = HeroSession(hero ?? HumanHero(), experience, InnateRecords());
        owner.AttachInnateEffects([HeroFemale, HeroDwarf, HeroHuman, HeroOrcSpecial]);
        return owner;
    }

    private static int LifeDamage(GameSessionOwner owner) => owner.QueryHeroLife()!.Record.State.HitPointDamage;

    [TestMethod]
    public void A_heros_innate_reaction_changes_stand_before_its_durable_ones_and_a_swap_moves_them_with_its_entry()
    {
        using var owner = SpecialSession();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, DurableCharm, EffectCause.Spell).Disposition);
        var before = owner.QueryHeroReactionFacts()!;
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, 1) }, before.ReactionEffects.ToArray());
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, -1) }, before.BadReactionEffects.ToArray());
        Assert.AreEqual(0, before.GoodReactionEffects.Count);
        // A half-orc: its entry's changes come first within each value.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        var orc = owner.QueryHeroReactionFacts()!;
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, 3), new ReactionModifier(StatModifierOperator.Add, 1) }, orc.ReactionEffects.ToArray());
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, -4), new ReactionModifier(StatModifierOperator.Add, -1) }, orc.BadReactionEffects.ToArray());
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, 2) }, orc.GoodReactionEffects.ToArray());
        // The old entry leaves with the race swap; the durable one stays.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 1).Disposition);
        var dwarf = owner.QueryHeroReactionFacts()!;
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, 1) }, dwarf.ReactionEffects.ToArray());
        Assert.AreEqual(0, dwarf.GoodReactionEffects.Count);
        // The rules are the host's, not state: detached, a hero's innate specials read nothing.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        owner.AttachInnateEffects(null);
        CollectionAssert.AreEqual(new[] { new ReactionModifier(StatModifierOperator.Add, 1) }, owner.QueryHeroReactionFacts()!.ReactionEffects.ToArray());
    }

    [TestMethod]
    public void A_heros_innate_maxima_move_its_damage_in_the_removal_and_in_the_add_of_a_swap_as_a_durable_entrys_do()
    {
        using var owner = SpecialSession();
        var life = owner.QueryHeroLife()!;
        var (maxHp, maxFatigue) = (life.Status.MaxHitPoints, life.Status.MaxFatigue);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 2 }).Disposition);
        // To the half-orc: the maxima rise by 10 and 6 and the damage with them, so the current hit points and fatigue stay.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        var orc = owner.QueryHeroLife()!;
        Assert.AreEqual((maxHp + 10, maxFatigue + 6, 12, 6), (orc.Status.MaxHitPoints, orc.Status.MaxFatigue, LifeDamage(owner), orc.Record.State.FatigueDamage));
        Assert.AreEqual((maxHp - 2, maxFatigue), (orc.Status.HitPoints, orc.Status.Fatigue));
        // The same race again: the removal takes 10 off the damage of 12 and the add puts it back, so nothing a reader sees changes.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        Assert.AreEqual(12, LifeDamage(owner));
        // Back to a dwarf: the half-orc's entry leaves with its maxima (the maxima fall by 10 and 6 and the damage with them: the hero keeps its current hit points and fatigue).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 1).Disposition);
        var dwarf = owner.QueryHeroLife()!;
        // The dwarf's own strength +2 adds 4 to the maximum hit points.
        Assert.AreEqual((maxHp + 4, maxFatigue, 6, 0), (dwarf.Status.MaxHitPoints, dwarf.Status.MaxFatigue, LifeDamage(owner), dwarf.Record.State.FatigueDamage));
        Assert.AreEqual((maxHp - 2, maxFatigue), (dwarf.Status.HitPoints, dwarf.Status.Fatigue));
    }

    [TestMethod]
    public void A_heros_innate_experience_gain_scales_its_awards()
    {
        using var owner = SpecialSession(experience: true);
        Assert.AreEqual(100, owner.AwardHeroExperience(owner.Version, 100).Award!.Award);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 6).Disposition);
        Assert.AreEqual(150, owner.AwardHeroExperience(owner.Version, 100).Award!.Award, "+50 percent of the half-orc's entry");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(owner.Version, false, 1).Disposition);
        Assert.AreEqual(100, owner.AwardHeroExperience(owner.Version, 100).Award!.Award);
    }

    [TestMethod]
    public void A_swap_to_innate_maxima_the_durable_entries_would_carry_past_the_bounds_is_refused_and_so_is_an_add_under_them()
    {
        // Seven changes of +1000 on the maximum hit points: the durable adds that stay within the bounds are fewer by seven than without.
        var heavy = new SessionEffectDefinition(70, [.. Enumerable.Repeat(new EffectChange(EffectAttributeKind.Special, 0, EffectChangeOperator.Add, 1000), 7)]);
        SessionEffectDefinition Durable(int number) => new(number, [new(EffectAttributeKind.Special, 0, EffectChangeOperator.Add, 1000)]);
        int Fill(GameSessionOwner owner)
        {
            var added = 0;
            while (added < 60 && owner.AddHeroEffect(owner.Version, Durable(400 - added - 1), EffectCause.Spell).Disposition == GameSessionDisposition.Changed) added++;
            return added;
        }
        using var plain = SpecialSession();
        var free = Fill(plain);
        Assert.IsTrue(free is > 7 and < 60, "the bounds, not the count, end the adds");
        using var held = SpecialSession();
        held.AttachInnateEffects([HeroHuman, HeroDwarf, heavy]);
        Assert.AreEqual(GameSessionDisposition.Changed, held.WriteHeroInnate(held.Version, false, 6).Disposition);
        Assert.AreEqual(free - 7, Fill(held));
        // The swap itself: the durable entries leave room for the dwarf but not for the half-orc's seven changes.
        using var full = SpecialSession();
        full.AttachInnateEffects([HeroHuman, HeroDwarf, heavy]);
        Assert.AreEqual(free, Fill(full));
        var version = full.Version;
        var refused = full.WriteHeroInnate(version, false, 6);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidEffect), (refused.Disposition, refused.Failure));
        Assert.AreEqual(version, full.Version, "nothing is written for a refusal");
        Assert.AreEqual(0, full.QueryHeroStoredRace());
    }
}
