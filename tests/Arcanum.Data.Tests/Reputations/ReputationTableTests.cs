using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Reputations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Reputations;

/// <summary>ARC-WORLD-044: the reputation table load and parse as read in the executable (ledger RP1–RP3) over invented
/// tables, and an opt-in census of the installed module.</summary>
[TestClass]
public sealed class ReputationTableTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-reputation-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void An_entry_holds_three_factions_and_up_to_five_effects_each_with_its_written_scope()
    {
        var table = Table("{1000}{1 2 3, 5 0 0, -10 4 0,+3 0 7}\n{1001}{0 0 0}\n{1002}{9 0 0,1,2,3,4,5,6,7}\n{999}{1 1 1, 9 9 9}\n{2000}{1 1 1}");
        Assert.IsNull(table.Aborted);
        CollectionAssert.AreEqual(new[] { 1000, 1001, 1002 }, table.Records.Select(record => record.Number).ToArray(), "only 1000..1999 are read");
        var first = table.Find(1000)!;
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, first.Factions.ToArray());
        CollectionAssert.AreEqual(new ReputationEffect[] { new(5, 0, 0), new(-10, 4, 0), new(3, 0, 7) }, first.Effects.ToArray(),
            "a signed effect keeps its origin and faction (D1, ledger Reputation P1)");
        Assert.AreEqual(0, table.Find(1001)!.Effects.Count);
        CollectionAssert.AreEqual(new ReputationEffect[] { new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(4, 0, 0), new(5, 0, 0) },
            table.Find(1002)!.Effects.ToArray(), "five effects at most; a short effect keeps 0 for the missing numbers");
    }

    [TestMethod]
    public void The_number_scan_is_the_originals()
    {
        // Empty pieces are skipped (strtok); a trailing space reads a last 0; a tab stalls the scan so a number is read again;
        // a separator that is no number reads 0 and stalls; a piece of spaces is an effect of 0.
        var table = Table("{1000}{4 5 ,,7 8 9,  ,1\t2 3}\n{1001}{1;2 3}");
        CollectionAssert.AreEqual(new[] { 4, 5, 0 }, table.Find(1000)!.Factions.ToArray());
        CollectionAssert.AreEqual(new ReputationEffect[] { new(7, 8, 9), new(0, 0, 0), new(1, 2, 2) }, table.Find(1000)!.Effects.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 0 }, table.Find(1001)!.Factions.ToArray());
    }

    [TestMethod]
    public void Missing_faction_numbers_stop_the_table_and_skip_the_names_and_greetings()
    {
        var names = Mes("{1000}{Name}");
        var greetings = new MesFile?[] { Mes("{1}{a}"), null, null, null };
        var stopped = ReputationTable.Create(Mes("{1000}{1 2 3}\n{1001}{4 5}\n{1002}{6 7 8}"), names, greetings).Table!;
        Assert.AreEqual(1001, stopped.Aborted);
        CollectionAssert.AreEqual(new[] { 1000, 1001 }, stopped.Records.Select(record => record.Number).ToArray(), "later entries are not read");
        var partial = stopped.Find(1001)!;
        Assert.IsFalse(partial.Complete);
        CollectionAssert.AreEqual(new[] { 4, 5, 0 }, partial.Factions.ToArray(), "the numbers read before the stop stay");
        Assert.IsNull(stopped.Names);
        Assert.IsTrue(stopped.Greetings.All(file => file is null));
        Assert.AreEqual(1000, ReputationTable.Create(Mes("{1000}{}")).Table!.Aborted, "an empty entry has no faction numbers");

        var noRules = ReputationTable.Create(null, names, greetings).Table!;
        Assert.IsFalse(noRules.RulesLoaded);
        Assert.IsNull(noRules.Names);
        var noNames = ReputationTable.Create(Mes("{1000}{1 2 3}"), null, greetings).Table!;
        Assert.IsTrue(noNames.Greetings.All(file => file is null), "no names file: no greetings");
        var full = ReputationTable.Create(Mes("{1000}{1 2 3}"), names, greetings).Table!;
        Assert.AreEqual("Name", Encoding.ASCII.GetString(full.Name(1000)!.Bytes.Span));
        Assert.IsNull(full.Name(999));
        Assert.IsNotNull(full.Greetings[0]);
        Assert.AreEqual((ReputationTableStatus.OutOfRange, 1000), (ReputationTable.Create(Mes("{1000}{1 2 3, 99999999999 0 0}")).Status,
            ReputationTable.Create(Mes("{1000}{1 2 3, 99999999999 0 0}")).Number), "a number beyond 32 bits refuses the table");
    }

    [TestMethod]
    public void Load_reads_the_stack_winners_and_refuses_an_unparsable_file()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(data, "rules/gamerep.mes", "{1000}{1 2 3}");
        Write(module, "rules/gamerep.mes", "{1000}{4 5 6, -2 0 0}");
        Write(module, "mes/gamereplog.mes", "{1000}{Module name}");
        Write(module, "mes/game_rp_npc_f2m.mes", "{1000}{Greeting}");
        var loaded = Load(module, data);
        Assert.AreEqual(ReputationTableStatus.Ready, loaded.Status);
        CollectionAssert.AreEqual(new[] { 4, 5, 6 }, loaded.Table!.Find(1000)!.Factions.ToArray(), "the module copy wins");
        Assert.IsNotNull(loaded.Table.Names);
        Assert.IsNull(loaded.Table.Greetings[0], "a missing greeting file is skipped");
        Assert.IsNotNull(loaded.Table.Greetings[3]);

        Write(module, "mes/game_rp_npc_m2f.mes", "{1000}{Broken");
        var broken = Load(module, data);
        Assert.AreEqual((ReputationTableStatus.ParseFailed, "mes/game_rp_npc_m2f.mes"), (broken.Status, broken.Path));
        Assert.IsNull(broken.Table);

        var empty = Load(Path.Combine(_root, "m2"), Path.Combine(_root, "d2"));
        Assert.AreEqual(ReputationTableStatus.Ready, empty.Status);
        Assert.IsFalse(empty.Table!.RulesLoaded);
        Assert.AreEqual(0, empty.Table.Records.Count);
    }

    /// <summary>Opt-in census of the installed module (DAT_ROOT): counts only (L1), no values.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_has_76_reputations_and_93_effects()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for this opt-in real-data check; it is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = ReputationTable.Load(stack);
        Assert.AreEqual(ReputationTableStatus.Ready, loaded.Status, $"{loaded.Path} {loaded.Read} {loaded.Parse?.Status}");
        var table = loaded.Table!;
        Assert.IsNull(table.Aborted);
        Assert.AreEqual(76, table.Records.Count);
        Assert.AreEqual(93, table.Records.Sum(record => record.Effects.Count));
        Assert.AreEqual(5, table.Records.Count(record => record.Factions.Any(faction => faction != 0)), "L1: 5 reputations list a membership faction");
        Assert.AreEqual(76, Enumerable.Range(1000, 76).Count(number => table.Name(number) is not null), "L2: every reputation has a name");
        Assert.AreEqual(4, table.Greetings.Count(file => file is not null));
    }

    private static ReputationTable Table(string rules)
    {
        var loaded = ReputationTable.Create(Mes(rules));
        Assert.AreEqual(ReputationTableStatus.Ready, loaded.Status);
        return loaded.Table!;
    }

    private ReputationTableLoad Load(string module, string data)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return ReputationTable.Load(stack);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}
