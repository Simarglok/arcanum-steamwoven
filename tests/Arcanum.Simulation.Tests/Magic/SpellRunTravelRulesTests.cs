using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-058 (MT9b, ledger MX1-MX6; 0x44F7A0): which runs go with the player's change of map. Objects and runs are invented.
[TestClass]
public sealed class SpellRunTravelRulesTests
{
    private static readonly SpellRunObject Hero = new("pc", SpellObjectFacts.PcType), Friend = new("oid:friend", SpellObjectFacts.NpcType), Stranger = new("oid:stranger", SpellObjectFacts.NpcType);
    private static readonly SpellRunObject Wand = new("oid:wand", 10), Rock = new("oid:rock", 3);

    private static SpellRun Run(int id, SpellRunObject? source, SpellRunObject? parent, SpellRunObject? target, SpellRunObject? other = null, SpellRunFlags flags = SpellRunFlags.Active) =>
        new(id, 1, SpellRunAction.Maintain, source, parent, SpellLocation.None, target, SpellLocation.None, other, flags);

    private static bool Travels(string id) => id is "pc" or "oid:friend";

    private static IEnumerable<string> NoSummoned(SpellRun run) => [];

    private static int[] Left(params SpellRun[] runs) => SpellRunTravelRules.RunsLeft(runs, Travels, NoSummoned).ToArray();

    [TestMethod]
    public void A_run_whose_source_parent_and_target_all_travel_goes_and_any_other_ends()
    {
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(0, Hero, Hero, Hero), Run(1, Hero, Hero, Friend)));
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, Left(Run(2, Hero, Hero, Stranger), Run(3, Stranger, Hero, Hero), Run(4, Hero, Stranger, Hero)), "target, source and parent each decide");
    }

    [TestMethod]
    public void A_run_naming_no_object_goes_and_a_parent_that_is_the_source_is_tested_once()
    {
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(0, null, null, null)), "a null handle passes every test");
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(1, null, null, Friend)));
        CollectionAssert.AreEqual(new[] { 2 }, Left(Run(2, Stranger, Stranger, null)), "the source that stays decides, whatever the parent");
        CollectionAssert.AreEqual(new[] { 3 }, Left(Run(3, null, Stranger, null)), "a parent with no source is tested on its own");
    }

    [TestMethod]
    public void The_other_object_is_not_tested_and_a_run_not_in_use_goes()
    {
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(0, Hero, Hero, Hero, other: Stranger)), "0x44F7A0 never reads +0xE8");
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(1, Stranger, Stranger, Stranger, flags: SpellRunFlags.None)), "a free slot is skipped");
    }

    [TestMethod]
    public void An_item_goes_with_its_holder_and_the_summoned_list_is_tested_as_well()
    {
        CollectionAssert.AreEqual(Array.Empty<int>(), Left(Run(0, Wand, Hero, null)), "an item the hero holds travels with it");
        CollectionAssert.AreEqual(new[] { 1 }, Left(Run(1, Wand, Stranger, null)), "an item a stranger holds stays");
        CollectionAssert.AreEqual(new[] { 2 }, Left(Run(2, Rock, Hero, Hero)), "a scenery source stays behind");
        CollectionAssert.AreEqual(new[] { 4 }, Left(Run(4, Wand, Wand, null)), "an item with no holder (its own parent, lying about) stays");
        var summon = Run(3, Hero, Hero, Hero);
        Assert.IsTrue(SpellRunTravelRules.Goes(summon, Travels, _ => ["oid:friend"]));
        Assert.IsFalse(SpellRunTravelRules.Goes(summon, Travels, _ => ["oid:friend", "oid:imp"]), "a summoned critter that stays ends the run");
    }

    [TestMethod]
    public void The_ids_come_in_slot_order()
    {
        CollectionAssert.AreEqual(new[] { 1, 5, 9 }, Left(Run(1, Stranger, null, null), Run(3, Hero, Hero, Hero), Run(5, null, null, Stranger), Run(9, Hero, Stranger, Hero)));
    }
}
