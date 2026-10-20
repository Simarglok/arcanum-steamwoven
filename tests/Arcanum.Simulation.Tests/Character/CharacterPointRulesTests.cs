using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.AutoLevelRulesTests;

namespace Arcanum.Simulation.Tests.Character;

// ARC-WORLD-052: the shared point pool (ledger CP1–CP12). Sheets are invented: a human with every primary stat 8 at level 1,
// nothing bought (AutoLevelRulesTests.Sheet); the floor is the sheet the editor opened on.
[TestClass]
public sealed class CharacterPointRulesTests
{
    private static readonly ImmutableArray<int> Willpower = AutoLevelRules.DefaultSpellWillpower;

    private static PointChange Spend(AdvancementSheet sheet, AdvancementTrack track, int index, AdvancementModifiers? modifiers = null,
        IPointLane? lane = null, int maxPool = AutoLevelRules.MaxPoolValue) =>
        CharacterPointRules.Spend(sheet, modifiers ?? AdvancementModifiers.None, track, index, Willpower, lane, maxPool);

    private static PointChange Refund(AdvancementSheet sheet, AdvancementSheet floor, AdvancementTrack track, int index,
        SkillRefundClick click = SkillRefundClick.UntilLevelChanges, AdvancementModifiers? modifiers = null, IPointLane? lane = null) =>
        CharacterPointRules.Refund(sheet, floor, modifiers ?? AdvancementModifiers.None, track, index, click, Willpower, lane);

    private static AdvancementSheet WithStat(AdvancementSheet sheet, CharacterStat stat, int value) =>
        sheet with { Stats = sheet.Stats.SetItem((int)stat, value) };

    private static AdvancementSheet WithBasic(AdvancementSheet sheet, Skill skill, int points) =>
        sheet with { BasicSkills = sheet.BasicSkills.SetItem((int)skill, points) };

    private static AdvancementModifiers StatChange(CharacterStat stat, int value) =>
        AdvancementModifiers.None with { Stats = [new(stat, StatModifierOperator.Add, value)] };

    private static AdvancementModifiers SkillChange(Skill skill, int value) =>
        AdvancementModifiers.None with { BasicSkills = [new((int)skill, StatModifierOperator.Add, value)] };

    [TestMethod]
    public void AStatRisesByItsCostAndRefundsDownToTheFloor()
    {
        var floor = Sheet(unspent: 2);
        var raised = Spend(floor, AdvancementTrack.Stat, (int)CharacterStat.Strength);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 9, 1), (raised.Status, raised.Points, raised.Value, raised.Sheet.UnspentPoints));
        Assert.AreEqual(9, raised.Sheet.Stats[0]);

        var back = Refund(raised.Sheet, floor, AdvancementTrack.Stat, 0);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 8, 2), (back.Status, back.Points, back.Value, back.Sheet.UnspentPoints));
        Assert.AreEqual(floor, back.Sheet);
        var atFloor = Refund(back.Sheet, floor, AdvancementTrack.Stat, 0);
        Assert.AreEqual((PointChangeStatus.AtFloor, 0, 8), (atFloor.Status, atFloor.Points, atFloor.Value));
        Assert.AreSame(back.Sheet, atFloor.Sheet, "a refusal returns the sheet it was given");
        Assert.AreEqual(PointChangeStatus.AtFloor, Refund(floor, WithStat(floor, CharacterStat.Strength, 12), AdvancementTrack.Stat, 0).Status,
            "below the floor is at the floor (Steamwoven compares at or below)");

        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Spend(Sheet(unspent: 0), AdvancementTrack.Stat, 0).Status);
    }

    [TestMethod]
    public void TheStatPlusTestsTheEffectiveValueWhileTheHintTestsTheBase()
    {
        var sheet = WithStat(Sheet(), CharacterStat.Strength, 18);
        var boosted = StatChange(CharacterStat.Strength, 2);
        Assert.AreEqual(PointChangeStatus.AtMaximum, Spend(sheet, AdvancementTrack.Stat, 0, boosted).Status, "effective 20, the human maximum");
        Assert.AreEqual(1, CharacterPointRules.NextStatCost(sheet, CharacterStat.Strength), "the hint still shows the base's next cost (CP3)");

        var top = WithStat(Sheet(unspent: 0), CharacterStat.Strength, 20);
        var drained = StatChange(CharacterStat.Strength, -2);
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Spend(top, AdvancementTrack.Stat, 0, drained).Status, "the cost is tested before the setter");
        Assert.AreEqual(PointChangeStatus.AtMaximum, Spend(top with { UnspentPoints = 1 }, AdvancementTrack.Stat, 0, drained).Status,
            "the setter clamps to the race's maximum");
        Assert.AreEqual(0, CharacterPointRules.NextStatCost(top, CharacterStat.Strength));
        Assert.AreEqual(1, CharacterPointRules.NextStatCost(WithStat(Sheet(), CharacterStat.Strength, 1), CharacterStat.Strength));
        Assert.AreEqual(PointChangeStatus.Changed, Spend(WithStat(Sheet(), CharacterStat.Strength, 20) with { Race = CharacterRace.Ogre },
            AdvancementTrack.Stat, 0).Status, "an ogre's strength goes to 26");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CharacterPointRules.NextStatCost(Sheet(), CharacterStat.MagickPoints));
    }

    [TestMethod]
    public void LoweringAStatPassesTheSkillSpellAndDegreeGuards()
    {
        var floor = WithStat(Sheet(), CharacterStat.Dexterity, 8);
        var bow = WithBasic(WithStat(floor, CharacterStat.Dexterity, 9), Skill.Bow, 2);
        var blocked = Refund(bow, floor, AdvancementTrack.Stat, (int)CharacterStat.Dexterity);
        Assert.AreEqual((PointChangeStatus.SkillNeedsStat, (CharacterStat?)CharacterStat.Dexterity, (Skill?)Skill.Bow, 9),
            (blocked.Status, blocked.Stat, blocked.Skill, blocked.Value), "dexterity 8 caps bow at 7, below its 8");

        var intFloor = WithStat(Sheet(), CharacterStat.Intelligence, 4);
        var mage = WithStat(intFloor, CharacterStat.Intelligence, 5) with { Colleges = intFloor.Colleges.SetItem(3, 1) };
        Assert.AreEqual(PointChangeStatus.SpellNeedsStat, Refund(mage, intFloor, AdvancementTrack.Stat, 4).Status, "a known rank needs 5");
        var tech = WithStat(Sheet(), CharacterStat.Intelligence, 8) with { TechDegrees = intFloor.TechDegrees.SetItem(6, 2) };
        Assert.AreEqual(PointChangeStatus.DegreeNeedsStat, Refund(tech, WithStat(Sheet(), CharacterStat.Intelligence, 7),
            AdvancementTrack.Stat, 4).Status, "degree 2 needs 8");
        var lowered = Refund(tech with { TechDegrees = tech.TechDegrees.SetItem(6, 1) }, WithStat(Sheet(), CharacterStat.Intelligence, 7),
            AdvancementTrack.Stat, 4);
        Assert.AreEqual((PointChangeStatus.Changed, 7), (lowered.Status, lowered.Value), "degree 1 needs 5");

        var wpFloor = WithStat(Sheet(), CharacterStat.Willpower, 5);
        var willed = WithStat(wpFloor, CharacterStat.Willpower, 6) with { Colleges = wpFloor.Colleges.SetItem(0, 1) };
        var refused = Refund(willed, wpFloor, AdvancementTrack.Stat, 6);
        Assert.AreEqual((PointChangeStatus.SpellNeedsStat, (CharacterStat?)CharacterStat.Willpower), (refused.Status, refused.Stat),
            "the first rank needs willpower 6");
        Assert.AreEqual(PointChangeStatus.Changed, Refund(willed, wpFloor, AdvancementTrack.Stat, 6,
            modifiers: StatChange(CharacterStat.Willpower, 1)).Status, "the guard reads the effective value");
    }

    [TestMethod]
    public void HitAndFatiguePointsCostOneAndStopAtTheBound()
    {
        var floor = Sheet(unspent: 1);
        var hp = Spend(floor, AdvancementTrack.HitPoints, 0);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1, 0), (hp.Status, hp.Points, hp.Sheet.HitPointPoints, hp.Sheet.UnspentPoints));
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Spend(hp.Sheet, AdvancementTrack.Fatigue, 0).Status);
        Assert.AreEqual(PointChangeStatus.AtMaximum, Spend(floor with { FatiguePoints = 100 }, AdvancementTrack.Fatigue, 0, maxPool: 100).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(floor, AdvancementTrack.Fatigue, 0, maxPool: AutoLevelRules.MaxPoolValue + 1).Status);

        var back = Refund(hp.Sheet, floor, AdvancementTrack.HitPoints, 0);
        Assert.AreEqual((PointChangeStatus.Changed, 0, 1), (back.Status, back.Sheet.HitPointPoints, back.Sheet.UnspentPoints));
        Assert.AreEqual(PointChangeStatus.AtFloor, Refund(back.Sheet, floor, AdvancementTrack.HitPoints, 0).Status);
    }

    [TestMethod]
    public void ASkillPointIsRefusedAtLevelTwentyOrForItsKeyStat()
    {
        var bought = Spend(Sheet(), AdvancementTrack.BasicSkill, (int)Skill.Bow);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1, 9), (bought.Status, bought.Points, bought.Value, bought.Sheet.UnspentPoints));
        var capped = Spend(bought.Sheet, AdvancementTrack.BasicSkill, (int)Skill.Bow);
        Assert.AreEqual((PointChangeStatus.KeyStatTooLow, (CharacterStat?)CharacterStat.Dexterity, 1), (capped.Status, capped.Stat, capped.Value),
            "dexterity 8 caps at 7: two points (8) are too many");
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Spend(Sheet(unspent: 0), AdvancementTrack.BasicSkill, 0).Status);

        var master = WithBasic(WithStat(Sheet(), CharacterStat.Dexterity, 20), Skill.Bow, 5);
        Assert.AreEqual(PointChangeStatus.AtMaximum, Spend(master, AdvancementTrack.BasicSkill, 0).Status, "level 20");
        Assert.AreEqual(PointChangeStatus.KeyStatTooLow, Spend(WithBasic(Sheet(), Skill.Bow, 1), AdvancementTrack.BasicSkill, 0,
            SkillChange(Skill.Bow, 20)).Status, "an effect does not lift the level past the key stat's cap (7), so the refusal names the key stat");

        var tech = Spend(Sheet(), AdvancementTrack.TechSkill, SkillRules.FamilyIndex(Skill.Repair));
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1), (tech.Status, tech.Sheet.TechSkills[0], tech.Sheet.TechPoints), "a tech point also");
    }

    [TestMethod]
    public void TheOriginalClickRefundsUntilTheLevelChangesAndStopsAtTheFloor()
    {
        var dx20 = WithStat(Sheet(unspent: 0), CharacterStat.Dexterity, 20);
        var three = WithBasic(dx20, Skill.Bow, 3);
        var plain = Refund(three, dx20, AdvancementTrack.BasicSkill, 0);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 2, 1), (plain.Status, plain.Points, plain.Value, plain.Sheet.UnspentPoints),
            "12 → 8: the level changes after one point");

        // An effect pins the level: +20 keeps it at 20 for every point count, so the original's click would take all three (CP5).
        var pinned = SkillChange(Skill.Bow, 20);
        var floorOne = WithBasic(dx20, Skill.Bow, 1);
        var loop = Refund(three, floorOne, AdvancementTrack.BasicSkill, 0, modifiers: pinned);
        Assert.AreEqual((PointChangeStatus.Changed, 2, 1, 2), (loop.Status, loop.Points, loop.Value, loop.Sheet.UnspentPoints),
            "two points, stopped at the floor");
        var all = Refund(three, dx20, AdvancementTrack.BasicSkill, 0, modifiers: SkillChange(Skill.Bow, -20));
        Assert.AreEqual((3, 0), (all.Points, all.Value), "a floor of 0: down to no points");
        var one = Refund(three, floorOne, AdvancementTrack.BasicSkill, 0, SkillRefundClick.OnePoint, pinned);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 2), (one.Status, one.Points, one.Value));
        Assert.AreEqual(PointChangeStatus.Invalid, Refund(three, dx20, AdvancementTrack.BasicSkill, 0, (SkillRefundClick)2).Status);
    }

    [TestMethod]
    public void ARefundTheKeyStatNoLongerAllowsIsRefusedAndATechSkillRefundsOnePoint()
    {
        // Five points bought at dexterity 20; an effect now holds dexterity at 8 (cap 7): four points would still be 16 > 7.
        var floor = WithStat(Sheet(), CharacterStat.Dexterity, 20);
        var five = WithBasic(floor, Skill.Bow, 5);
        var refused = Refund(five, floor, AdvancementTrack.BasicSkill, 0, modifiers: StatChange(CharacterStat.Dexterity, -12));
        Assert.AreEqual((PointChangeStatus.KeyStatTooLow, (CharacterStat?)CharacterStat.Dexterity, 5), (refused.Status, refused.Stat, refused.Value));

        var techFloor = Sheet(unspent: 0);
        var tech = techFloor with { TechSkills = techFloor.TechSkills.SetItem(1, 1 | 1 << 6), TechPoints = 1 };
        var back = Refund(tech, techFloor, AdvancementTrack.TechSkill, 1);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1 << 6, 0, 1), (back.Status, back.Points, back.Sheet.TechSkills[1], back.Sheet.TechPoints,
            back.Sheet.UnspentPoints), "the training bits stay");
        Assert.AreEqual(PointChangeStatus.AtFloor, Refund(back.Sheet, techFloor, AdvancementTrack.TechSkill, 1).Status);
    }

    [TestMethod]
    public void RefundsNeverRaiseTheUnspentPointsPastTheirMaximum()
    {
        var floor = Sheet(unspent: ExperienceRules.MaxUnspentPoints);
        var back = Refund(WithStat(floor, CharacterStat.Strength, 9), floor, AdvancementTrack.Stat, 0);
        Assert.AreEqual((PointChangeStatus.Changed, ExperienceRules.MaxUnspentPoints), (back.Status, back.Sheet.UnspentPoints));
    }

    private sealed class Ranks(int cost = 1, bool touchUnspent = false, PointChangeStatus refuse = PointChangeStatus.Changed) : IPointLane
    {
        public PointLaneStep Raise(AdvancementSheet sheet, AdvancementModifiers modifiers, int index) => Step(sheet, index, +1);

        public PointLaneStep Lower(AdvancementSheet sheet, AdvancementModifiers modifiers, int index) => Step(sheet, index, -1);

        private PointLaneStep Step(AdvancementSheet sheet, int index, int by) => refuse != PointChangeStatus.Changed ? new(refuse, sheet, 0)
            : new(PointChangeStatus.Changed, sheet with
            {
                Colleges = sheet.Colleges.SetItem(index, sheet.Colleges[index] + by), MagickPoints = sheet.MagickPoints + by,
                TechDegrees = sheet.TechDegrees.SetItem(index % 8, sheet.TechDegrees[index % 8] + by),
                UnspentPoints = touchUnspent ? 0 : sheet.UnspentPoints,
            }, cost);
    }

    [TestMethod]
    public void SpellRanksAndDegreesGoThroughTheirLaneAndKeepTheFloor()
    {
        var floor = Sheet(unspent: 2) with { Colleges = Sheet().Colleges.SetItem(2, 1), MagickPoints = 1, TechDegrees = Sheet().TechDegrees.SetItem(2, 1) };
        Assert.AreEqual(PointChangeStatus.LaneNotReady, Spend(floor, AdvancementTrack.College, 2).Status);
        Assert.AreEqual(PointChangeStatus.LaneNotReady, Refund(floor with { Colleges = floor.Colleges.SetItem(2, 2) }, floor,
            AdvancementTrack.College, 2).Status);

        var learned = Spend(floor, AdvancementTrack.College, 2, lane: new Ranks());
        Assert.AreEqual((PointChangeStatus.Changed, 1, 2, 1), (learned.Status, learned.Points, learned.Value, learned.Sheet.UnspentPoints));
        var unlearned = Refund(learned.Sheet, floor, AdvancementTrack.College, 2, lane: new Ranks());
        Assert.AreEqual((PointChangeStatus.Changed, 1, 2), (unlearned.Status, unlearned.Value, unlearned.Sheet.UnspentPoints));
        Assert.AreEqual(PointChangeStatus.AtFloor, Refund(unlearned.Sheet, floor, AdvancementTrack.College, 2, lane: new Ranks()).Status);

        // TC10: the original sells a degree below the value the editor opened with; the pool keeps the floor in both modes.
        Assert.AreEqual(PointChangeStatus.AtFloor, Refund(floor, floor, AdvancementTrack.TechDiscipline, 2, lane: new Ranks()).Status);

        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Spend(floor, AdvancementTrack.College, 2, lane: new Ranks(cost: 3)).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(floor, AdvancementTrack.College, 2, lane: new Ranks(touchUnspent: true)).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(floor, AdvancementTrack.College, 2, lane: new Ranks(cost: -1)).Status);
        Assert.AreEqual(PointChangeStatus.SpellNeedsStat, Spend(floor, AdvancementTrack.College, 2,
            lane: new Ranks(refuse: PointChangeStatus.SpellNeedsStat)).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(floor, AdvancementTrack.College, 2, lane: new Ranks(refuse: (PointChangeStatus)99)).Status);
    }

    [TestMethod]
    public void InputsOutsideTheDomainAreInvalid()
    {
        var sheet = Sheet();
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(sheet, AdvancementTrack.Stat, 8).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(sheet, AdvancementTrack.HitPoints, 1).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(sheet, (AdvancementTrack)7, 0).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(sheet, AdvancementTrack.BasicSkill, -1).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Spend(sheet with { UnspentPoints = 57 }, AdvancementTrack.Stat, 0).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, CharacterPointRules.Spend(sheet, AdvancementModifiers.None, AdvancementTrack.Stat, 0, [1, 2]).Status);
        Assert.AreEqual(PointChangeStatus.Invalid, Refund(sheet, sheet with { Stats = [] }, AdvancementTrack.Stat, 0).Status);
        Assert.AreEqual(0, Spend(sheet, (AdvancementTrack)7, 0).Value);
        Assert.AreEqual(0, CharacterPointRules.Indices((AdvancementTrack)7));
    }
}
