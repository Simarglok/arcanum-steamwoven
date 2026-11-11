using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-005: the resistance, the saving throw and the predictor (ledger RS1–RS10). Records and targets are invented.
[TestClass]
public sealed class SpellResistanceRulesTests
{
    private static readonly SpellResistRun Begin = new(Begin: true, Unresistable: false, FateMaximized: false);

    // A non-damaging, non-maintained spell with a willpower save +2; an NPC with willpower 10.
    private static SpellResistRecord Charm(int resistStat = SpellResistanceRules.Willpower, bool damaging = false, int period = 0) =>
        new(10, false, damaging, true, period, resistStat, 2);

    private static SpellResistTarget Npc(int resistance = 30, int aptitude = 0, int stat = 10, bool extraordinary = false,
        SpellTargetKind kind = SpellTargetKind.Npc, bool caster = false) => new(caster, kind, false, resistance, aptitude, stat, extraordinary);

    private sealed class Scripted(params int[] values) : ISpellRollSource
    {
        private int _next;

        public List<(SpellRollPurpose Purpose, int Min, int Max)> Made { get; } = [];

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            Made.Add((purpose, min, max));
            return values[_next++];
        }
    }

    private sealed class Armed(bool armed) : ISpellFateSave
    {
        public int Asked { get; private set; }

        public bool ConsumeSaveAgainstMagick()
        {
            Asked++;
            return armed;
        }
    }

    [TestMethod]
    public void TheWorkedExampleScalesTheResistanceForANegativeAptitude()
    {
        // Resistance 30 and aptitude −40: R = 100 − 70 × 60 / 100 = 58; a d100 of 20 gives margin 38.
        var rolls = new Scripted(20, 20);
        var result = SpellResistanceRules.Resist(Charm() with { Damaging = true }, Npc(30, -40) with { ResistStatValue = 0 }, Begin, rolls,
            SpellFateSave.None, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 38, 38, 0), (result.Applies, result.Margin, result.DisplayMargin, result.Penalty),
            "a damaging spell has no penalty; S = 2, the d20 20 fails the save");
        Assert.AreEqual(2, rolls.Made.Count);
        var fated = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave) with { Damaging = true }, Npc(30, -40), Begin with { FateMaximized = true },
            new Scripted(20), SpellFateSave.None, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual(10, fated.Margin, "a fate-maximised action reads the stored 30");
    }

    [TestMethod]
    public void Mg4SubtractsTheResistanceTermFromTheSaveOnlyInTheOriginalMode()
    {
        // R 50, d100 10: margin 40, penalty 4; willpower 10 + 2: Original S = 8, Steamwoven S = 16; a d20 of 12 saves only in Steamwoven.
        SpellResistance Resolve(MagicAnomalyProfile profile) =>
            SpellResistanceRules.Resist(Charm(), Npc(50), Begin, new Scripted(10, 12), SpellFateSave.None, profile);
        var original = Resolve(MagicAnomalyProfile.Original);
        Assert.AreEqual((true, 40, 4), (original.Applies, original.Margin, original.Penalty), "8 < 12: no save, the spell applies");
        var steamwoven = Resolve(MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, 44, 44), (steamwoven.Applies, steamwoven.Margin, steamwoven.DisplayMargin), "16 ≥ 12: saved, margin + 4, skipped");
        Assert.AreEqual(MagicAnomalyProfile.Original, MagicAnomalyProfile.FromName("original"));
        Assert.IsNull(MagicAnomalyProfile.FromName("other"));
    }

    [TestMethod]
    public void WithoutASaveAResistedNonDamagingSpellIsSkipped()
    {
        var resisted = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave), Npc(30), Begin, new Scripted(29), SpellFateSave.None,
            MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, 1), (resisted.Applies, resisted.Margin));
        var hit = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave), Npc(30), Begin, new Scripted(30), SpellFateSave.None,
            MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 0), (hit.Applies, hit.Margin), "a roll equal to R does not resist");
        var damaging = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave, damaging: true), Npc(30), Begin, new Scripted(29),
            SpellFateSave.None, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 1), (damaging.Applies, damaging.Margin), "a damaging spell applies with the margin");
    }

    [TestMethod]
    public void ASavedDamagingSpellAppliesWithAtLeastFiftyAndAMaintainedOneApplies()
    {
        var damaging = SpellResistanceRules.Resist(Charm(damaging: true), Npc(0), Begin, new Scripted(50, 3), SpellFateSave.None,
            MagicAnomalyProfile.Original);
        Assert.AreEqual((true, 50, 50), (damaging.Applies, damaging.Margin, damaging.DisplayMargin), "margin 0 + 12 − 3 = 9, raised to 50");
        var maintained = SpellResistanceRules.Resist(Charm(period: 5), Npc(0), Begin, new Scripted(50, 3), SpellFateSave.None,
            MagicAnomalyProfile.Original);
        Assert.AreEqual((true, 9), (maintained.Applies, maintained.Margin));
        var plain = SpellResistanceRules.Resist(Charm(), Npc(0), Begin, new Scripted(50, 3), SpellFateSave.None, MagicAnomalyProfile.Original);
        Assert.AreEqual((false, 9), (plain.Applies, plain.Margin));
        var failed = SpellResistanceRules.Resist(Charm(), Npc(0, stat: -5), Begin, new Scripted(50), SpellFateSave.None, MagicAnomalyProfile.Original);
        Assert.AreEqual(true, failed.Applies, "S ≤ 0: no save is rolled");
    }

    [TestMethod]
    public void TheFateSaveGivesMarginTenAndTheSaveStillRolls()
    {
        var fate = new Armed(true);
        var rolls = new Scripted(19);
        var pc = SpellResistanceRules.Resist(Charm(), Npc(kind: SpellTargetKind.Pc), Begin, rolls, fate, MagicAnomalyProfile.Original);
        Assert.AreEqual((true, 10, 100, 1), (pc.Applies, pc.Margin, pc.DisplayMargin, fate.Asked), "MG13: no d100, the d20 19 > 12 fails");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.SaveD20, 1, 20) }, rolls.Made);
        var npc = new Armed(true);
        SpellResistanceRules.Resist(Charm(), Npc(), Begin, new Scripted(50, 20), npc, MagicAnomalyProfile.Original);
        Assert.AreEqual(0, npc.Asked, "only a PC target's fate is asked");
        var caster = new Armed(true);
        Assert.IsTrue(SpellResistanceRules.Resist(Charm(), Npc(kind: SpellTargetKind.Pc, caster: true), Begin, new Scripted(), caster,
            MagicAnomalyProfile.Original).Applies);
        Assert.AreEqual(0, caster.Asked, "the caster is never resisted");
    }

    [TestMethod]
    public void EarlyExitsLeaveTheMarginsAndExtraordinaryWillpowerShrugsOff()
    {
        foreach (var (record, run) in new[] { (Charm(), Begin with { Begin = false }), (Charm() with { Technology = true }, Begin),
                     (Charm(), Begin with { Unresistable = true }) })
        {
            var untouched = SpellResistanceRules.Resist(record, Npc(), run, new Scripted(), SpellFateSave.None, MagicAnomalyProfile.Original);
            Assert.AreEqual((true, (int?)null, (int?)null), (untouched.Applies, untouched.Margin, untouched.DisplayMargin));
        }
        var disintegrate = SpellResistanceRules.Resist(Charm() with { Spell = SpellResistanceRules.Disintegrate }, Npc() with { NoDisintegrate = true },
            Begin, new Scripted(), SpellFateSave.None, MagicAnomalyProfile.Original);
        Assert.AreEqual((false, 0), (disintegrate.Applies, disintegrate.Margin));
        var extraordinary = SpellResistanceRules.Resist(Charm(), Npc(0, extraordinary: true), Begin, new Scripted(50), SpellFateSave.None,
            MagicAnomalyProfile.Original);
        Assert.AreEqual((false, true), (extraordinary.Applies, extraordinary.NotifiesAi));
        var otherStat = SpellResistanceRules.Resist(Charm(resistStat: 4), Npc(0, extraordinary: true), Begin, new Scripted(50, 20),
            SpellFateSave.None, MagicAnomalyProfile.Original);
        Assert.AreEqual(true, otherStat.Applies, "only a willpower save is shrugged off");
        var item = SpellResistanceRules.Resist(Charm(), Npc(kind: SpellTargetKind.Other), Begin, new Scripted(50), SpellFateSave.None,
            MagicAnomalyProfile.Original);
        Assert.AreEqual(true, item.Applies, "no saving throw for an object");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellResistanceRules.Resist(Charm(), Npc(kind: (SpellTargetKind)7), Begin,
            new Scripted(), SpellFateSave.None, MagicAnomalyProfile.Original));
    }

    [TestMethod]
    public void ThePredictorRollsItsOwnD20AndAnswersWithTheResistance()
    {
        var rolls = new Scripted(12);
        Assert.AreEqual(50, SpellResistanceRules.FailChance(Charm(damaging: true), Npc(30, stat: 10), false, rolls, MagicAnomalyProfile.Original),
            "saved (S 12 ≥ 12): a damaging spell answers at least 50");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.FailChanceD20, 1, 20) }, rolls.Made);
        Assert.AreEqual(100, SpellResistanceRules.FailChance(Charm(SpellResistanceRules.NoSave), Npc(41), false, new Scripted(), MagicAnomalyProfile.Original));
        Assert.AreEqual(40, SpellResistanceRules.FailChance(Charm(SpellResistanceRules.NoSave), Npc(40), false, new Scripted(), MagicAnomalyProfile.Original));
        Assert.AreEqual(100, SpellResistanceRules.FailChance(Charm(), Npc(0, extraordinary: true), false, new Scripted(), MagicAnomalyProfile.Original));
        Assert.AreEqual(0, SpellResistanceRules.FailChance(Charm() with { Technology = true }, Npc(90), false, new Scripted(), MagicAnomalyProfile.Original));
        Assert.AreEqual(0, SpellResistanceRules.FailChance(Charm(), Npc(90, caster: true), false, new Scripted(), MagicAnomalyProfile.Original));
        // R 50: penalty 5; willpower 10 + 2: Original S 7, Steamwoven S 17; a d20 of 10 saves only in Steamwoven (then 100).
        Assert.AreEqual(50, SpellResistanceRules.FailChance(Charm(), Npc(50), false, new Scripted(10), MagicAnomalyProfile.Original));
        Assert.AreEqual(100, SpellResistanceRules.FailChance(Charm(), Npc(50), false, new Scripted(10), MagicAnomalyProfile.Steamwoven));
        Assert.AreEqual(7, SpellResistanceRules.ItemFailChance(true, 3, spell => spell + 4));
        Assert.AreEqual(0, SpellResistanceRules.ItemFailChance(false, 3, spell => spell + 4));
        Assert.AreEqual(0, SpellResistanceRules.ItemFailChance(true, -1, spell => spell + 4));
    }
    // ARC-MAGIC-033 (owner decision D6c): the save reads the stored magic resistance under the target's resistmagic changes, in both modes.
    [TestMethod]
    public void The_resistance_under_effects_is_the_stored_value_without_a_change_and_the_aggregation_with_one()
    {
        Assert.AreEqual(100, SpellResistanceRules.ResistanceUnderEffects(100, [], false), "an immune value is kept, never cut to 95 when nothing changes it");
        Assert.AreEqual(-7, SpellResistanceRules.ResistanceUnderEffects(-7, [], false));
        EffectModifier Add(int v) => new(StatModifierOperator.Add, v);
        Assert.AreEqual(65, SpellResistanceRules.ResistanceUnderEffects(40, [Add(25)], false), "Resist Magick adds");
        Assert.AreEqual(90, SpellResistanceRules.ResistanceUnderEffects(40, [Add(25), Add(25)], false), "the entries are summed");
        Assert.AreEqual(95, SpellResistanceRules.ResistanceUnderEffects(40, [Add(80)], false), "a PC and an ordinary NPC are cut at 95");
        Assert.AreEqual(0, SpellResistanceRules.ResistanceUnderEffects(10, [Add(-50)], false), "a curse takes it down to 0");
        Assert.AreEqual(-30, SpellResistanceRules.ResistanceUnderEffects(-30, [Add(-10)], false), "a curse never makes a negative stored value more resistant");
        Assert.AreEqual(-5, SpellResistanceRules.ResistanceUnderEffects(-30, [Add(25)], false), "a blessing lifts it, as the sum says");
        Assert.AreEqual(100, SpellResistanceRules.ResistanceUnderEffects(100, [Add(10)], false), "a blessing never lowers an already higher stored value");
        Assert.AreEqual(60, SpellResistanceRules.ResistanceUnderEffects(100, [Add(-40)], false), "a curse lowers it");
        Assert.AreEqual(300, SpellResistanceRules.ResistanceUnderEffects(100, [Add(200)], true), "a monstrous NPC is not cut at 95");
        Assert.AreEqual(1000, SpellResistanceRules.ResistanceUnderEffects(100, [Add(5000)], true), "only the resolver's bound cuts it");
        Assert.AreEqual(150, SpellResistanceRules.ResistanceUnderEffects(100, [new(StatModifierOperator.Percent, 50)], true), "the percent form works on the stored value");
        Assert.AreEqual(70, SpellResistanceRules.ResistanceUnderEffects(40, [new(StatModifierOperator.Minimum, 70)], false), "a minimum lifts it");
    }

    [TestMethod]
    public void Resist_Magick_turns_a_landing_spell_into_a_resisted_one_in_both_modes()
    {
        // A spell with no save, a d100 of 45: the stored 40 does not stop it; under Resist Magick (+25 = 65) the roll is below the resistance, margin 20, and the spell is skipped.
        var blessed = SpellResistanceRules.ResistanceUnderEffects(40, [new(StatModifierOperator.Add, 25)], false);
        foreach (var profile in new[] { MagicAnomalyProfile.Original, MagicAnomalyProfile.Steamwoven })
        {
            var plain = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave), Npc(40), Begin, new Scripted(45), SpellFateSave.None, profile);
            Assert.AreEqual((true, 0), (plain.Applies, plain.Margin), profile.Name);
            var resisted = SpellResistanceRules.Resist(Charm(SpellResistanceRules.NoSave), Npc(blessed), Begin, new Scripted(45), SpellFateSave.None, profile);
            Assert.AreEqual((false, 20), (resisted.Applies, resisted.Margin), profile.Name);
        }
    }
}
