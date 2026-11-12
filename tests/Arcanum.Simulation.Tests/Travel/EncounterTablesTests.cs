using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Travel;

/// <summary>ARC-WORLD-1159 (E2-c, ledger PO13, PO14): the encounter tables as the original's loader reads <c>rules/wmap_rnd.mes</c>. Invented tables, none of the installed ones.</summary>
[TestClass]
public sealed class EncounterTablesTests
{
    internal static List<(int Number, string Text)> Minimal(params (int Number, string Text)[] extra)
    {
        var list = new List<(int, string)>
        {
            (10000, "100, 200, 50, 20%"), (10001, "5000, 5000, 100, 4%"),
            (20000, "100, 200, 50, Powerful"), (20001, "5000, 5000, 100, Easy"),
            (30000, "700, 700, 10, 3"),
            (49999, "0"),
        };
        list.AddRange(extra);
        return list;
    }

    private static EncounterTables Parse(params (int Number, string Text)[] rows) => EncounterTables.Parse(Minimal(rows));

    [TestMethod]
    public void The_charts_and_the_fifty_four_terrain_tables_load()
    {
        var tables = Parse((50000, "80%, First: 5001, 1-4, MaxLevel: 10"), (50001, "30%, First: 5002, 1, Second: 5003, 2-3, MinLevel: 3, MaxLevel: 9, GlobalFlag: 77, TriggerCount: 1"));
        Assert.AreEqual(EncounterTablesStatus.Loaded, tables.Status);
        CollectionAssert.AreEqual(new[] { new EncounterRegion(100, 200, 50, 20), new EncounterRegion(5000, 5000, 100, 4) }, tables.Frequency.ToArray());
        CollectionAssert.AreEqual(new[] { 3, 1 }, new[] { tables.Power[0].Value, tables.Power[1].Value }, "powerful is 3, easy 1 (the names are read without case)");
        Assert.AreEqual(3, tables.Override.Single().Value);
        Assert.AreEqual(54, tables.Tables.Count, "the count line 0 adds nothing to the 54 terrain tables");
        var rows = tables.Tables[0];
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(50000, rows[0].Line);
        Assert.AreEqual((80, 0, 10, -1, -1), (rows[0].Weight, rows[0].MinLevel, rows[0].MaxLevel, rows[0].GlobalFlag, rows[0].TriggerCap));
        CollectionAssert.AreEqual(new[] { new EncounterGroup(5001, 1, 4) }, rows[0].Groups.ToArray());
        CollectionAssert.AreEqual(new[] { new EncounterGroup(5002, 1, 1), new EncounterGroup(5003, 2, 3) }, rows[1].Groups.ToArray());
        Assert.AreEqual((30, 3, 9, 77, 1), (rows[1].Weight, rows[1].MinLevel, rows[1].MaxLevel, rows[1].GlobalFlag, rows[1].TriggerCap));
    }

    [TestMethod]
    public void The_count_line_adds_tables_and_an_absent_table_is_empty()
    {
        var messages = Minimal().Where(m => m.Number != 49999).Append((49999, "2")).ToList();
        var tables = EncounterTables.Parse(messages);
        Assert.AreEqual(56, tables.Tables.Count);
        Assert.AreEqual(0, tables.Tables[55].Count);
    }

    [TestMethod]
    public void A_misspelt_slot_name_ends_the_groups_and_leaves_every_later_key_unread()
    {
        // The retail file spells a third slot "Thrid:": the loader reads a key only at its cursor, so the level range and the flag after it are lost.
        var tables = Parse((50000, "50%, First: 7, 1, Second: 8, 2, Thrid: 9, 1, MinLevel: 15, MaxLevel: 45, GlobalFlag: 2627"));
        var row = tables.Tables[0].Single();
        Assert.AreEqual(2, row.Groups.Count);
        Assert.AreEqual((0, EncounterTables.DefaultMaxLevel, -1), (row.MinLevel, row.MaxLevel, row.GlobalFlag));
    }

    [TestMethod]
    public void Keys_must_come_in_the_loaders_order()
    {
        var row = Parse((50000, "50%, First: 7, 1, MaxLevel: 40, MinLevel: 3")).Tables[0].Single();
        Assert.AreEqual((0, 40), (row.MinLevel, row.MaxLevel), "MinLevel after MaxLevel is not at the cursor any more");
    }

    [TestMethod]
    public void A_group_range_with_one_number_is_a_fixed_size_and_the_last_value_may_end_the_text()
    {
        var row = Parse((50000, "10%, First: 7, 3")).Tables[0].Single();
        Assert.AreEqual(new EncounterGroup(7, 3, 3), row.Groups.Single());
    }

    [TestMethod]
    public void Any_error_of_the_loader_disables_the_encounters()
    {
        string Reason(params (int, string)[] rows) { var t = Parse(rows); Assert.AreEqual(EncounterTablesStatus.Disabled, t.Status); Assert.AreEqual(0, t.Tables.Count); return t.Detail; }
        StringAssert.Contains(Reason((50000, "50%, MaxLevel: 4")), "no monsters");
        StringAssert.Contains(Reason((50000, "50%, First: 7, 1, MinLevel: 32001")), "MinLevel");
        StringAssert.Contains(Reason((50000, "50%, First: 7, 1, GlobalFlag: 3201")), "GlobalFlag");
        StringAssert.Contains(Reason((50001, "50%, First: 7, 1")), "missing line 50000", "the table's rows are consecutive lines from its base");
        var noFrequency = EncounterTables.Parse(Minimal().Where(m => m.Number is < 10000 or >= 20000).ToList());
        Assert.AreEqual(EncounterTablesStatus.Disabled, noFrequency.Status);
        var badPower = EncounterTables.Parse(Minimal().Select(m => m.Number == 20001 ? (20001, "5000, 5000, 100, Mighty") : m).ToList());
        StringAssert.Contains(badPower.Detail, "20001");
    }

    [TestMethod]
    public void A_chart_is_sorted_by_radius_so_the_smallest_covering_circle_answers()
    {
        // The big circle is listed first; the original sorts each chart by its radius field (0x558D00) and the lookup (0x558F30) takes the first match.
        var messages = new List<(int, string)>
        {
            (10000, "100, 100, 100, 5"), (10001, "100, 100, 10, 90"), (10002, "100, 100, 50, 40"),
            (20000, "0, 0, 1, Easy"), (30000, "0, 0, 1, 2"), (49999, "0"),
        };
        var tables = EncounterTables.Parse(messages);
        CollectionAssert.AreEqual(new long[] { 10, 50, 100 }, tables.Frequency.Select(r => r.Radius).ToArray());
        Assert.AreEqual(90, EncounterRules.RegionValue(tables.Frequency, new(105, 100)));
        Assert.AreEqual(40, EncounterRules.RegionValue(tables.Frequency, new(140, 100)));
        Assert.AreEqual(5, EncounterRules.RegionValue(tables.Frequency, new(190, 100)));
        Assert.IsNull(EncounterRules.RegionValue(tables.Frequency, new(300, 100)));
    }

    [TestMethod]
    public void A_chart_row_is_read_by_its_line_number_and_a_missing_number_is_a_zero_row()
    {
        // 0x558B80 counts the messages of the span (two) and reads the consecutive numbers from the base: 10001 is missing (a row of zeros), 10002 is never read.
        var tables = EncounterTables.Parse([(10000, "10, 10, 5, 7"), (10002, "1, 1, 1, 99"), (20000, "0, 0, 1, Easy"), (30000, "0, 0, 1, 2"), (49999, "0")]);
        Assert.AreEqual(EncounterTablesStatus.Loaded, tables.Status);
        CollectionAssert.AreEqual(new[] { new EncounterRegion(0, 0, 0, 0), new EncounterRegion(10, 10, 5, 7) }, tables.Frequency.ToArray());
        // A power chart with a missing line has no power name to read: the loader fails and the encounters are off.
        var failed = EncounterTables.Parse([(10000, "0, 0, 1, 5"), (20000, "0, 0, 1, Easy"), (20002, "0, 0, 1, Easy"), (30000, "0, 0, 1, 2"), (49999, "0")]);
        Assert.AreEqual(EncounterTablesStatus.Disabled, failed.Status);
    }
}
