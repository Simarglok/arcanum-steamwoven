using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1658 (ledger RM3b, issue #1658): the TraitIdx component for the hero's resistance array (field 31), stat 25 (age), gender (26) and race (27). Records and numbers are invented.
public sealed partial class SessionSpellCasterTraitIndexTests
{
    // Normal resistance +40, stat 25 +2000 / -3000 (the clamp 20..1000 of the setter), gender and race by their stored value.
    private const int NormalPlus = 700, AgePlus = 701, AgeMinus = 702;

    private static Dictionary<int, SessionSpellRecord> InnateRecords() => new(Records)
    {
        [NormalPlus] = Spell(NormalPlus, ResistField, 0, 40), [AgePlus] = Spell(AgePlus, StatField, 25, 2000), [AgeMinus] = Spell(AgeMinus, StatField, 25, -3000),
    };

    // The effect rules the host attaches: a woman's (stat strength -1, constitution +1, a basic skill), the dwarf's (strength +2, a tech skill), the human's (perception +1).
    private static readonly SessionEffectDefinition HeroFemale = new(330, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, -1), new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, 1),
            new(EffectAttributeKind.BasicSkill, 1, EffectChangeOperator.Add, 5)]),
        HeroDwarf = new(65, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2), new(EffectAttributeKind.TechSkill, 0, EffectChangeOperator.Add, 3)]),
        HeroHuman = new(64, [new(EffectAttributeKind.Stat, 5, EffectChangeOperator.Add, 1)]),
        Brisk = new(300, [new(EffectAttributeKind.Stat, 1, EffectChangeOperator.Add, 2)]);

    private static SessionHeroDefinition HumanHero(int[]? stats = null) => HalfElf(stats ?? [8, 10, 8, 8, 8, 8, 8, 8, 0, 0]) with { Race = CharacterRace.Human, RaceEffect = null };

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) InnateSession(SessionHeroDefinition hero, bool rules = true)
    {
        var (owner, caster) = HeroSession(hero, records: InnateRecords());
        if (rules) owner.AttachInnateEffects([HeroFemale, HeroDwarf, HeroHuman]);
        return (owner, caster);
    }

    private static int Effective(GameSessionOwner owner, CharacterStat stat) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(stat);

    private static (int Number, EffectCause Cause)[] Innate(GameSessionOwner owner) => [.. owner.QueryHeroEffects()!.Innate.Select(e => (e.Number, e.Cause))];

    [TestMethod]
    public void A_heros_resistance_adds_to_its_stored_array_unclamped_and_the_resistance_readers_lay_the_armour_and_effects_over_it()
    {
        var (owner, caster) = InnateSession(SkilledHero());
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, FirePlus));
        Assert.AreEqual(25, owner.QueryHeroStoredResistance(1));
        CastOnHero(caster, FirePlus);
        Assert.AreEqual(50, owner.QueryHeroStoredResistance(1), "the second write reads the value the first left");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, ResistField, Hero, 1), caster.Log.Last());
        CastOnHero(caster, NormalPlus);
        CastOnHero(caster, MagicMinus);
        Assert.AreEqual(40, owner.QueryHeroStoredResistance(0));
        Assert.AreEqual(-200, owner.QueryHeroStoredResistance(4), "the stored array keeps the value unclamped");
        // The sheet: the stored value is the base under the worn armour (none here) and the effects, cut to 0..95 (the poison bonus of constitution 4 is 0).
        var (damage, magic) = owner.QueryHeroSheetResistances(EquipmentResistances.None, 4);
        Assert.AreEqual((40, 50, 0, 0), (damage.Normal, damage.Fire, damage.Electrical, damage.Poison));
        Assert.AreEqual(0, magic);
        // The save of a spell reads the stored magic resistance (no effect changes it here).
        Assert.AreEqual(-200, owner.HeroSaveMagicResistance());
        Assert.IsFalse(NotReady(caster));
        // The refusals of the write.
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroResistance(stale, 2, 7).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroResistance(owner.Version, 3, 9).Disposition);
        var sheet = owner.QueryHeroSheetResistances(EquipmentResistances.None, 4).Damage;
        Assert.AreEqual((7, 9), (sheet.Electrical, sheet.Poison), "index 2 is electrical and 3 is poison");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteHeroResistance(stale, 2, 8).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroResistance(owner.Version, 5, 1).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteHeroResistance(owner.Version, 2, 7).Disposition);
        Assert.IsNull(owner.QueryHeroStoredResistance(5));
    }

    [TestMethod]
    public void A_heros_age_is_clamped_to_20_to_1000_and_stored_and_is_not_ready_when_the_session_has_not_read_it()
    {
        var (owner, caster) = InnateSession(SkilledHero() with { Age = 30 });
        using var _ = owner;
        Assert.AreEqual(30, owner.QueryHeroStoredAge());
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, AgePlus));
        Assert.AreEqual(1000, owner.QueryHeroStoredAge());
        CastOnHero(caster, AgeMinus);
        Assert.AreEqual(20, owner.QueryHeroStoredAge(), "the setter's minimum for stat 25 is 20");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Hero, 25), caster.Log.Last());
        Assert.IsFalse(NotReady(caster));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroAge(owner.Version, 19).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteHeroAge(owner.Version, 20).Disposition);

        // A hero whose age was never read (a save of schema 98 or before) has no stored value to add to.
        var (unread, tried) = InnateSession(SkilledHero());
        using var _u = unread;
        var version = unread.Version;
        Assert.IsNull(unread.QueryHeroStoredAge());
        Assert.IsNull(CastOnHero(tried, AgePlus).JumpTo);
        Assert.AreEqual(version, unread.Version);
        Assert.IsTrue(NotReady(tried));
    }

    [TestMethod]
    public void A_heros_gender_swaps_the_innate_effect_the_skill_changes_and_the_stats_under_it()
    {
        var (owner, caster) = InnateSession(SkilledHero());
        using var _ = owner;
        CollectionAssert.AreEqual(new[] { (67, EffectCause.Race) }, Innate(owner));
        Assert.AreEqual(8, Effective(owner, CharacterStat.Strength));
        // Gender 1 + (-1) = 0: a woman. The effect 0x14A is added with the gender cause, after the race entry.
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, GenderToFemale));
        var hero = owner.QueryHero()!;
        Assert.AreEqual(CharacterGender.Female, hero.Gender);
        Assert.AreEqual(330, hero.GenderEffect!.EffectId);
        CollectionAssert.AreEqual(new[] { (67, EffectCause.Race), (330, EffectCause.Gender) }, Innate(owner));
        Assert.AreEqual((Arcanum.Simulation.Skills.SkillRules.Basic(1), EffectCause.Gender), (hero.InnateSkills.Single().Skill, hero.InnateSkills.Single().Cause));
        Assert.AreEqual(7, Effective(owner, CharacterStat.Strength));
        Assert.AreEqual(8, Effective(owner, CharacterStat.Constitution), "8 + 1 - 1 of the half-elf's");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Hero, 26), caster.Log.Last());
        Assert.IsFalse(NotReady(caster));
        // Back to a man: the entry of the gender cause leaves, the male gender adds none.
        CastOnHero(caster, GenderToMale);
        hero = owner.QueryHero()!;
        Assert.AreEqual(CharacterGender.Male, hero.Gender);
        Assert.IsNull(hero.GenderEffect);
        Assert.AreEqual(0, hero.InnateSkills.Length);
        CollectionAssert.AreEqual(new[] { (67, EffectCause.Race) }, Innate(owner));
        Assert.AreEqual(8, Effective(owner, CharacterStat.Strength));
    }

    [TestMethod]
    public void A_heros_race_swaps_the_innate_effect_and_the_durable_effects_stay_applied_to_the_new_character_owner()
    {
        var (owner, caster) = InnateSession(HumanHero());
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Brisk, EffectCause.Spell).Disposition);
        Assert.AreEqual(12, Effective(owner, CharacterStat.Dexterity));
        // Race 0 + 1: a dwarf; effect 0x41 with the race cause replaces 0x40.
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, RaceDwarf));
        var hero = owner.QueryHero()!;
        Assert.AreEqual(CharacterRace.Dwarf, hero.Race);
        Assert.AreEqual(65, hero.RaceEffect!.EffectId);
        Assert.AreEqual(Arcanum.Simulation.Skills.SkillRules.Tech(0), hero.InnateSkills.Single().Skill);
        Assert.AreEqual(EffectCause.Race, hero.InnateSkills.Single().Cause);
        CollectionAssert.AreEqual(new[] { (65, EffectCause.Race) }, Innate(owner));
        Assert.AreEqual(10, Effective(owner, CharacterStat.Strength));
        Assert.AreEqual(8, Effective(owner, CharacterStat.Perception));
        Assert.AreEqual(12, Effective(owner, CharacterStat.Dexterity), "the durable effect is applied to the new owner");
        Assert.AreEqual(1, owner.QueryHeroEffects()!.Effects.Count);
        // The durable entry's applied id belongs to the new owner: removing the entry takes its change off.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 300).Disposition);
        Assert.AreEqual(10, Effective(owner, CharacterStat.Dexterity));
        // Back to a human (1 - 5 clamps to 0): the dwarf's effect leaves with its skill change, the human's perception +1 comes in.
        CastOnHero(caster, RaceHuman);
        hero = owner.QueryHero()!;
        Assert.AreEqual(CharacterRace.Human, hero.Race);
        Assert.AreEqual(64, hero.RaceEffect!.EffectId);
        Assert.AreEqual(0, hero.InnateSkills.Length);
        Assert.AreEqual(8, Effective(owner, CharacterStat.Strength));
        Assert.AreEqual(9, Effective(owner, CharacterStat.Perception));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_heros_gender_and_race_survive_a_save_and_restore_with_the_effects_they_gave()
    {
        var (owner, caster) = InnateSession(HumanHero() with { Age = 30 });
        using var _ = owner;
        CastOnHero(caster, GenderToFemale);
        CastOnHero(caster, RaceDwarf);
        CastOnHero(caster, FirePlus);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"resistances\":[0,25,0,0,0],\"age\":30}");
        using var restored = Restore(Decode(text));
        var hero = restored.QueryHero()!;
        Assert.AreEqual((CharacterRace.Dwarf, CharacterGender.Female), (hero.Race, hero.Gender));
        Assert.AreEqual(65, hero.RaceEffect!.EffectId);
        Assert.AreEqual(330, hero.GenderEffect!.EffectId);
        Assert.AreEqual(25, restored.QueryHeroStoredResistance(1));
        Assert.AreEqual(30, restored.QueryHeroStoredAge());
        Assert.AreEqual(Effective(owner, CharacterStat.Strength), Effective(restored, CharacterStat.Strength));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void A_heros_innate_swap_is_not_ready_without_the_rule_with_race_eleven_for_a_stat_the_race_refuses_and_for_a_durable_effect_of_the_same_number()
    {
        // No rule attached for the effect to add.
        var (bare, caster) = InnateSession(HumanHero(), rules: false);
        using var _b = bare;
        var version = bare.Version;
        Assert.IsNull(CastOnHero(caster, RaceDwarf).JumpTo);
        Assert.IsNull(CastOnHero(caster, GenderToFemale).JumpTo);
        Assert.AreEqual(version, bare.Version);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        Assert.AreEqual(GameSessionFailure.EffectsUnavailable, bare.WriteHeroInnate(bare.Version, false, 1).Failure);
        // A man's gender adds no effect, so it needs no rule: the old gender effect leaves.
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, GenderToMale));

        // Race 11 is the table's last slot and a race the character rules do not have.
        var (owner, tried) = InnateSession(HumanHero());
        using var _o = owner;
        Assert.IsNull(CastOnHero(tried, RaceTooHigh).JumpTo);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroInnate(owner.Version, false, 11).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroInnate(owner.Version, true, 2).Failure);

        // Strength 24 is a half-ogre's: a human's range ends at 20, so the new owner cannot hold it.
        var (ogre, ogreCaster) = InnateSession(HalfElf([24, 10, 8, 8, 8, 8, 8, 8, 0, 0]) with { Race = CharacterRace.HalfOgre, RaceEffect = HalfOgreEffect });
        using var _g = ogre;
        ogre.AttachInnateEffects([HeroHuman]);
        Assert.IsNull(CastOnHero(ogreCaster, RaceHuman).JumpTo);
        Assert.AreEqual(CharacterRace.HalfOgre, ogre.QueryHero()!.Race);
        Assert.AreEqual(GameSessionFailure.InvalidHero, ogre.WriteHeroInnate(ogre.Version, false, 0).Failure);

        // A durable effect with the number of the new innate effect would have been refused at its add.
        var (clash, clashCaster) = InnateSession(HumanHero());
        using var _c = clash;
        Assert.AreEqual(GameSessionDisposition.Changed, clash.AddHeroEffect(clash.Version, HeroDwarf, EffectCause.Spell).Disposition);
        var before = clash.Version;
        Assert.IsNull(CastOnHero(clashCaster, RaceDwarf).JumpTo);
        Assert.AreEqual(before, clash.Version);
        Assert.AreEqual(GameSessionFailure.InvalidEffect, clash.WriteHeroInnate(clash.Version, false, 1).Failure);
    }

    [TestMethod]
    public void A_heros_innate_write_refuses_a_stale_version_and_a_session_without_a_hero()
    {
        var (owner, _) = InnateSession(HumanHero());
        using var _o = owner;
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroInnate(stale, false, 1).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteHeroInnate(stale, false, 0).Failure);
        Assert.IsFalse(owner.HeroInnateReady(false, 11));
        using var none = Create();
        Assert.AreEqual(GameSessionFailure.InvalidHero, none.WriteHeroInnate(none.Version, true, 1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHero, none.WriteHeroResistance(none.Version, 0, 1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHero, none.WriteHeroAge(none.Version, 30).Failure);
        Assert.IsNull(none.QueryHeroStoredRace());
    }
}
