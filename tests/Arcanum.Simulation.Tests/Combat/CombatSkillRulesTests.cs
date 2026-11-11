using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// Combat skill levels and training (ARC-COMBAT-005, ledger FA7–FA9) and the combatant helpers: numbers
// checked in the original 1.0.7.4 executable.
[TestClass]
public sealed class CombatSkillRulesTests
{
    [TestMethod]
    public void TheKeyStatCapsTheLevelByTheOriginalsTable()
    {
        int[] caps = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
        for (var stat = 1; stat <= 20; stat++) Assert.AreEqual(caps[stat - 1], CombatSkillRules.KeyStatCap(stat), $"stat {stat}");
        Assert.AreEqual(3, CombatSkillRules.KeyStatCap(0), "clamped to 1");
        Assert.AreEqual(3, CombatSkillRules.KeyStatCap(-5));
        Assert.AreEqual(20, CombatSkillRules.KeyStatCap(30), "clamped to 20");
        foreach (var skill in Enum.GetValues<BasicCombatSkill>()) Assert.AreEqual(CharacterStat.Dexterity, CombatSkillRules.KeyStat(skill));
    }

    [TestMethod]
    public void TheLevelIsFourPerPointClampedAndCapped()
    {
        Assert.AreEqual(0, CombatSkillRules.Level(BasicCombatSkill.Melee, 0, 8, false));
        Assert.AreEqual(4, CombatSkillRules.Level(BasicCombatSkill.Melee, 1, 8, false));
        Assert.AreEqual(7, CombatSkillRules.Level(BasicCombatSkill.Melee, 2, 8, false), "8 capped by DEX 8 at 7");
        Assert.AreEqual(20, CombatSkillRules.Level(BasicCombatSkill.Dodge, 6, 20, false), "24 clamped to 20");
        Assert.AreEqual(8, CombatSkillRules.Level(BasicCombatSkill.Bow, 2 | 3 << 6, 12, false), "training bits are not points");
        Assert.AreEqual(4, CombatSkillRules.Level(BasicCombatSkill.Throwing, 1 | 0x100, 20, false), "bits 0..5 only: bit 8 is training");
        Assert.AreEqual(12, CombatSkillRules.Level(BasicCombatSkill.Melee, 2, 20, false, [new(StatModifierOperator.Add, 4)]), "effects before the clamp");
        Assert.AreEqual(0, CombatSkillRules.Level(BasicCombatSkill.Melee, 1, 20, false, [new(StatModifierOperator.Add, -10)]), "never below 0");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatSkillRules.Level((BasicCombatSkill)7, 0, 8, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatSkillRules.Level(BasicCombatSkill.Melee, 0, CombatSkillRules.MaxValue + 1, false));
    }

    [TestMethod]
    public void AMonstrousCritterFightsAtMeleeTwentyCappedByItsDexterity()
    {
        Assert.AreEqual(3, CombatSkillRules.Level(BasicCombatSkill.Melee, 0, 4, true), "a rat with DEX 4");
        Assert.AreEqual(20, CombatSkillRules.Level(BasicCombatSkill.Melee, 0, 18, true));
        Assert.AreEqual(0, CombatSkillRules.Level(BasicCombatSkill.Dodge, 0, 18, true), "only melee");
    }

    [TestMethod]
    public void TrainingIsStoredExceptAMonstrousCrittersMeleeTiers()
    {
        Assert.AreEqual(SkillTraining.Expert, CombatSkillRules.Training(BasicCombatSkill.Dodge, 1 | 2 << 6, false, 0, 1));
        Assert.AreEqual(SkillTraining.Master, CombatSkillRules.Training(BasicCombatSkill.Melee, 3 << 6, false, 0, 1));
        Assert.AreEqual(SkillTraining.None, CombatSkillRules.Training(BasicCombatSkill.Melee, 3 << 6, true, 20, 9), "level below 10");
        Assert.AreEqual(SkillTraining.None, CombatSkillRules.Training(BasicCombatSkill.Melee, 0, true, 0, 40), "melee below 1");
        Assert.AreEqual(SkillTraining.Apprentice, CombatSkillRules.Training(BasicCombatSkill.Melee, 0, true, 8, 40));
        Assert.AreEqual(SkillTraining.Apprentice, CombatSkillRules.Training(BasicCombatSkill.Melee, 0, true, 20, 19));
        Assert.AreEqual(SkillTraining.Expert, CombatSkillRules.Training(BasicCombatSkill.Melee, 0, true, 17, 30));
        Assert.AreEqual(SkillTraining.Master, CombatSkillRules.Training(BasicCombatSkill.Melee, 0, true, 18, 30));
        Assert.AreEqual(SkillTraining.Expert, CombatSkillRules.Training(BasicCombatSkill.Dodge, 2 << 6, true, 20, 30), "other skills stay stored");
    }

    [TestMethod]
    public void ResistancesAddArmourAndConstitutionAndClampUnlessMonstrous()
    {
        var stored = new DamageResistances(10, 0, 5, 90);
        var worn = new EquipmentResistances(20, 10, 0, 5, 0);
        Assert.AreEqual(new DamageResistances(30, 5 + 20, 5, 95), CombatantRules.Resistances(stored, worn, 8, false), "poison + 5 × (8 − 4)");
        Assert.AreEqual(new DamageResistances(30, 5, 5, 100), CombatantRules.Resistances(stored, worn, 3, true), "no negative CN bonus; unclamped");
        Assert.AreEqual(0, CombatantRules.Resistances(new(-10, 0, 0, 0), EquipmentResistances.None, 4, false).Normal);
        Assert.AreEqual(-10, CombatantRules.Resistances(new(-10, 0, 0, 0), EquipmentResistances.None, 4, true).Normal);
    }

    // ARC-MAGIC-027 (0x43D6D0, EQ8, RE1): the resistance effects come after the armour and the poison bonus and before the clamp.
    [TestMethod]
    public void ResistanceEffectsAreAggregatedOverTheSumAndUnderTheClamp()
    {
        EffectModifier Add(int value) => new(StatModifierOperator.Add, value);
        EffectModifier Percent(int value) => new(StatModifierOperator.Percent, value);
        var stored = new DamageResistances(10, 0, 5, 40);
        var worn = new EquipmentResistances(20, 10, 0, 5, 0);
        var effects = new EffectResistances([Percent(50), Add(5)], [Add(30)], [], [Add(-100)], [Add(99)]);
        // Normal (10 + 20) * 150 % + 5 = 50; fire 40 + 10 + 30 = 80; electrical 5 + 0 = 5; poison 0 + 5 + 5 × (8 − 4) − 100 cut at 0; magic is no part of the result.
        Assert.AreEqual(new DamageResistances(50, 0, 5, 80), CombatantRules.Resistances(stored, worn, 8, false, effects));
        // The effects see the poison bonus: +30 over (0 + 5 + 20) = 55.
        Assert.AreEqual(55, CombatantRules.Resistances(stored, worn, 8, false, new([], [], [], [Add(30)], [])).Poison);
        // The clamp is after the effects: +90 over 80 is cut at 95; a monstrous NPC keeps the sum (to the resolver's own bound).
        var cut = new EffectResistances([], [Add(90)], [], [], []);
        Assert.AreEqual(95, CombatantRules.Resistances(stored, worn, 8, false, cut).Fire);
        Assert.AreEqual(140, CombatantRules.Resistances(stored, worn, 8, true, cut).Fire);
        Assert.AreEqual(-60, CombatantRules.Resistances(new(0, 0, 0, 0), EquipmentResistances.None, 4, true, new([Add(-60)], [], [], [], [])).Normal);
        // No effects: the four-argument form.
        Assert.AreEqual(CombatantRules.Resistances(stored, worn, 8, false), CombatantRules.Resistances(stored, worn, 8, false, EffectResistances.None));
        Assert.IsTrue(EffectResistances.None.IsEmpty);
        Assert.IsFalse(effects.IsEmpty);
    }

    [TestMethod]
    public void ConditionsComeFromLifeTraitsAndFlags()
    {
        var status = new LifeStatus(10, 0, 10, 0, true, true, false);
        var state = new LifeState(32000, 10, 0, LifeConditions.Blinded | LifeConditions.CrippledArmsBoth | LifeConditions.CrippledLegsBoth, 0);
        var conditions = CombatantRules.Conditions(status, state, LifeTraits.Undead | LifeTraits.NighInvulnerable,
            CombatantFlags.DarkSight | CombatantFlags.FatigueDraining);
        Assert.AreEqual(CombatantConditions.Dead | CombatantConditions.Unconscious | CombatantConditions.Blinded | CombatantConditions.CrippledArmsBoth |
            CombatantConditions.CrippledLegsBoth | CombatantConditions.Undead | CombatantConditions.NighInvulnerable | CombatantConditions.DarkSight |
            CombatantConditions.FatigueDraining, conditions);
        Assert.AreEqual(CombatantConditions.None, CombatantRules.Conditions(new(10, 10, 10, 10, false, false, true), LifeState.Unharmed,
            LifeTraits.Unrevivifiable, CombatantFlags.None));
    }
}
