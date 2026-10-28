using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

// ARC-UI-050: merchant buy lists of rules\InvenSourceBuy.mes as the original's load reads them (ledger BT8). Synthetic sets and numbers.
[TestClass]
public sealed class InventorySourceBuyRulesTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    private static readonly MesFile Sources = Mes("{1}{Smith: 10,20}{2}{Grocer: 5,5 100,6029}{3}{No colon 1,1}{4}{Fence: -50,50}{5}{Bad item: 1,1 100,5000}{7}{Gap: 1,1}");
    private static readonly HashSet<int> Prototypes = [6029, 6030, 8042, 17067];
    private static bool Exists(int basic) => Prototypes.Contains(basic);

    private static InventorySourceBuyList Line(string text) => InventorySourceBuyRules.ParseLine(Encoding.ASCII.GetBytes(text), Exists);

    [TestMethod]
    public void All_in_any_case_buys_everything_and_a_list_buys_its_basic_numbers()
    {
        foreach (var all in new[] { "ALL", "all", "All", " all  6029", "aLl x" })
            Assert.AreEqual(InventorySourceBuyStatus.Everything, Line(all).Status, all);
        var listed = Line("6029  8042 17067");
        Assert.AreEqual(InventorySourceBuyStatus.Listed, listed.Status);
        CollectionAssert.AreEqual(new[] { 6029, 8042, 17067 }, listed.BasicPrototypes.ToArray());
        Assert.IsTrue(listed.Deals(8042));
        Assert.IsFalse(listed.Deals(6030));
        Assert.IsTrue(Line("ALL").Deals(1));
    }

    [TestMethod]
    public void Only_spaces_separate_and_tokens_read_as_atoi()
    {
        CollectionAssert.AreEqual(new[] { 6029, 8042 }, Line("6029,6030 8042x").BasicPrototypes.ToArray(), "a comma or a letter stops the number");
        CollectionAssert.AreEqual(new[] { 6029 }, Line("6029\t8042").BasicPrototypes.ToArray(), "a tab does not separate: one token, read as 6029");
        Assert.AreEqual(InventorySourceBuyStatus.Listed, Line("\t6029").Status, "atoi skips leading white space");
        foreach (var bad in new[] { "6029 ALL", "6029 x", "0", "-6029", "999", "5000", "6179", "28472", "6031" })
            Assert.AreEqual(InventorySourceBuyStatus.Emptied, Line(bad).Status, bad);
        Assert.AreEqual(InventorySourceBuyStatus.None, Line("   ").Status, "a line of spaces buys nothing");
    }

    [TestMethod]
    public void Three_hundred_numbers_fit_and_a_line_of_2000_characters_empties_the_set()
    {
        var three = string.Join(' ', Enumerable.Repeat("6029", 300));
        Assert.AreEqual(InventorySourceBuyStatus.Listed, Line(three).Status);
        Assert.AreEqual(InventorySourceBuyStatus.Emptied, Line(three + " 6029").Status);
        Assert.AreEqual(InventorySourceBuyStatus.Listed, Line(new string(' ', 1995) + "6029").Status, "1999 characters");
        Assert.AreEqual(InventorySourceBuyStatus.Emptied, Line(new string(' ', 1996) + "6029").Status, "2000 characters");
    }

    [TestMethod]
    public void A_set_reads_the_buy_message_of_its_number_and_unreachable_or_failing_loot_sets_buy_nothing()
    {
        var buy = Mes("{4}{6029}{1}{all}{2}{6030}{3}{ALL}{5}{ALL}{9}{ALL}");
        Assert.AreEqual(InventorySourceBuyStatus.Everything, InventorySourceBuyRules.Read(Sources, buy, 1, Exists).Status, "order does not matter");
        Assert.AreEqual(InventorySourceBuyStatus.Listed, InventorySourceBuyRules.Read(Sources, buy, 2, Exists).Status);
        Assert.AreEqual(InventorySourceBuyStatus.Everything, InventorySourceBuyRules.Read(Sources, buy, 3, Exists).Status, "no colon: an empty set, not a failure");
        Assert.AreEqual(InventorySourceBuyStatus.Listed, InventorySourceBuyRules.Read(Sources, buy, 4, Exists).Status, "negative coins are not checked");
        Assert.AreEqual(InventorySourceBuyStatus.Emptied, InventorySourceBuyRules.Read(Sources, buy, 5, Exists).Status, "the loot line fails");
        Assert.AreEqual(InventorySourceBuyStatus.None, InventorySourceBuyRules.Read(Sources, buy, 6, Exists).Status, "message 6 is the gap");
        Assert.AreEqual(InventorySourceBuyStatus.None, InventorySourceBuyRules.Read(Sources, buy, 7, Exists).Status, "past the message count");
        Assert.AreEqual(InventorySourceBuyStatus.None, InventorySourceBuyRules.Read(Sources, buy, 0, Exists).Status);
        Assert.AreEqual(InventorySourceBuyStatus.None, InventorySourceBuyRules.Read(Sources, null, 1, Exists).Status, "no buy file");
        var last = Mes("{1}{a: 1,1}{2}{b: 1,1}");
        Assert.AreEqual(InventorySourceBuyStatus.Everything, InventorySourceBuyRules.Read(last, Mes("{2}{ALL}"), 2, Exists).Status, "the last set is in range");
    }

    [TestMethod]
    public void A_loot_line_fails_only_on_length_name_an_incomplete_pair_or_a_bad_item()
    {
        bool Fails(string text) => InventorySourceBuyRules.LootLineFails(Encoding.ASCII.GetBytes(text), Exists);
        foreach (var good in new[] { "Smith: 10,20", "No colon at all", ":: name", "Name:", "Coins: -5,5 -3,6029", "Third: 1,2,3 100,6029,7",
                     "Smith: 1,1 100,8042x", "Trailing: 1,1 100,6029 ", "      " })
            Assert.IsFalse(Fails(good), good);
        foreach (var bad in new[] { "", ":::", new string('n', 70) + ": 1,1", "Trail: 1,1 50,", "Two spaces: 1,1 100,6029  ", "Item: 1,1 100,6031",
                     "Item: 1,1 100,abc", "Item: 1,1 100,-6029", new string(' ', 1990) + "x: 1,1 1" })
            Assert.IsTrue(Fails(bad), bad);
        // The shortest item pair takes 7 characters, so the 2000-character limit comes before the 301st item.
        var many = "Many: 1,1" + string.Concat(Enumerable.Repeat(" 1,6029", 284));
        Assert.IsFalse(Fails(many), many.Length.ToString());
        Assert.IsTrue(Fails(many + " 1,6029"), "2001 characters");
    }
}
