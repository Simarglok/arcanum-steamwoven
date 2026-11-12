using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

// ARC-MAGIC-053 (ledger SUM6, MG9): the six summon lists of the retail table (0x5B0ED8) and the pick function (0x452900).
[TestClass]
public sealed class SummonListsTests
{
    [TestMethod]
    public void The_table_holds_six_lists_whose_shape_is_the_retail_one()
    {
        Assert.AreEqual(6, Enumerable.Range(0, 20).Count(SummonLists.Exists));
        CollectionAssert.AreEqual(new[] { 6, 6, 2, 4, 6, 4 }, Enumerable.Range(0, 6).Select(n => SummonLists.EntriesOf(n).Count).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, false, true, true, true }, Enumerable.Range(0, 6).Select(SummonLists.ByAptitude).ToArray());
        CollectionAssert.AreEqual(new[] { 100, 120, 100 }, new[] { 0, 1, 2 }.Select(SummonLists.Total).ToArray(), "weighted lists sum to 100, 120 and 100");
        Assert.IsTrue(Enumerable.Range(3, 3).All(n => SummonLists.EntriesOf(n)[^1].Value == 1000), "an aptitude list always finds an entry: its last threshold is 1000");
        Assert.IsFalse(SummonLists.Exists(-1) || SummonLists.Exists(6));
        Assert.AreEqual(0, SummonLists.EntriesOf(7).Count);
        Assert.IsNull(SummonLists.Pick(9, 0, (_, _) => 0));
    }

    [TestMethod]
    public void An_aptitude_list_takes_the_first_threshold_not_below_the_aptitude_and_draws_nothing()
    {
        int Pick(int list, int aptitude) => SummonLists.Pick(list, aptitude, (_, _) => throw new AssertFailedException("no draw"))!.Value;
        Assert.AreEqual(28468, Pick(5, 0));
        Assert.AreEqual(28468, Pick(5, 20), "a threshold equal to the aptitude counts (not below)");
        Assert.AreEqual(28469, Pick(5, 21));
        Assert.AreEqual(28470, Pick(5, 85));
        Assert.AreEqual(28471, Pick(5, 86));
        Assert.AreEqual(28471, Pick(5, 100), "100 under the armed 'spell at maximum' fate");
        Assert.AreEqual(27374, Pick(3, 76));
        Assert.AreEqual(28360, Pick(4, 96));
    }

    [TestMethod]
    public void A_weighted_list_takes_the_entry_whose_cumulative_weight_passes_the_draw_over_its_own_total()
    {
        var asked = new List<(int, int)>();
        int Pick(int list, int roll) => SummonLists.Pick(list, 0, (low, high) => { asked.Add((low, high)); return roll; })!.Value;
        Assert.AreEqual(28319, Pick(0, 0));
        Assert.AreEqual(28319, Pick(0, 19));
        Assert.AreEqual(28360, Pick(0, 20), "the one-weight entry");
        Assert.AreEqual(28329, Pick(0, 21));
        Assert.AreEqual(28334, Pick(0, 99), "the last entry takes the end of the table; the original's draw of 100 falls off to entry 0 (MG9, a technical bug fixed in both modes)");
        Assert.AreEqual((0, 99), asked[0], "the draw spans the table's own total");
        asked.Clear();
        Pick(1, 0);
        Assert.AreEqual((0, 119), asked[0], "list 1 sums to 120: the draw reaches its last entries (the original's 0..100 never did)");
        Assert.AreEqual(28336, Pick(1, 119));
        Assert.AreEqual(28360, Pick(2, 99));
        Assert.AreEqual(28343, Pick(2, 98));
    }
}
