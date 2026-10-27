using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Characters;

/// <summary>ARC-DATA-033 and ARC-WORLD-077: the AI parameter table and its typed rows on invented tables (ledger AP1, AP2, AP6).</summary>
[TestClass]
public sealed class AiParameterTableTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;
    private static string Row(int seed) => string.Join(' ', Enumerable.Range(0, 17).Select(i => seed * 100 + i));

    [TestMethod]
    public void Entries_from_number_0_fill_consecutive_rows_whatever_their_numbers()
    {
        // Entries 0, 7 and 30 fill rows 0, 1 and 2.
        var table = AiParameterTable.Create(Mes($"{{0}}{{{Row(1)}}}{{7}}{{{Row(2)}}}{{30}}{{{Row(3)}}}"), null);
        Assert.AreEqual((3, 0), (table.BaseFilled, table.ModuleFilled));
        CollectionAssert.AreEqual(Enumerable.Range(100, 17).ToArray(), table.Lookup(0).Row!.Values.ToArray());
        Assert.AreEqual(200, table.Lookup(1).Row!.Values[0]);
        Assert.AreEqual(316, table.Lookup(2).Row!.Values[16]);
        Assert.IsTrue(table.Lookup(3).Row!.Values.All(v => v == 0), "a row no entry filled is zero");
        Assert.AreEqual(new AiParameterLookup(AiParameterLookupStatus.OutsideTable, 150, null), table.Lookup(150));
        Assert.AreEqual(new AiParameterLookup(AiParameterLookupStatus.OutsideTable, -1, null), table.Lookup(-1));
        Assert.AreEqual(2, table.Lookup(2).Row!.Index);
    }

    [TestMethod]
    public void Tokens_split_on_space_tab_and_line_feed_are_read_like_atoi_up_to_17()
    {
        var table = AiParameterTable.Create(Mes("{0}{ 5\t-12x\n+7  0 0 -1000 2001 2001 30 1 2 3 4 5 6 7 8 99 }{1}{1 2}"), null);
        var row = table.Lookup(0).Row!;
        CollectionAssert.AreEqual(new[] { 5, -12, 7, 0, 0, -1000, 2001, 2001, 30, 1, 2, 3, 4, 5, 6, 7, 8 }, row.Values.ToArray());
        Assert.AreEqual((-1000, 2001, 2001, 30), (row.FollowReaction, row.FollowAlignmentAbove, row.FollowAlignmentBelow, row.JoinLevelGap));
        CollectionAssert.AreEqual(new[] { 1, 2 }.Concat(new int[15]).ToArray(), table.Lookup(1).Row!.Values.ToArray(), "missing tokens stay 0");
    }

    [TestMethod]
    public void Each_part_needs_its_first_entry_and_stops_at_its_last_row()
    {
        Assert.AreEqual(0, AiParameterTable.Create(Mes($"{{1}}{{{Row(1)}}}"), null).BaseFilled, "no entry 0, no row");
        var many = string.Concat(Enumerable.Range(0, 120).Select(i => $"{{{i}}}{{{i}}}"));
        var module = Mes($"{{99}}{{{Row(9)}}}{{100}}{{{Row(4)}}}" + string.Concat(Enumerable.Range(101, 60).Select(i => $"{{{i}}}{{{i}}}")));
        var table = AiParameterTable.Create(Mes(many), module);
        Assert.AreEqual((100, 50), (table.BaseFilled, table.ModuleFilled));
        Assert.AreEqual(99, table.Lookup(99).Row!.Values[0]);
        Assert.AreEqual(400, table.Lookup(100).Row!.Values[0], "the module's rows start at its entry 100");
        Assert.AreEqual(149, table.Lookup(149).Row!.Values[0]);
        Assert.AreEqual(0, AiParameterTable.Create(Mes(many), Mes($"{{101}}{{{Row(1)}}}")).ModuleFilled);
    }

    [TestMethod]
    public void Every_slot_reads_into_its_named_field()
    {
        var row = AiParameterTable.Create(Mes($"{{0}}{{{Row(1)}}}"), null).Lookup(0).Row!;
        var named = new[]
        {
            row.FleeHpPercent, row.FleeOutnumbered, row.FleeOutlevelled, row.StandWhileTargetWeakPercent, row.FleeDistance,
            row.FollowReaction, row.FollowAlignmentAbove, row.FollowAlignmentBelow, row.JoinLevelGap, row.FollowerHitReaction,
            row.HostileReaction, row.HostileAlignmentGap, row.GoodFollowerStop, row.DefensiveSpellRoll, row.CombatHealChance,
            row.CombatDistance, row.OpensPortals,
        };
        // Row(1) holds 100 + slot in each slot, so each name proves its slot.
        CollectionAssert.AreEqual(Enumerable.Range(100, AiParameterTable.Fields).ToArray(), named);
        CollectionAssert.AreEqual(named, row.Values.ToArray());
        Assert.AreEqual(row, new AiParameterRow(0, named), "a row is a value");
    }

    [TestMethod]
    public void A_short_row_leaves_its_trailing_slots_zero_in_a_fresh_table()
    {
        // The installed module row has 14 values: slots 14..16 stay as the zeroed table left them (ledger AP1, AP6).
        var many = string.Concat(Enumerable.Range(0, 100).Select(i => $"{{{i}}}{{{Row(1)}}}"));
        var table = AiParameterTable.Create(Mes(many), Mes($"{{100}}{{{string.Join(' ', Enumerable.Range(1, 14))}}}"));
        var row = table.Lookup(100).Row!;
        Assert.AreEqual((AiParameterLookupStatus.Found, 100), (table.Lookup(100).Status, row.Index));
        Assert.AreEqual((1, 14), (row.FleeHpPercent, row.DefensiveSpellRoll), "slots 0..13 hold the 14 values");
        Assert.AreEqual((0, 0, 0), (row.CombatHealChance, row.CombatDistance, row.OpensPortals));
        Assert.IsTrue(table.Lookup(101).Row!.Values.All(v => v == 0), "a module row no entry filled is zero");
        Assert.AreEqual(116, table.Lookup(99).Row!.OpensPortals, "the module never touches the base rows");
    }

    [TestMethod]
    public void An_npc_names_its_row_by_its_ai_data_field_and_outside_rows_are_refused()
    {
        var table = AiParameterTable.Create(Mes($"{{0}}{{{Row(1)}}}{{1}}{{{Row(2)}}}"), null);
        static CombatNpcFields Npc(int aiData) => new(0, aiData, 0, 0, 0, 0, 0, []);
        Assert.AreEqual(205, table.Lookup(Npc(1)).Row!.FollowReaction);
        Assert.AreEqual((AiParameterLookupStatus.Found, 149), (table.Lookup(Npc(149)).Status, table.Lookup(Npc(149)).Row!.Index));
        foreach (var outside in new[] { int.MinValue, -1, 150, int.MaxValue })
            Assert.AreEqual(new AiParameterLookup(AiParameterLookupStatus.OutsideTable, outside, null), table.Lookup(Npc(outside)));
        Assert.ThrowsExactly<ArgumentNullException>(() => table.Lookup(null!));
    }

    [TestMethod]
    public void A_row_takes_17_values_and_an_index_inside_the_table()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AiParameterRow(0, new int[16]));
        Assert.ThrowsExactly<ArgumentException>(() => new AiParameterRow(0, new int[18]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AiParameterRow(-1, new int[17]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AiParameterRow(150, new int[17]));
        Assert.AreEqual(149, new AiParameterRow(149, new int[17]).Index);
    }

    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-ai-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void Open_reads_the_base_rows_from_the_first_base_source_and_the_module_rows_from_the_stack_winner()
    {
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Dat(("rules/ai_params.mes", Bytes($"{{0}}{{{Row(1)}}}"))));
        // A module copy of the base table is never read for rows 0..99; the module's own table fills 100..149.
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(("rules/ai_params.mes", Bytes($"{{0}}{{{Row(7)}}}")),
            ("rules/gameai.mes", Bytes($"{{100}}{{{Row(2)}}}"))));
        using var stack = Stack();
        var load = AiParameterTable.Open(stack);
        Assert.AreEqual(AiParameterTableStatus.Read, load.Status, load.Detail);
        Assert.AreEqual(100, load.Table!.Lookup(0).Row!.Values[0]);
        Assert.AreEqual(200, load.Table.Lookup(100).Row!.Values[0]);
    }

    [TestMethod]
    public void Open_needs_the_base_table_and_refuses_a_table_the_grammar_does_not_admit()
    {
        using (var empty = Stack()) Assert.AreEqual(AiParameterTableStatus.BaseMissing, AiParameterTable.Open(empty).Status);
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Dat(("rules/ai_params.mes", Bytes($"{{0}}{{{Row(1)}}}"))));
        using (var noModule = Stack())
        {
            var load = AiParameterTable.Open(noModule);
            Assert.AreEqual((AiParameterTableStatus.Read, 0), (load.Status, load.Table!.ModuleFilled), "a missing module table is no failure");
        }
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(("rules/gameai.mes", Bytes("{100}{1}{100}{2}"))));
        using (var duplicate = Stack()) Assert.AreEqual(AiParameterTableStatus.ModuleRefused, AiParameterTable.Open(duplicate).Status);
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Dat(("rules/ai_params.mes", Bytes("{0}{1}{0}{2}"))));
        using (var refused = Stack()) Assert.AreEqual(AiParameterTableStatus.BaseRefused, AiParameterTable.Open(refused).Status);
    }
}
