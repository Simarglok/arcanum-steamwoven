using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// The rules of all sixteen skills (ARC-SKILLS-001, ledger FA7–FA9, AL5 and SR1–SR9): numbers checked in the original 1.0.7.4
// executable.
[TestClass]
public sealed class SkillRulesTests
{
    private static readonly int[] NoSkills = new int[SkillRules.BasicCount];
    private static readonly int[] NoTech = new int[SkillRules.TechCount];

    [TestMethod]
    public void SixteenSkillsInTwoFamiliesWithTheOriginalsKeyStats()
    {
        Assert.AreEqual(SkillRules.Count, Enum.GetValues<Skill>().Length);
        CharacterStat[] expected =
        [
            CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity,
            CharacterStat.Dexterity, CharacterStat.Perception, CharacterStat.Perception, CharacterStat.Intelligence, CharacterStat.Willpower,
            CharacterStat.Intelligence, CharacterStat.Charisma,
            CharacterStat.Intelligence, CharacterStat.Perception, CharacterStat.Dexterity, CharacterStat.Perception,
        ];
        CollectionAssert.AreEqual(expected, Enum.GetValues<Skill>().Select(SkillRules.KeyStat).ToArray());
        Assert.IsTrue(Enum.GetValues<Skill>().All(s => SkillRules.IsTech(s) == s >= Skill.Repair));
        for (var i = 0; i < SkillRules.BasicCount; i++) Assert.AreEqual(i, SkillRules.FamilyIndex(SkillRules.Basic(i)));
        for (var i = 0; i < SkillRules.TechCount; i++) Assert.AreEqual(i, SkillRules.FamilyIndex(SkillRules.Tech(i)));
        Assert.AreEqual(Skill.PickLocks, SkillRules.Tech(2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillRules.Basic(12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillRules.Tech(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillRules.KeyStat((Skill)16));
        // Every combat skill keeps the answer it had before the rules moved here.
        foreach (var skill in Enum.GetValues<BasicCombatSkill>())
            Assert.AreEqual(SkillRules.KeyStat((Skill)(int)skill), CombatSkillRules.KeyStat(skill));
    }

    [TestMethod]
    public void EveryPointCostsAndRefundsOne()
    {
        foreach (var skill in Enum.GetValues<Skill>()) Assert.AreEqual((1, 1), (SkillRules.PointCost(skill), SkillRules.PointRefund(skill)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillRules.PointCost((Skill)(-1)));
    }

    [TestMethod]
    public void LevelsOfBasicAndTechSkillsAreFourPerPointClampedAndCapped()
    {
        Assert.AreEqual(8, SkillRules.Level(Skill.Heal, 2, 9, false), "intelligence 9 allows 11");
        Assert.AreEqual(7, SkillRules.Level(Skill.Persuasion, 2, 8, false), "charisma 8 caps at 7");
        Assert.AreEqual(12, SkillRules.Level(Skill.Repair, 3 | 2 << 6, 12, false), "tech: training bits are not points");
        Assert.AreEqual(20, SkillRules.Level(Skill.Firearms, 6, 20, false), "24 clamped to 20");
        Assert.AreEqual(15, SkillRules.Level(Skill.PickLocks, 3, 20, false, [new(StatModifierOperator.Add, 3)]), "effects in level units");
        Assert.AreEqual(19, SkillRules.Level(Skill.DisarmTraps, 5, 16, false, [new(StatModifierOperator.Add, 5)]), "effects cannot pass the key stat");
        Assert.AreEqual(0, SkillRules.Level(Skill.Gambling, 1, 20, false, [new(StatModifierOperator.Add, -9)]), "never below 0");
        Assert.AreEqual(20, SkillRules.Level(Skill.Melee, 0, 20, true), "a monster's melee");
        Assert.AreEqual(0, SkillRules.Level(Skill.Repair, 0, 20, true), "no monstrous tech skill");
        Assert.AreEqual(4, SkillRules.BaseLevel(1 | 3 << 6 | 0x300));
        Assert.AreEqual(63, SkillRules.Points(0xFF));
        Assert.AreEqual(SkillTraining.Master, SkillRules.StoredTraining(0xC5));
    }

    [TestMethod]
    public void TechTrainingIsStoredAndOnlyBasicMeleeIsMonstrous()
    {
        Assert.AreEqual(SkillTraining.Expert, SkillRules.Training(Skill.Repair, 2 << 6, true, 20, 40));
        Assert.AreEqual(SkillTraining.Master, SkillRules.Training(Skill.Melee, 0, true, 18, 30));
        Assert.AreEqual(SkillTraining.None, SkillRules.Training(Skill.Melee, 3 << 6, true, 20, 9));
        Assert.AreEqual(SkillTraining.Master, SkillRules.Training(Skill.Melee, 3 << 6, false, 0, 0));
    }

    [TestMethod]
    public void TheMinimumKeyStatNamesWhatTheNextLevelNeeds()
    {
        int[] levels = [-5, 0, 1, 3, 4, 7, 8, 12, 19, 20, 21];
        int[] stats = [1, 1, 1, 1, 6, 6, 9, 12, 15, 18, 0];
        CollectionAssert.AreEqual(stats, levels.Select(SkillRules.MinimumKeyStat).ToArray());
        for (var level = 0; level <= SkillRules.MaxLevel; level++)
        {
            var stat = SkillRules.MinimumKeyStat(level);
            Assert.IsTrue(SkillRules.KeyStatCap(stat) >= level && (stat == 1 || SkillRules.KeyStatCap(stat - 1) < level), $"level {level}");
        }
    }

    [TestMethod]
    public void PointsAreSetWithinTheKeyStatsCapAndTechPointsFollow()
    {
        var stored = 1 | 2 << 6 | 0x100;
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.Changed, 2 | 2 << 6 | 0x100, 2, 0), SkillRules.SetPoints(Skill.Bow, stored, 2, 9));
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.KeyStatTooLow, stored, 1, 0), SkillRules.SetPoints(Skill.Bow, stored, 3, 11),
            "12 is above dexterity 11's cap");
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.NegativePoints, stored, 0, 0), SkillRules.SetPoints(Skill.Bow, stored, -1, 20));
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.Changed, 5, 5, 4), SkillRules.SetPoints(Skill.Firearms, 1, 5, 18));
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.Changed, 0, 0, -3), SkillRules.SetPoints(Skill.Repair, 3, 0, 1), "tech points fall too");
        Assert.AreEqual(new SkillPointsChange(SkillChangeStatus.KeyStatTooLow, 0, 0, 0), SkillRules.SetPoints(Skill.Repair, 0, 6, 20), "never above five");
        Assert.AreEqual(SkillChangeStatus.Changed, SkillRules.SetPoints(Skill.Heal, 0, 0, -3).Status, "zero points fit any stat");
        Assert.AreEqual(SkillChangeStatus.Invalid, SkillRules.SetPoints((Skill)16, 0, 1, 20).Status);
        Assert.AreEqual(SkillChangeStatus.Invalid, SkillRules.SetPoints(Skill.Heal, SkillRules.MaxValue + 1, 1, 20).Status);
        Assert.AreEqual(SkillChangeStatus.Invalid, SkillRules.SetPoints(Skill.Heal, 0, int.MaxValue / 2, 20).Status, "no 32-bit wrap (SR9)");
        Assert.AreEqual(0, SkillRules.SetPoints(Skill.Heal, 0, int.MaxValue / 2, 20).Points);
    }

    [TestMethod]
    public void TrainingRisesOneTierAtATimeFromItsMinimumLevel()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 9, 18 }, Enum.GetValues<SkillTraining>().Select(SkillRules.MinimumLevel).ToArray());
        var apprentice = 3 | 1 << 6;
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.Changed, 3 | 2 << 6, SkillTraining.Expert),
            SkillRules.SetTraining(Skill.Haggle, apprentice, SkillTraining.Expert, 9));
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.LevelTooLow, apprentice, SkillTraining.Apprentice),
            SkillRules.SetTraining(Skill.Haggle, apprentice, SkillTraining.Expert, 8));
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.SkipsTier, apprentice, SkillTraining.Apprentice),
            SkillRules.SetTraining(Skill.Haggle, apprentice, SkillTraining.Master, 20));
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.SkipsTier, 0, SkillTraining.None),
            SkillRules.SetTraining(Skill.Repair, 0, SkillTraining.Expert, 20), "none to expert skips apprentice");
        var master = 1 | 3 << 6 | 0x200;
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.LevelTooLow, master, SkillTraining.Master),
            SkillRules.SetTraining(Skill.Dodge, master, SkillTraining.Expert, 5), "lowering still needs the tier's level");
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.Changed, 1 | 1 << 6 | 0x200, SkillTraining.Apprentice),
            SkillRules.SetTraining(Skill.Dodge, master, SkillTraining.Apprentice, 5), "lowering skips tiers; other bits kept");
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.Changed, 1 | 0x200, SkillTraining.None),
            SkillRules.SetTraining(Skill.Dodge, master, SkillTraining.None, 0), "none always succeeds");
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.Changed, master, SkillTraining.Master),
            SkillRules.SetTraining(Skill.Dodge, master, SkillTraining.Master, 18), "the same tier is written again");
        Assert.AreEqual(new SkillTrainingChange(SkillChangeStatus.Invalid, 0, SkillTraining.None), SkillRules.SetTraining(Skill.Dodge, 0, (SkillTraining)4, 20));
        Assert.AreEqual(SkillChangeStatus.Invalid, SkillRules.SetTraining(Skill.Dodge, 0, SkillTraining.None, 21).Status);
    }

    [TestMethod]
    public void AKeyStatCannotFallBelowWhatTheBoughtPointsNeed()
    {
        var basic = (int[])NoSkills.Clone();
        var tech = (int[])NoTech.Clone();
        basic[(int)Skill.Prowling] = 2 | 3 << 6;   // base 8 on perception: needs perception 9
        tech[SkillRules.FamilyIndex(Skill.DisarmTraps)] = 3;  // base 12 on perception: needs perception 12
        Assert.AreEqual(new SkillKeyStatCheck(SkillKeyStatStatus.Allowed, null), SkillRules.CheckKeyStat(CharacterStat.Perception, 12, basic, tech));
        Assert.AreEqual(new SkillKeyStatCheck(SkillKeyStatStatus.SkillNeedsStat, Skill.DisarmTraps), SkillRules.CheckKeyStat(CharacterStat.Perception, 11, basic, tech));
        Assert.AreEqual(new SkillKeyStatCheck(SkillKeyStatStatus.SkillNeedsStat, Skill.Prowling), SkillRules.CheckKeyStat(CharacterStat.Perception, 8, basic, tech),
            "basic skills are checked first");
        Assert.IsTrue(SkillRules.CheckKeyStat(CharacterStat.Strength, 1, basic, tech).Allowed, "no skill keys on strength");
        basic[(int)Skill.Melee] = 0;
        Assert.IsTrue(SkillRules.CheckKeyStat(CharacterStat.Dexterity, 1, basic, tech).Allowed, "a monster's melee 20 is not bought points");
        Assert.AreEqual(SkillKeyStatStatus.Invalid, SkillRules.CheckKeyStat(CharacterStat.Perception, 12, basic[..11], tech).Status);
        Assert.AreEqual(SkillKeyStatStatus.Invalid, SkillRules.CheckKeyStat(CharacterStat.Perception, 12, basic, null).Status);
        Assert.AreEqual(SkillKeyStatStatus.Invalid, SkillRules.CheckKeyStat((CharacterStat)99, 12, basic, tech).Status);
        // The stat setter asks only when it lowers the stored stat, with the effective value the new base gives.
        Assert.IsTrue(SkillRules.CheckBaseChange(CharacterStat.Perception, 8, 9, 5, basic, tech).Allowed, "raising is never refused");
        Assert.IsTrue(SkillRules.CheckBaseChange(CharacterStat.Perception, 13, 11, 12, basic, tech).Allowed, "an effect keeps 12");
        Assert.AreEqual(Skill.DisarmTraps, SkillRules.CheckBaseChange(CharacterStat.Perception, 13, 12, 11, basic, tech).Blocking);
    }
}
