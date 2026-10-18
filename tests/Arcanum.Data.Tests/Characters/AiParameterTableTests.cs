using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Characters;

/// <summary>ARC-DATA-033: the AI parameter table on invented tables (ledger AP1, AP2).</summary>
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
        CollectionAssert.AreEqual(Enumerable.Range(100, 17).ToArray(), table.Row(0)!.Values.ToArray());
        Assert.AreEqual(200, table.Row(1)!.Values[0]);
        Assert.AreEqual(316, table.Row(2)!.Values[16]);
        Assert.IsTrue(table.Row(3)!.Values.All(v => v == 0), "a row no entry filled is zero");
        Assert.IsNull(table.Row(150)); Assert.IsNull(table.Row(-1));
        Assert.AreEqual(2, table.Row(2)!.Index);
    }

    [TestMethod]
    public void Tokens_split_on_space_tab_and_line_feed_are_read_like_atoi_up_to_17()
    {
        var table = AiParameterTable.Create(Mes("{0}{ 5\t-12x\n+7  0 0 -1000 2001 2001 30 1 2 3 4 5 6 7 8 99 }{1}{1 2}"), null);
        var row = table.Row(0)!;
        CollectionAssert.AreEqual(new[] { 5, -12, 7, 0, 0, -1000, 2001, 2001, 30, 1, 2, 3, 4, 5, 6, 7, 8 }, row.Values.ToArray());
        Assert.AreEqual((-1000, 2001, 2001, 30), (row.DislikeReaction, row.MaxAlignmentAbove, row.MaxAlignmentBelow, row.LevelGate));
        CollectionAssert.AreEqual(new[] { 1, 2 }.Concat(new int[15]).ToArray(), table.Row(1)!.Values.ToArray(), "missing tokens stay 0");
    }

    [TestMethod]
    public void Each_part_needs_its_first_entry_and_stops_at_its_last_row()
    {
        Assert.AreEqual(0, AiParameterTable.Create(Mes($"{{1}}{{{Row(1)}}}"), null).BaseFilled, "no entry 0, no row");
        var many = string.Concat(Enumerable.Range(0, 120).Select(i => $"{{{i}}}{{{i}}}"));
        var module = Mes($"{{99}}{{{Row(9)}}}{{100}}{{{Row(4)}}}" + string.Concat(Enumerable.Range(101, 60).Select(i => $"{{{i}}}{{{i}}}")));
        var table = AiParameterTable.Create(Mes(many), module);
        Assert.AreEqual((100, 50), (table.BaseFilled, table.ModuleFilled));
        Assert.AreEqual(99, table.Row(99)!.Values[0]);
        Assert.AreEqual(400, table.Row(100)!.Values[0], "the module's rows start at its entry 100");
        Assert.AreEqual(149, table.Row(149)!.Values[0]);
        Assert.AreEqual(0, AiParameterTable.Create(Mes(many), Mes($"{{101}}{{{Row(1)}}}")).ModuleFilled);
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
        Assert.AreEqual(100, load.Table!.Row(0)!.Values[0]);
        Assert.AreEqual(200, load.Table.Row(100)!.Values[0]);
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
