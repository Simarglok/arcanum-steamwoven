using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-026: the maintain bar's rules (0x57C290–0x57C520, ledger MB1–MB9). Runs and spells are invented.
[TestClass]
public sealed class MaintainBarRulesTests
{
    private static SpellRun Run(int id, int spell) =>
        new(id, spell, SpellRunAction.Maintain, null, null, SpellLocation.None, null, SpellLocation.None, null, SpellRunFlags.Active);

    [TestMethod]
    public void Open_slots_are_intelligence_over_four_capped_at_five_Mg12()
    {
        Assert.AreEqual(0, MaintainBarRules.OpenSlots(3));
        Assert.AreEqual(1, MaintainBarRules.OpenSlots(4));
        Assert.AreEqual(3, MaintainBarRules.OpenSlots(15));
        Assert.AreEqual(5, MaintainBarRules.OpenSlots(20));
        Assert.AreEqual(5, MaintainBarRules.OpenSlots(28), "MG12: never past the five slots");
        Assert.AreEqual(0, MaintainBarRules.OpenSlots(-4));
    }

    [TestMethod]
    public void A_run_takes_the_first_empty_open_slot_and_a_shown_run_stays()
    {
        var (bar, added) = MaintainBarRules.Add(MaintainBar.Empty, 7, 12, 12);
        Assert.IsTrue(added);
        Assert.AreEqual(new MaintainSlot(7, MaintainSlotState.Shown), bar[0]);
        (bar, added) = MaintainBarRules.Add(bar, 9, 30, 12);
        Assert.AreEqual((true, 9), (added, bar[1].Run));
        var again = MaintainBarRules.Add(bar, 7, 12, 12);
        Assert.IsTrue(again.Added && ReferenceEquals(bar, again.Bar), "a shown run stays where it is");
        bar = MaintainBarRules.Remove(bar, 7);
        Assert.AreEqual(MaintainSlot.Empty, bar[0]);
        (bar, _) = MaintainBarRules.Add(bar, 11, 40, 12);
        Assert.AreEqual(11, bar[0].Run, "the first empty slot from the left");
    }

    [TestMethod]
    public void A_full_bar_and_a_spell_outside_the_bound_are_refused_Mg12_fixed()
    {
        var bar = MaintainBar.Empty;
        for (var run = 0; run < 2; run++) bar = MaintainBarRules.Add(bar, run, 1, 8).Bar;
        var full = MaintainBarRules.Add(bar, 5, 1, 8);
        Assert.IsFalse(full.Added, "INT 8: two open slots, both taken (the original writes slot -1)");
        Assert.IsTrue(full.Bar.SameAs(bar));
        Assert.IsFalse(MaintainBarRules.Add(MaintainBar.Empty, 1, 1, 3).Added, "no open slot at INT 3");
        Assert.IsFalse(MaintainBarRules.Add(MaintainBar.Empty, 1, MaintainBarRules.SpellBound, 20).Added, "0x459FF0: spell 223 and above");
        Assert.IsFalse(MaintainBarRules.Add(MaintainBar.Empty, 1, -1, 20).Added);
        Assert.IsTrue(MaintainBarRules.Add(MaintainBar.Empty, 1, MaintainBarRules.SpellBound - 1, 20).Added);
    }

    [TestMethod]
    public void A_click_stops_a_shown_slot_once_and_its_removal_frees_it()
    {
        var bar = MaintainBarRules.Add(MaintainBar.Empty, 4, 12, 12).Bar;
        Assert.IsNull(MaintainBarRules.Stop(bar, 1), "an empty slot");
        Assert.IsNull(MaintainBarRules.Stop(bar, 5));
        Assert.AreEqual(4, MaintainBarRules.Hovered(bar, 0));
        var (stopping, run) = MaintainBarRules.Stop(bar, 0)!.Value;
        Assert.AreEqual((4, MaintainSlotState.Stopping), (run, stopping[0].State));
        Assert.IsNull(MaintainBarRules.Stop(stopping, 0), "a stopping slot takes no second click");
        Assert.IsNull(MaintainBarRules.Hovered(stopping, 0), "nor a hover");
        // A stopping slot does not count as showing its run: a maintenance tick would add it again (the original's test of state 1).
        var twice = MaintainBarRules.Add(stopping, 4, 12, 12).Bar;
        Assert.AreEqual((4, MaintainSlotState.Shown), (twice[1].Run, twice[1].State));
        Assert.AreEqual(MaintainSlot.Empty, MaintainBarRules.Remove(stopping, 4)[0], "the run's end frees the stopping slot");
        Assert.IsTrue(MaintainBarRules.Remove(stopping, -1).SameAs(stopping));
    }

    [TestMethod]
    public void Notes_apply_in_order_and_a_freed_runs_slot_empties()
    {
        var runs = new Dictionary<int, SpellRun> { [3] = Run(3, 12), [8] = Run(8, 20) };
        SpellRun? Of(int id) => runs.GetValueOrDefault(id);
        var bar = MaintainBarRules.Add(MaintainBar.Empty, 3, 12, 8).Bar;
        bar = MaintainBarRules.Add(bar, 5, 13, 8).Bar;
        // INT 8: run 5 (gone from the table) is pruned; run 3 leaves before run 8 comes, so run 8 takes slot 0.
        var after = MaintainBarRules.Apply(bar, [new(SpellRunNoteKind.MaintainBar, 3), new(SpellRunNoteKind.MaintainBarAdd, 8)], Of, 8);
        Assert.AreEqual((8, MaintainSlotState.Shown), (after[0].Run, after[0].State));
        Assert.AreEqual(MaintainSlot.Empty, after[1]);
        Assert.AreEqual(MaintainSlot.Empty, MaintainBarRules.Apply(MaintainBar.Empty, [new(SpellRunNoteKind.MaintainBarAdd, 9)], Of, 8)[0],
            "an add of a run no longer there adds nothing");
    }
}
