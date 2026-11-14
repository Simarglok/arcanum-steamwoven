using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

[TestClass]
public sealed class AttackRulesTests
{
    [TestMethod]
    public void HitLocationRollBoundaries()
    {
        Assert.AreEqual(HitLocation.Torso, AttackRules.HitLocationFromRoll(1));
        Assert.AreEqual(HitLocation.Torso, AttackRules.HitLocationFromRoll(70));
        Assert.AreEqual(HitLocation.Leg, AttackRules.HitLocationFromRoll(71));
        Assert.AreEqual(HitLocation.Leg, AttackRules.HitLocationFromRoll(85));
        Assert.AreEqual(HitLocation.Arm, AttackRules.HitLocationFromRoll(86));
        Assert.AreEqual(HitLocation.Arm, AttackRules.HitLocationFromRoll(95));
        Assert.AreEqual(HitLocation.Head, AttackRules.HitLocationFromRoll(96));
        Assert.AreEqual(HitLocation.Head, AttackRules.HitLocationFromRoll(100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AttackRules.HitLocationFromRoll(0));
        Assert.AreEqual(-50, AttackRules.HitLocationPenalty(HitLocation.Head));
        Assert.AreEqual(-30, AttackRules.HitLocationPenalty(HitLocation.Arm));
        Assert.AreEqual(-30, AttackRules.HitLocationPenalty(HitLocation.Leg));
        Assert.AreEqual(0, AttackRules.HitLocationPenalty(HitLocation.Torso));
    }

    [TestMethod]
    public void AttackEffectivenessBySkillIntelligenceAndDifficulty()
    {
        Assert.AreEqual(75, Effectiveness(CombatSkill.Melee, 10));
        Assert.AreEqual(75, Effectiveness(CombatSkill.Bow, 10));
        Assert.AreEqual(95, Effectiveness(CombatSkill.Throwing, 10));
        Assert.AreEqual(25, Effectiveness(CombatSkill.Melee, 0));
        // Combat skills are not capped at 95.
        Assert.AreEqual(125, Effectiveness(CombatSkill.Melee, 20));
        Assert.AreEqual(55, AttackRules.AttackEffectiveness(CombatSkill.Firearms, 10, false, false, GameDifficulty.Normal, true));
        // Magnetic inversion only hampers firearms.
        Assert.AreEqual(75, AttackRules.AttackEffectiveness(CombatSkill.Bow, 10, false, false, GameDifficulty.Normal, true));
        Assert.AreEqual(85, AttackRules.AttackEffectiveness(CombatSkill.Melee, 10, true, false, GameDifficulty.Normal, false));
        // Difficulty scales the PC only: 75 + 37 and 75 − 18.
        Assert.AreEqual(112, AttackRules.AttackEffectiveness(CombatSkill.Melee, 10, false, true, GameDifficulty.Easy, false));
        Assert.AreEqual(57, AttackRules.AttackEffectiveness(CombatSkill.Melee, 10, false, true, GameDifficulty.Hard, false));
        Assert.AreEqual(75, AttackRules.AttackEffectiveness(CombatSkill.Melee, 10, false, false, GameDifficulty.Easy, false));
        Assert.AreEqual(5, AttackRules.AttackEffectiveness(CombatSkill.Firearms, 0, false, false, GameDifficulty.Normal, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Effectiveness(CombatSkill.Melee, 21));

        static int Effectiveness(CombatSkill skill, int level) =>
            AttackRules.AttackEffectiveness(skill, level, false, false, GameDifficulty.Normal, false);
    }

    [TestMethod]
    public void DodgeEffectivenessIsTwiceTheLevelAsInTheOriginal()
    {
        // The retail 1.0.7.4 executable computes 2L (ARC-RES-048).
        Assert.AreEqual(0, AttackRules.DodgeEffectiveness(0, false, false, GameDifficulty.Normal));
        Assert.AreEqual(20, AttackRules.DodgeEffectiveness(10, false, false, GameDifficulty.Normal));
        Assert.AreEqual(40, AttackRules.DodgeEffectiveness(20, false, false, GameDifficulty.Normal));
        Assert.AreEqual(30, AttackRules.DodgeEffectiveness(10, true, false, GameDifficulty.Normal));
        Assert.AreEqual(30, AttackRules.DodgeEffectiveness(10, false, true, GameDifficulty.Easy));
        Assert.AreEqual(30, AttackRules.DodgeEffectiveness(20, false, true, GameDifficulty.Hard));
        // The highest reachable value stays below the 95 cap.
        Assert.AreEqual(75, AttackRules.DodgeEffectiveness(20, true, true, GameDifficulty.Easy));
    }

    [TestMethod]
    public void CriticalChances()
    {
        Assert.AreEqual(3, AttackRules.CriticalHitChance(75, null, 0, 0, 0));
        Assert.AreEqual(13, AttackRules.CriticalHitChance(75, HitLocation.Head, 0, 0, 0));
        Assert.AreEqual(9, AttackRules.CriticalHitChance(75, HitLocation.Arm, 0, 0, 0));
        Assert.AreEqual(9, AttackRules.CriticalHitChance(75, HitLocation.Leg, 0, 0, 0));
        Assert.AreEqual(3 + 5 + 15 - 2, AttackRules.CriticalHitChance(75, null, 5, 15, -2));
        Assert.AreEqual(15, AttackRules.TrueBackstabCriticalBonus(10, SkillTraining.Expert, 5));
        Assert.AreEqual(35, AttackRules.TrueBackstabCriticalBonus(10, SkillTraining.Master, 5));
        Assert.AreEqual(-10, AttackRules.TrueBackstabCriticalBonus(0, SkillTraining.Apprentice, 10));

        Assert.AreEqual(3, AttackRules.CriticalMissChance(75, 0, 0, 0, 0));
        Assert.AreEqual(10, AttackRules.CriticalMissChance(25, 0, 0, 0, 0));
        // Never below 2, also for effectiveness above 100.
        Assert.AreEqual(2, AttackRules.CriticalMissChance(90, 0, 0, 0, 0));
        Assert.AreEqual(2, AttackRules.CriticalMissChance(125, 0, 0, 0, 0));
        // Wear: 100 × 50 / (100 × 7) = 7.
        Assert.AreEqual(3 + 7, AttackRules.CriticalMissChance(75, 50, 100, 0, 0));
        Assert.AreEqual(3, AttackRules.CriticalMissChance(75, 100, 100, 0, 0));
        Assert.AreEqual(3 + 4 + 1, AttackRules.CriticalMissChance(75, 0, 0, 4, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AttackRules.CriticalMissChance(75, 101, 100, 0, 0));

        Assert.AreEqual(0, AttackRules.DodgeCriticalMissChance(SkillTraining.None));
        Assert.AreEqual(10, AttackRules.DodgeCriticalMissChance(SkillTraining.Apprentice));
        Assert.AreEqual(50, AttackRules.DodgeCriticalMissChance(SkillTraining.Expert));
        Assert.AreEqual(100, AttackRules.DodgeCriticalMissChance(SkillTraining.Master));
    }

    [TestMethod]
    public void AptitudeChances()
    {
        Assert.AreEqual(20, AttackRules.TechnologyFailureChance(-50, 40));
        Assert.AreEqual(0, AttackRules.TechnologyFailureChance(-50, 0));
        Assert.AreEqual(0, AttackRules.TechnologyFailureChance(-50, -40));
        Assert.AreEqual(0, AttackRules.TechnologyFailureChance(50, 40));

        // Magickal weapon against a technological target.
        Assert.AreEqual(60, AttackRules.MagickCancelChance(50, 0, -60));
        Assert.AreEqual(30, AttackRules.MagickCancelChance(50, 50, -60));
        Assert.AreEqual(60, AttackRules.MagickCancelChance(50, -20, -60));
        // Technological weapon against a magickal target.
        Assert.AreEqual(40, AttackRules.MagickCancelChance(-50, 0, 40));
        Assert.AreEqual(20, AttackRules.MagickCancelChance(-50, -50, 40));
        Assert.AreEqual(40, AttackRules.MagickCancelChance(-50, 30, 40));
        // Matching sides and neutral weapons never cancel.
        Assert.AreEqual(0, AttackRules.MagickCancelChance(50, 0, 60));
        Assert.AreEqual(0, AttackRules.MagickCancelChance(-50, 0, -60));
        Assert.AreEqual(0, AttackRules.MagickCancelChance(0, 0, -60));
    }

    [TestMethod]
    public void LightPenalty()
    {
        Assert.AreEqual(30, Penalty(0));
        Assert.AreEqual(14, Penalty(128));
        Assert.AreEqual(0, Penalty(255));
        // A small light keeps three quarters (the original's ×¾, ARC-SKILLS-003 SC9; it was ×⅔ = 20 before).
        Assert.AreEqual(22, Penalty(0, LightEmission.Small));
        Assert.AreEqual(15, Penalty(0, LightEmission.Medium));
        Assert.AreEqual(10, Penalty(0, LightEmission.Large));
        Assert.AreEqual(7, Penalty(0, LightEmission.ExtraLarge));
        // Dark sight inverts the penalty: free in the dark, full in bright light.
        Assert.AreEqual(0, Penalty(0, darkSight: true));
        Assert.AreEqual(30, Penalty(255, darkSight: true));
        Assert.AreEqual(0, Penalty(0, undead: true));
        Assert.AreEqual(0, Penalty(0, skill: CombatSkill.Melee, training: SkillTraining.Expert));
        Assert.AreEqual(30, Penalty(0, skill: CombatSkill.Melee, training: SkillTraining.Apprentice));
        Assert.AreEqual(30, Penalty(0, skill: CombatSkill.Bow, training: SkillTraining.Master));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Penalty(256));

        static int Penalty(
            int light, LightEmission emission = LightEmission.None, bool darkSight = false, bool undead = false,
            CombatSkill skill = CombatSkill.Bow, SkillTraining training = SkillTraining.None) =>
            AttackRules.LightPenalty(light, emission, darkSight, undead, skill, training);
    }

    [TestMethod]
    public void ArmorClassCountsEffectsOnceByDefault()
    {
        // Base 10, worn 20, AcAdjustment DX 14 − 10 = 4 plus a +5 effect = 9.
        Func<int, int> plusFive = ac => ac + 5;
        Assert.AreEqual(39, AttackRules.ArmorClass(10, 20, 9, plusFive, CombatAnomalyProfile.Steamwoven));
        Assert.AreEqual(44, AttackRules.ArmorClass(10, 20, 9, plusFive, CombatAnomalyProfile.Original));
        Assert.AreEqual(95, AttackRules.ArmorClass(90, 20, 9, plusFive, CombatAnomalyProfile.Steamwoven));
        Assert.AreEqual(0, AttackRules.ArmorClass(0, 0, -9, plusFive, CombatAnomalyProfile.Steamwoven));
    }

    [TestMethod]
    public void BackstabEligibility()
    {
        // 0x45F9D0 (ARC-COMBAT-1929): a nonzero level, a target not facing the attacker, and the wielded art subtype 2 at any training
        // or 3, 4 or 7 above apprentice; a true backstab when the target neither sees nor hears.
        Assert.AreEqual(BackstabKind.TrueBackstab, Backstab(3, SkillTraining.Apprentice, 2, perceives: false));
        Assert.AreEqual(BackstabKind.Backstab, Backstab(3, SkillTraining.Apprentice, 2, perceives: true));
        Assert.AreEqual(BackstabKind.TrueBackstab, Backstab(1, SkillTraining.None, 2, perceives: false), "the level counts, not the training");
        Assert.AreEqual(BackstabKind.None, Backstab(0, SkillTraining.Master, 2, perceives: false), "no points: the training does not help");
        foreach (var subtype in new[] { 3, 4, 7 })
        {
            Assert.AreEqual(BackstabKind.None, Backstab(3, SkillTraining.Apprentice, subtype, perceives: false), $"subtype {subtype} needs more than apprentice");
            Assert.AreEqual(BackstabKind.None, Backstab(3, SkillTraining.None, subtype, perceives: false));
            Assert.AreEqual(BackstabKind.TrueBackstab, Backstab(3, SkillTraining.Expert, subtype, perceives: false));
            Assert.AreEqual(BackstabKind.TrueBackstab, Backstab(3, SkillTraining.Master, subtype, perceives: false));
        }
        foreach (var subtype in new[] { 0, 1, 5, 6, 8, 9, 13, 15 })
            Assert.AreEqual(BackstabKind.None, Backstab(20, SkillTraining.Master, subtype, perceives: false), $"subtype {subtype} never backstabs");
        Assert.AreEqual(BackstabKind.None, Backstab(3, SkillTraining.Master, 2, perceives: false, faces: true), "the target faces the attacker");
        Assert.AreEqual(BackstabKind.None, AttackRules.Backstab(3, SkillTraining.Master, WeaponKind.Unarmed, 2, () => false, () => false), "bare hands");
        Assert.AreEqual(BackstabKind.None, AttackRules.Backstab(3, SkillTraining.Master, WeaponKind.ThrownItem, 2, () => false, () => false));

        // The facts are asked in the original's order and only when reached: no points, no ask; a facing target, no awareness ask.
        var asked = new List<string>();
        Func<bool> Face(bool value) => () => { asked.Add("faces"); return value; };
        Func<bool> Perceive(bool value) => () => { asked.Add("perceives"); return value; };
        AttackRules.Backstab(0, SkillTraining.Master, WeaponKind.Weapon, 2, Face(false), Perceive(false));
        Assert.AreEqual(0, asked.Count);
        AttackRules.Backstab(3, SkillTraining.Master, WeaponKind.Weapon, 2, Face(true), Perceive(false));
        CollectionAssert.AreEqual(new[] { "faces" }, asked);
        asked.Clear();
        AttackRules.Backstab(3, SkillTraining.Apprentice, WeaponKind.Weapon, 3, Face(false), Perceive(false));
        CollectionAssert.AreEqual(new[] { "faces" }, asked, "a subtype the training does not allow never asks the awareness");
        asked.Clear();
        AttackRules.Backstab(3, SkillTraining.Apprentice, WeaponKind.Weapon, 2, Face(false), Perceive(false));
        CollectionAssert.AreEqual(new[] { "faces", "perceives" }, asked);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Backstab(21, SkillTraining.Master, 2, perceives: false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Backstab(3, SkillTraining.Master, 16, perceives: false));

        static BackstabKind Backstab(int level, SkillTraining training, int subtype, bool perceives, bool faces = false) =>
            AttackRules.Backstab(level, training, WeaponKind.Weapon, subtype, () => faces, () => perceives);
    }

    [TestMethod]
    public void WeaponDamageRanges()
    {
        var sword = CombatFixtures.Sword() with { MagicDamageAdjustment = DamageByType.Zero with { Normal = 2 } };
        Assert.AreEqual(new DamageRange(10, 15), Range(sword, 3, CombatSkill.Melee, DamageType.Normal, cancelled: false));
        Assert.AreEqual(new DamageRange(8, 13), Range(sword, 3, CombatSkill.Melee, DamageType.Normal, cancelled: true));
        // The melee bonus applies to normal and fatigue only, and not to other skills.
        Assert.AreEqual(new DamageRange(0, 0), Range(sword, 3, CombatSkill.Melee, DamageType.Fire, cancelled: true));
        Assert.AreEqual(new DamageRange(5, 10), Range(sword with { Kind = WeaponKind.Weapon }, 3, CombatSkill.Throwing,
            DamageType.Normal, cancelled: true));
        // Capped at 3 × base max (12): 1 + 20 and 4 + 20 both become 12.
        var light = CombatFixtures.Sword() with { Damage = DamageRanges.None with { Normal = new DamageRange(1, 4) } };
        Assert.AreEqual(new DamageRange(12, 12), Range(light, 20, CombatSkill.Melee, DamageType.Normal, cancelled: false));
        // A bonus that would leave a positive bound at or below 0 leaves 1.
        Assert.AreEqual(new DamageRange(1, 1), Range(light, -5, CombatSkill.Melee, DamageType.Normal, cancelled: false));
        Assert.AreEqual(new DamageRange(1, 2), Range(light, -2, CombatSkill.Melee, DamageType.Normal, cancelled: false));

        // Bare hands: 1..5 normal and fatigue, capped at 2 × 5.
        var hands = CombatWeapon.Unarmed();
        Assert.AreEqual(new DamageRange(1, 5), hands.Damage.Normal);
        Assert.AreEqual(new DamageRange(1, 5), hands.Damage.Fatigue);
        Assert.AreEqual(DamageRange.Zero, hands.Damage.Poison);
        Assert.AreEqual(new DamageRange(3, 7), Range(hands, 2, CombatSkill.Melee, DamageType.Fatigue, cancelled: false));
        Assert.AreEqual(new DamageRange(10, 10), Range(hands, 20, CombatSkill.Melee, DamageType.Normal, cancelled: false));
        Assert.AreEqual(new DamageRange(5, 35), CombatWeapon.Unarmed(30).Damage.Normal);

        static DamageRange Range(CombatWeapon weapon, int bonus, CombatSkill skill, DamageType type, bool cancelled) =>
            AttackRules.WeaponDamageRange(weapon, bonus, skill, type, cancelled);
    }

    [TestMethod]
    public void MaterialReductions()
    {
        Assert.AreEqual(10, AttackRules.MaterialDamageReduction(ObjectMaterial.Stone));
        Assert.AreEqual(10, AttackRules.MaterialDamageReduction(ObjectMaterial.Brick));
        Assert.AreEqual(10, AttackRules.MaterialDamageReduction(ObjectMaterial.Metal));
        Assert.AreEqual(10, AttackRules.MaterialDamageReduction(ObjectMaterial.Force));
        Assert.AreEqual(5, AttackRules.MaterialDamageReduction(ObjectMaterial.Wood));
        Assert.AreEqual(0, AttackRules.MaterialDamageReduction(ObjectMaterial.Glass));
    }
}
