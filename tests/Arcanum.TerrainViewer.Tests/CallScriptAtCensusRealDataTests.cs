using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2824 opt-in census (DAT_ROOT = the game folder, ledger TQ17): the installed script lines that run call script at (55) and call script in (54), the scripts they
/// are in and the script each names. The census of ARC-RES-045 found one call-at line (and 304 call-in lines); the test pins both counts.
/// <c>ARC_CALL_AT_REPORT</c>, when set, names a text file the rows are written to.
/// </summary>
[TestClass]
public sealed class CallScriptAtCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_call_script_at_lines_and_the_scripts_that_hold_them()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var report = new List<string>();
        var at = new List<(int Script, int Line)>();
        int callIn = 0, scripts = 0;
        foreach (var name in content.Names.Names)
        {
            if (content.Script(name.Number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            scripts++;
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                {
                    if (clause.Type == ScrCatalogue.Action.CallScriptIn) callIn++;
                    if (clause.Type == ScrCatalogue.Action.CallScriptAt)
                    {
                        at.Add((name.Number, line.Number));
                        report.Add($"call at: script {name.Number} line {line.Number}: script {clause.Value(0)} line {clause.Value(1)} operand {clause.Value(4)} (kinds {clause.Kind(0)} {clause.Kind(1)} {clause.Kind(4)})");
                    }
                }
        }
        report.Add($"scripts read {scripts}, call in lines {callIn}, call at lines {at.Count}");
        if (Environment.GetEnvironmentVariable("ARC_CALL_AT_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
        foreach (var row in report) Console.WriteLine(row);
        // The installed data holds one call-at line, script 1405 line 4 (it calls its own line 6 five seconds after the start hour).
        CollectionAssert.AreEqual(new[] { (1405, 4) }, at.ToArray(), string.Join("\n", report));
        Assert.AreEqual(304, callIn);
    }
}
