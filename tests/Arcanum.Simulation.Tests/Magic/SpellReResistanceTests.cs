using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-DEV-303 (ledger RS10): the entangle's re-resistance on each hit (0x459C10). Records and targets are invented; the entangle's own numbers are used where the installed record is meant
// (spell 51: a strength save, modifier −1, a maintenance period of 2).
[TestClass]
public sealed class SpellReResistanceTests
{
    // Entangle: spell 51, no damage, aggressive, maintained every 2, strength (stat 0) at −1.
    private static readonly SpellResistRecord Entangle = new(51, false, false, true, 2, 0, -1);

    private static SpellResistTarget Npc(int resistance = 30, int aptitude = 0, int stat = 10, bool extraordinary = false, SpellTargetKind kind = SpellTargetKind.Npc,
        bool caster = false, bool noDisintegrate = false) => new(caster, kind, noDisintegrate, resistance, aptitude, stat, extraordinary);

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

    private static SpellReResistance Run(SpellResistRecord record, SpellResistTarget target, ISpellRollSource rolls, SpellTargetKind? cached = null, bool unresistable = false,
        ISpellFateSave? fate = null, MagicAnomalyProfile? profile = null) =>
        SpellResistanceRules.ReResist(record, target, cached ?? target.Kind, unresistable, rolls, fate ?? SpellFateSave.None, profile ?? MagicAnomalyProfile.Steamwoven);

    [TestMethod]
    public void A_made_strength_save_resists_again_and_a_missed_one_keeps_the_hold()
    {
        // Strength 10 at −1: S = 9. The d100 is rolled first (it is only a margin for the penalty of a non-maintained record), then the d20.
        var made = new Scripted(50, 9);
        Assert.AreEqual(new SpellReResistance(false, false), Run(Entangle, Npc(), made), "a d20 of 9 is at most the save: resisted");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.ResistanceD100, 1, 100), (SpellRollPurpose.SaveD20, 1, 20) }, made.Made);
        Assert.AreEqual(new SpellReResistance(true, false), Run(Entangle, Npc(), new Scripted(50, 10)), "a d20 of 10 is above the save: the hold stays");
    }

    [TestMethod]
    public void A_save_of_nothing_never_resists_and_rolls_no_d20()
    {
        var rolls = new Scripted(50);
        Assert.AreEqual(new SpellReResistance(true, false), Run(Entangle, Npc(stat: 1), rolls), "S = 0");
        Assert.AreEqual(1, rolls.Made.Count, "only the d100");
    }

    [TestMethod]
    public void A_technology_record_or_an_unresistable_run_holds_without_rolling()
    {
        var rolls = new Scripted();
        Assert.IsTrue(Run(Entangle with { Technology = true }, Npc(), rolls).Holds);
        Assert.IsTrue(Run(Entangle, Npc(), rolls, unresistable: true).Holds);
        Assert.AreEqual(0, rolls.Made.Count);
    }

    [TestMethod]
    public void The_caster_and_a_record_without_a_save_hold_unless_the_resistance_roll_resists()
    {
        var rolls = new Scripted();
        Assert.IsTrue(Run(Entangle, Npc(caster: true), rolls).Holds, "the caster: no roll at all");
        Assert.AreEqual(0, rolls.Made.Count);
        // No save and a record that is not maintained: R 30, a d100 of 29 is below it and resists; 30 holds.
        var plain = Entangle with { ResistStat = SpellResistanceRules.NoSave, MaintainPeriod = 0 };
        Assert.IsFalse(Run(plain, Npc(), new Scripted(29)).Holds);
        Assert.IsTrue(Run(plain, Npc(), new Scripted(30)).Holds);
        // The same record maintained: the roll decides nothing and there is no save.
        Assert.IsTrue(Run(plain with { MaintainPeriod = 2 }, Npc(), new Scripted(1)).Holds);
    }

    [TestMethod]
    public void The_save_part_reads_the_cached_type_not_the_critters_own()
    {
        var rolls = new Scripted(50);
        Assert.IsTrue(Run(Entangle, Npc(), rolls, cached: SpellTargetKind.Other).Holds, "a cached type that is no critter's skips the save (MG48)");
        Assert.AreEqual(1, rolls.Made.Count, "the d100 was still rolled, the d20 was not");
        Assert.IsFalse(Run(Entangle, Npc(), new Scripted(50, 1), cached: SpellTargetKind.Pc).Holds, "a cached PC counts as a critter");
    }

    [TestMethod]
    public void A_pcs_armed_fate_save_skips_the_resistance_roll_but_still_saves()
    {
        var fate = new Armed(true);
        var rolls = new Scripted(1);
        Assert.IsFalse(Run(Entangle, Npc(kind: SpellTargetKind.Pc), rolls, fate: fate).Holds, "the fate sent it to the save: a d20 of 1 resists");
        Assert.AreEqual(1, fate.Asked);
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.SaveD20, 1, 20) }, rolls.Made, "no d100");
        var npcFate = new Armed(true);
        Run(Entangle, Npc(), new Scripted(50, 1), fate: npcFate);
        Assert.AreEqual(0, npcFate.Asked, "an NPC has no fate");
    }

    [TestMethod]
    public void The_penalty_of_a_resisted_roll_follows_the_mg4_switch_for_a_record_that_is_not_maintained()
    {
        // R 50 with a d100 of 10: margin 40, penalty 4. Strength 10 at −1: S = 5 in the Original setting (the penalty is subtracted), 13 in Steamwoven.
        var plain = Entangle with { MaintainPeriod = 0 };
        var target = Npc(resistance: 50);
        Assert.IsTrue(Run(plain, target, new Scripted(10, 10), profile: MagicAnomalyProfile.Original).Holds, "S = 5: a d20 of 10 is above it");
        Assert.IsFalse(Run(plain, target, new Scripted(10, 10), profile: MagicAnomalyProfile.Steamwoven).Holds, "S = 13: a d20 of 10 resists");
        // The maintained entangle takes no penalty at all.
        Assert.IsFalse(Run(Entangle, target, new Scripted(10, 9), profile: MagicAnomalyProfile.Original).Holds, "S = 9 in both settings");
    }

    [TestMethod]
    public void A_negative_aptitude_raises_the_resistance_and_disintegrate_against_no_disintegrate_resists()
    {
        // Resistance 30, aptitude −40: R = 58. A non-maintained record without a save resists on a d100 of 57.
        var plain = Entangle with { ResistStat = SpellResistanceRules.NoSave, MaintainPeriod = 0 };
        Assert.IsFalse(Run(plain, Npc(30, -40), new Scripted(57)).Holds);
        Assert.IsTrue(Run(plain, Npc(30, -40), new Scripted(58)).Holds);
        var rolls = new Scripted();
        Assert.IsFalse(Run(Entangle with { Spell = 34 }, Npc(noDisintegrate: true), rolls).Holds);
        Assert.AreEqual(0, rolls.Made.Count);
    }

    [TestMethod]
    public void An_extraordinary_willpower_resists_and_tells_the_ai_of_an_aggressive_record()
    {
        var charm = Entangle with { ResistStat = SpellResistanceRules.Willpower };
        Assert.AreEqual(new SpellReResistance(false, true), Run(charm, Npc(extraordinary: true, stat: 20), new Scripted(50)));
        Assert.AreEqual(new SpellReResistance(false, false), Run(charm with { Aggressive = false }, Npc(extraordinary: true, stat: 20), new Scripted(50)));
    }
}
