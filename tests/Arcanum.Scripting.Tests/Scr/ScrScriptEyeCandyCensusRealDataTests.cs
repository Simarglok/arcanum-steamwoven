using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// Opt-in census of ARC-SCRIPT-1592 (<c>DAT_ROOT</c>, the game folder): which installed scripts use the action play script eye candy (120, 0x44C7A0, ledger WE4) with which entry of the script list (<c>rules\ScriptEyeCandy.mes</c>) and on which object. Only
/// script numbers, line numbers, entry numbers and focus kinds are printed, never original text.
/// </summary>
[TestClass]
public sealed class ScrScriptEyeCandyCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_that_play_script_eye_candy_name_entries_of_the_installed_list()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in script eye-candy census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var table = SpellEyeCandyTable.OpenScript(stack).Table!;
        var rows = new List<(string Script, int Line, int? Number, ScrFocusKind Object)>();
        var withTeleport = new HashSet<string>();
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".scr", StringComparison.Ordinal)))
        {
            var read = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!);
            if (read.Program is not { } program) continue;
            var name = Path.GetFileNameWithoutExtension(key);
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                {
                    if (clause.Type == ScrCatalogue.Action.PlayScriptEyeCandy)
                        rows.Add((name, line.Number, clause.Kind(0) == 3 ? clause.Value(0) : null, (ScrFocusKind)clause.Kind(1)));
                    if (clause.Type is ScrCatalogue.Action.Teleport or ScrCatalogue.Action.FadeAndTeleport) withTeleport.Add(name);
                }
        }
        foreach (var row in rows) Console.WriteLine($"EYE-CANDY-CENSUS|{row.Script}|line {row.Line}|number {row.Number?.ToString() ?? "variable"}|object {row.Object}|teleport {withTeleport.Contains(row.Script)}");
        Console.WriteLine($"EYE-CANDY-CENSUS|clauses {rows.Count}|scripts {rows.Select(r => r.Script).Distinct().Count()}|numbers {string.Join(',', rows.Where(r => r.Number is not null).Select(r => r.Number!.Value).Distinct().Order())}");
        // 149 clauses in 115 scripts: every literal number but one names an installed entry with art (1 to 74), the exception (script 2862's 438) is past the 75 entries and plays nothing; one clause reads its number from a variable (30282).
        Assert.AreEqual((149, 115), (rows.Count, rows.Select(r => r.Script).Distinct().Count()));
        var numbers = rows.Where(r => r.Number is not null).Select(r => r.Number!.Value).Distinct().Order().ToArray();
        Assert.AreEqual(75, table.Entries.Count);
        CollectionAssert.AreEqual(new[] { 438 }, numbers.Where(n => n >= table.Entries.Count || table.At(n).Art is null).ToArray());
        Assert.AreEqual(1, rows.Count(r => r.Number is null));
    }
    /// <summary>ARC-SCRIPT-1828: the census of stop script eye candy (124, 0x44C800, ledger WE12): the installed scripts that use it, with the entry number and the object kind of each clause. Only script numbers, line numbers, entry numbers and focus kinds are printed.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_that_stop_script_eye_candy_name_entries_of_the_installed_list()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in script eye-candy census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var table = SpellEyeCandyTable.OpenScript(stack).Table!;
        var rows = new List<(string Script, int Line, int? Number, ScrFocusKind Object)>();
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".scr", StringComparison.Ordinal)))
        {
            var read = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!);
            if (read.Program is not { } program) continue;
            var name = Path.GetFileNameWithoutExtension(key);
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                    if (clause.Type == ScrCatalogue.Action.StopScriptEyeCandy)
                        rows.Add((name, line.Number, clause.Kind(0) == 3 ? clause.Value(0) : null, (ScrFocusKind)clause.Kind(1)));
        }
        foreach (var row in rows) Console.WriteLine($"STOP-EYE-CANDY-CENSUS|{row.Script}|line {row.Line}|number {row.Number?.ToString() ?? "variable"}|object {row.Object}");
        Console.WriteLine($"STOP-EYE-CANDY-CENSUS|clauses {rows.Count}|scripts {rows.Select(r => r.Script).Distinct().Count()}|numbers {string.Join(',', rows.Where(r => r.Number is not null).Select(r => r.Number!.Value).Distinct().Order())}");
        Assert.IsTrue(rows.Count > 0);
        Assert.AreEqual(75, table.Entries.Count);
    }
}
