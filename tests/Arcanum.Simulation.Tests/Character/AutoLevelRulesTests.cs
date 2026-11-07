using System.Collections.Immutable;
using System.Text;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// ARC-WORLD-036. Schemes and sheets are invented: a human with every primary stat 8 at level 1, nothing bought.
[TestClass]
public sealed class AutoLevelRulesTests
{
    internal static AdvancementSheet Sheet(int unspent = 10, int level = 1, CharacterRace race = CharacterRace.Human) => new(race, false,
        [.. Enumerable.Repeat(8, 8)], 0, 0, level, unspent, [.. new int[12]], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0);

    private static AutoLevelScheme Scheme(string text)
    {
        var parsed = AutoLevelRules.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(AutoLevelParseStatus.Parsed, parsed.Status, text);
        return parsed.Scheme!;
    }

    private static AutoLevelOutcome Run(AdvancementSheet sheet, string scheme, AdvancementModifiers? modifiers = null, bool npc = false,
        string? standard = null) =>
        AutoLevelRules.Apply(sheet, modifiers ?? AdvancementModifiers.None, Scheme(scheme), standard is null ? null : Scheme(standard), npc,
            AutoLevelRules.DefaultSpellWillpower);

    private static AdvancementModifiers Stats(params StatModifier[] modifiers) => AdvancementModifiers.None with { Stats = [.. modifiers] };

    [TestMethod]
    public void RulesAreTokensAndTargetsInTheSourceOrder()
    {
        var scheme = Scheme("ST 10, dx 12,melee 8, picklock 4, fire 2, mechanical 3, maxhps 40, MaxFatigue 30, wizardry 2");
        CollectionAssert.AreEqual(new AutoLevelStep[]
        {
            new(AdvancementTrack.Stat, 0, 10), new(AdvancementTrack.Stat, 1, 12), new(AdvancementTrack.BasicSkill, 2, 8),
            new(AdvancementTrack.TechSkill, 2, 4), new(AdvancementTrack.College, 4, 2), new(AdvancementTrack.TechDiscipline, 5, 3),
            new(AdvancementTrack.HitPoints, 0, 40), new(AdvancementTrack.Fatigue, 0, 30), new(null, 0, 2),
        }, scheme.Steps.ToArray());
        Assert.AreEqual(0, scheme.IgnoredTargetText);
        // A missing comma: the target is read up to the first non-digit and the rest of the step is lost, as atoi does.
        var swallowed = Scheme("st 12 melee 4, dx 9");
        CollectionAssert.AreEqual(new AutoLevelStep[] { new(AdvancementTrack.Stat, 0, 12), new(AdvancementTrack.Stat, 1, 9) }, swallowed.Steps.ToArray());
        Assert.AreEqual(1, swallowed.IgnoredTargetText);
        Assert.AreEqual(0, Scheme("st x").Steps[0].Target);
        Assert.AreEqual(-3, Scheme("st  -3 ").Steps[0].Target);
        Assert.AreEqual(0, Scheme("").Steps.Length);

        Assert.AreEqual(AutoLevelParseStatus.MissingTarget, AutoLevelRules.Parse("st 10, dx"u8).Status);
        Assert.AreEqual(AutoLevelParseStatus.InvalidText, AutoLevelRules.Parse("st\t10"u8).Status);
        Assert.AreEqual(AutoLevelParseStatus.InvalidTarget, AutoLevelRules.Parse("st 1234567890"u8).Status);
        Assert.AreEqual(AutoLevelParseStatus.InvalidTarget, AutoLevelRules.Parse("fire 6"u8).Status);
        Assert.AreEqual(AutoLevelParseStatus.InvalidTarget, AutoLevelRules.Parse("smithy 8"u8).Status);
        Assert.AreEqual(AutoLevelParseStatus.Parsed, AutoLevelRules.Parse("fire 5, smithy 7"u8).Status);
    }

    [TestMethod]
    public void ASkillLoopComparesTheLevelReadBeforeAKeyStatRaise()
    {
        // Melee +8 from an effect, capped at 7 by dexterity 8: one point keeps it at 7, the second is above the cap, so
        // dexterity rises to 9; the loop still holds level 7 and buys the second point (ledger AL5). A re-read would stop at one.
        var boosted = AdvancementModifiers.None with { BasicSkills = [new SkillModifier(2, StatModifierOperator.Add, 8)] };
        var run = Run(Sheet(10), "melee 8", boosted);
        Assert.AreEqual((2, 9, 7, true), (run.Sheet.BasicSkills[2] & AutoLevelRules.SkillPointMask, run.Sheet.Stats[1], run.Sheet.UnspentPoints,
            run.Completed));
    }

    [TestMethod]
    public void AMissingStandardRuleLeavesAnNpcRunNotCompleted()
    {
        var run = AutoLevelRules.Apply(Sheet(3), AdvancementModifiers.None, Scheme("st 9"), null, npc: true, AutoLevelRules.DefaultSpellWillpower);
        Assert.AreEqual((9, false), (run.Sheet.Stats[0], run.Completed));
        Assert.IsTrue(AutoLevelRules.Apply(Sheet(3), AdvancementModifiers.None, Scheme("st 9"), null, npc: false, AutoLevelRules.DefaultSpellWillpower).Completed);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AutoLevelRules.SpellMinimumLevel(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AutoLevelRules.SpellMinimumLevel(AutoLevelRules.Spells));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AutoLevelRules.DegreeMinimumIntelligence(AutoLevelRules.MaxDegree + 1));
        Assert.AreEqual((15, 19), (AutoLevelRules.SpellMinimumLevel(79), AutoLevelRules.DegreeMinimumIntelligence(7)));
    }

    [TestMethod]
    public void StatsAreBoughtFromTheirBaseValue()
    {
        var bought = Run(Sheet(3), "st 10");
        Assert.AreEqual((10, 1, true), (bought.Sheet.Stats[0], bought.Sheet.UnspentPoints, bought.Completed));
        CollectionAssert.AreEqual(new AdvancementPurchase[] { new(AdvancementTrack.Stat, 0, 9), new(AdvancementTrack.Stat, 0, 10) },
            bought.Purchases.ToArray());

        // An effect that lowers the stat does not make the loop pay again: it stops at the base target (ledger AL4).
        var lowered = Run(Sheet(5), "st 10", Stats(new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, -1)));
        Assert.AreEqual((10, 3), (lowered.Sheet.Stats[0], lowered.Sheet.UnspentPoints));
        Assert.AreEqual(9, lowered.Purchases[^1].Value, "the log shows the effective value");

        // Running out of points ends the rule; the later step does not run.
        var short_ = Run(Sheet(1), "st 10, dx 9");
        Assert.AreEqual((9, 8, 0, false), (short_.Sheet.Stats[0], short_.Sheet.Stats[1], short_.Sheet.UnspentPoints, short_.Completed));

        // The race maximum fails the step, and the rule goes on.
        var capped = Run(Sheet(20), "st 25, dx 9");
        Assert.AreEqual((20, 9, true), (capped.Sheet.Stats[0], capped.Sheet.Stats[1], capped.Completed));
        Assert.AreEqual(20 - 12 - 1, capped.Sheet.UnspentPoints);

        Assert.AreEqual((0, 0, 1, 1), (AutoLevelRules.StatCost(0), AutoLevelRules.StatCost(1), AutoLevelRules.StatCost(2), AutoLevelRules.StatCost(25)));
    }

    [TestMethod]
    public void ASkillTheKeyStatRefusesRaisesTheKeyStatFirst()
    {
        // Dexterity 8 allows 7: the first point (level 4) is bought, the second needs dexterity 9.
        var melee = Run(Sheet(5), "melee 8");
        CollectionAssert.AreEqual(new AdvancementPurchase[]
        {
            new(AdvancementTrack.BasicSkill, 2, 1), new(AdvancementTrack.Stat, 1, 9), new(AdvancementTrack.BasicSkill, 2, 2),
        }, melee.Purchases.ToArray());
        Assert.AreEqual((2, 9, 2), (melee.Sheet.BasicSkills[2], melee.Sheet.Stats[1], melee.Sheet.UnspentPoints));
        Assert.AreEqual(8, AutoLevelRules.BasicSkillLevel(melee.Sheet, AdvancementModifiers.None, 2));

        // Training bits stay; a monstrous critter's melee is 20 already; a skill effect counts in levels.
        var trained = Sheet(5) with { BasicSkills = [.. new[] { 0x41 }.Concat(new int[11])] };
        Assert.AreEqual(0x42, Run(trained, "bow 8").Sheet.BasicSkills[0]);
        Assert.AreEqual(0, Run(Sheet(5) with { Monstrous = true, Stats = [8, 18, 8, 8, 8, 8, 8, 8] }, "melee 8").Purchases.Length);
        var boosted = AdvancementModifiers.None with { BasicSkills = [new(2, StatModifierOperator.Add, 4)] };
        Assert.AreEqual(1, Run(Sheet(5) with { Stats = [8, 9, 8, 8, 8, 8, 8, 8] }, "melee 8", boosted).Sheet.BasicSkills[2]);

        // A tech skill moves the tech points; dexterity at its maximum fails the step.
        var lockpick = Run(Sheet(5), "picklock 4");
        Assert.AreEqual((1, 1, 4), (lockpick.Sheet.TechSkills[2], lockpick.Sheet.TechPoints, lockpick.Sheet.UnspentPoints));
        var maxed = Sheet(50) with { Stats = [8, 20, 8, 8, 8, 8, 8, 8] };
        var tooHigh = Run(maxed, "melee 20, wp 9");
        Assert.AreEqual((5, 9, true), (tooHigh.Sheet.BasicSkills[2], tooHigh.Sheet.Stats[6], tooHigh.Completed));
        Assert.AreEqual(8, AutoLevelRules.TechSkillLevel(Sheet() with { TechSkills = [0, 0, 2, 0], Stats = [8, 9, 8, 8, 8, 8, 8, 8] },
            AdvancementModifiers.None, 2));
    }

    [TestMethod]
    public void SpellsNeedTheirLevelIntelligenceAndWillpower()
    {
        // Fire rank 2 needs willpower 9: the helper buys it first; magick points rise by one per spell.
        var fire = Run(Sheet(5), "fire 2");
        Assert.AreEqual((2, 2, 9, 2), (fire.Sheet.Colleges[4], fire.Sheet.MagickPoints, fire.Sheet.Stats[6], fire.Sheet.UnspentPoints));
        CollectionAssert.AreEqual(new AdvancementPurchase[]
        {
            new(AdvancementTrack.College, 4, 1), new(AdvancementTrack.Stat, 6, 9), new(AdvancementTrack.College, 4, 2),
        }, fire.Purchases.ToArray());
        // Rank 3 needs level 5: the step fails and the rule goes on.
        var early = Run(Sheet(10), "fire 3, st 9");
        Assert.AreEqual((2, 9, true), (early.Sheet.Colleges[4], early.Sheet.Stats[0], early.Completed));
        Assert.AreEqual(3, Run(Sheet(10, level: 5), "fire 3").Sheet.Colleges[4]);
        // Intelligence below 5 is raised first.
        var dull = Sheet(5) with { Stats = [8, 8, 8, 8, 4, 8, 8, 8] };
        var learned = Run(dull, "air 1");
        Assert.AreEqual((5, 1, 3), (learned.Sheet.Stats[4], learned.Sheet.Colleges[2], learned.Sheet.UnspentPoints));
        // A record's own willpower minimum wins over the default.
        var strict = AutoLevelRules.DefaultSpellWillpower.SetItem(20, 12);
        var hard = AutoLevelRules.Apply(Sheet(10), AdvancementModifiers.None, Scheme("fire 1"), null, false, strict);
        Assert.AreEqual((12, 1), (hard.Sheet.Stats[6], hard.Sheet.Colleges[4]));

        Assert.AreEqual((6, 9, 18, 9), (AutoLevelRules.DefaultSpellMinimumWillpower(0), AutoLevelRules.DefaultSpellMinimumWillpower(21),
            AutoLevelRules.DefaultSpellMinimumWillpower(79), AutoLevelRules.DefaultSpellMinimumWillpower(80)));
        Assert.AreEqual((1, 1, 5, 10, 15), (AutoLevelRules.SpellMinimumLevel(0), AutoLevelRules.SpellMinimumLevel(1),
            AutoLevelRules.SpellMinimumLevel(2), AutoLevelRules.SpellMinimumLevel(3), AutoLevelRules.SpellMinimumLevel(4)));
    }

    [TestMethod]
    public void TechDegreesNeedTheirIntelligence()
    {
        var two = Run(Sheet(5), "mechanical 2");
        Assert.AreEqual((2, 2, 3), (two.Sheet.TechDegrees[5], two.Sheet.TechPoints, two.Sheet.UnspentPoints));
        // Degree 3 needs intelligence 11: three intelligence units first.
        var three = Run(Sheet(10), "mechanical 3");
        Assert.AreEqual((3, 11, 3, 4), (three.Sheet.TechDegrees[5], three.Sheet.Stats[4], three.Sheet.TechPoints, three.Sheet.UnspentPoints));
        Assert.AreEqual((0, 5, 8, 19), (AutoLevelRules.DegreeMinimumIntelligence(0), AutoLevelRules.DegreeMinimumIntelligence(1),
            AutoLevelRules.DegreeMinimumIntelligence(2), AutoLevelRules.DegreeMinimumIntelligence(7)));
    }

    [TestMethod]
    public void HitAndFatiguePointsAddFourToTheirMaximum()
    {
        // Maximum hit points of the sheet: 8 + 2 × (8 + 1) + 4 = 30; each point adds 4.
        var tough = Run(Sheet(5), "maxhps 40, maxfatigue 31");
        Assert.AreEqual((3, 1, 1), (tough.Sheet.HitPointPoints, tough.Sheet.FatiguePoints, tough.Sheet.UnspentPoints));
        CollectionAssert.AreEqual(new AdvancementPurchase[]
        {
            new(AdvancementTrack.HitPoints, 0, 34), new(AdvancementTrack.HitPoints, 0, 38), new(AdvancementTrack.HitPoints, 0, 42),
            new(AdvancementTrack.Fatigue, 0, 34),
        }, tough.Purchases.ToArray());
        // A cap on the maximum fails the step; a point is still spent before the check, as the source writes first.
        var capped = AdvancementModifiers.None with { MaxHitPoints = [new(StatModifierOperator.Maximum, 30)] };
        var none = Run(Sheet(5), "maxhps 40, st 9", capped);
        Assert.AreEqual((1, 9, 4, true), (none.Sheet.HitPointPoints, none.Sheet.Stats[0], none.Sheet.UnspentPoints, none.Completed));
    }

    [TestMethod]
    public void AnNpcRunsTheStandardSchemeAfterItsOwnCompletes()
    {
        var npc = Run(Sheet(3), "st 9", npc: true, standard: "dx 10");
        Assert.AreEqual((9, 10, true), (npc.Sheet.Stats[0], npc.Sheet.Stats[1], npc.Completed));
        var pc = Run(Sheet(3), "st 9", npc: false, standard: "dx 10");
        Assert.AreEqual((9, 8), (pc.Sheet.Stats[0], pc.Sheet.Stats[1]));
        var stopped = Run(Sheet(1), "st 10", npc: true, standard: "dx 10");
        Assert.AreEqual((9, 8, false), (stopped.Sheet.Stats[0], stopped.Sheet.Stats[1], stopped.Completed));
        var unknown = Run(Sheet(5), "st 9, nothing 3, dx 9", npc: true, standard: "cn 9");
        Assert.AreEqual((9, 8, 8, false), (unknown.Sheet.Stats[0], unknown.Sheet.Stats[1], unknown.Sheet.Stats[2], unknown.Completed));

        var idle = AutoLevelRules.Apply(Sheet(3), AdvancementModifiers.None, null, null, true, AutoLevelRules.DefaultSpellWillpower);
        Assert.AreEqual((false, 0, 3), (idle.Completed, idle.Purchases.Length, idle.Sheet.UnspentPoints));
    }

    [TestMethod]
    public void InvalidInputIsRefused()
    {
        Assert.IsTrue(AutoLevelRules.IsValid(Sheet()));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet() with { Stats = [.. Enumerable.Repeat(8, 7)] }));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet() with { Stats = [21, 8, 8, 8, 8, 8, 8, 8] }));
        Assert.IsTrue(AutoLevelRules.IsValid(Sheet(race: CharacterRace.HalfOgre) with { Stats = [24, 8, 8, 8, 8, 8, 8, 8] }));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet(57)));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet() with { Colleges = [6, .. new int[15]] }));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet() with { TechDegrees = [8, .. new int[7]] }));
        Assert.IsFalse(AutoLevelRules.IsValid(Sheet() with { HitPointPoints = AutoLevelRules.MaxPoolValue + 1 }));
        Assert.IsFalse(AutoLevelRules.IsValid(AdvancementModifiers.None with { PoisonLevel = 1001 }));
        Assert.IsFalse(AutoLevelRules.IsValid(AdvancementModifiers.None with { BasicSkills = [new(12, StatModifierOperator.Add, 1)] }));
        Assert.IsFalse(AutoLevelRules.IsValid(AdvancementModifiers.None with { MaxFatigue = [new(StatModifierOperator.Divide, 0)] }));
        Assert.ThrowsExactly<ArgumentException>(() => AutoLevelRules.Apply(Sheet(57), AdvancementModifiers.None, null, null, false,
            AutoLevelRules.DefaultSpellWillpower));
        Assert.ThrowsExactly<ArgumentException>(() => AutoLevelRules.Apply(Sheet(), AdvancementModifiers.None, null, null, false,
            ImmutableArray<int>.Empty));
    }

    // ARC-UI-088: the derived stats of a critter without a stat object, from its sheet's effective stats and its effects.
    [TestMethod]
    public void DerivedStatsFollowTheEffectiveStatsAndTheEffectsOfThatStat()
    {
        var sheet = Sheet() with { Stats = [10, 8, 8, 8, 8, 8, 8, 12] };
        var none = AdvancementModifiers.None;
        Assert.AreEqual(5000, AutoLevelRules.DerivedStat(sheet, none, CharacterStat.CarryWeight), "500 × strength 10");
        Assert.AreEqual(0, AutoLevelRules.DerivedStat(sheet, none, CharacterStat.DamageBonus), "strength 10");
        Assert.AreEqual(-2, AutoLevelRules.DerivedStat(sheet, none, CharacterStat.AcAdjustment), "dexterity 8 − 10");
        Assert.AreEqual(3, AutoLevelRules.DerivedStat(sheet, none, CharacterStat.MaxFollowers), "charisma 12 / 4");
        var stronger = Stats(new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, 4));
        Assert.AreEqual(7000, AutoLevelRules.DerivedStat(sheet, stronger, CharacterStat.CarryWeight), "the strength effect counts before the derived value");
        var faster = Stats(new StatModifier(CharacterStat.Speed, StatModifierOperator.Add, 3));
        Assert.AreEqual(11, AutoLevelRules.DerivedStat(sheet, faster, CharacterStat.Speed), "8 + 3: an effect on the derived stat itself");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AutoLevelRules.DerivedStat(sheet, none, CharacterStat.Strength));
    }
}
