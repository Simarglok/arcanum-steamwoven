using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2507 opt-in census (DAT_ROOT = the game folder, ledger TI5e): which installed scripts can run a dialog action (4) after a transform (40) in one run. The reach is the control flow of the lines with
/// both sides of every condition taken (a goto jumps, a return or a dialog ends, a loop end may go back to any line after a loop for; a call script (7) runs its callee from any line), so a script found here is
/// a candidate, a script not found cannot do it. <c>ARC_TRANSFORM_DIALOG_REPORT</c>, when set, names a text file the rows are written to.
/// </summary>
[TestClass]
public sealed class TransformThenDialogCensusRealDataTests
{
    private const int Transform = ScrCatalogue.Action.TransformObject;

    [TestMethod, TestCategory("RealData")]
    public void Installed_scripts_that_can_open_a_dialog_after_a_transform_in_one_run()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var programs = new SortedDictionary<int, Arcanum.Data.Scripts.ScrProgram>();
        foreach (var name in content.Names.Names)
            if (content.Script(name.Number) is { Status: ScriptContentStatus.Read, Program: { } program }) programs[name.Number] = program;

        var report = new List<string>();
        var transformScripts = programs.Where(p => Clauses(p.Value).Any(c => c.Clause.Type == Transform)).Select(p => p.Key).ToArray();
        report.Add($"scripts read {programs.Count}, with a transform {transformScripts.Length}: {string.Join(",", transformScripts)}");

        // Scripts that hold a dialog action reachable (from any line) in a callee called by script `from` after reaching `reached`.
        var candidates = new List<string>();
        foreach (var number in transformScripts)
        {
            var program = programs[number];
            foreach (var (line, side, clause) in Clauses(program).Where(c => c.Clause.Type == Transform))
            {
                var reached = Reach(program, Successors(program, line, clause));
                foreach (var after in reached)
                    foreach (var other in Taken(program.Lines[after]))
                    {
                        if (other.Type == ScrCatalogue.Action.Dialog) candidates.Add($"dialog after transform: script {number} transform line {line}{side} -> dialog line {after}");
                        if (other.Type == ScrCatalogue.Action.CallScript && other.Value(0) is var callee && programs.ContainsKey(callee) && CalleeDialog(callee, programs, new HashSet<int>()))
                            candidates.Add($"callee with a dialog after transform: script {number} transform line {line}{side} -> call script {callee} at line {after}");
                        if (other.Type == ScrCatalogue.Action.CallScriptEx) candidates.Add($"call script ex after transform (callee by attachment): script {number} transform line {line}{side} -> line {after}");
                    }
            }
        }
        // A transform in a callee (at any depth of call script), then a dialog in the caller after the call returns.
        var transforming = transformScripts.ToHashSet();   // grows below
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (number, program) in programs)
                if (!transforming.Contains(number) && Clauses(program).Any(c => c.Clause.Type == ScrCatalogue.Action.CallScript && transforming.Contains(c.Clause.Value(0)))) { transforming.Add(number); grew = true; }
        }
        report.Add($"scripts that can transform, through call script: {transforming.Count}");
        foreach (var (caller, program) in programs)
            foreach (var (line, _, clause) in Clauses(program).Where(c => c.Clause.Type == ScrCatalogue.Action.CallScript))
                if (clause.Value(0) is var callee && transforming.Contains(callee) && callee != caller)
                    foreach (var after in Reach(program, Successors(program, line, clause)))
                        if (Taken(program.Lines[after]).Any(c => c.Type == ScrCatalogue.Action.Dialog))
                            candidates.Add($"transform in callee {callee} then dialog in caller: script {caller} call line {line} -> dialog line {after}");
        // A call script ex (81) runs the callee by the attachment of an object, so the callee is not known from the lines: the scripts that hold one and can reach a dialog action after it.
        var exThenDialog = new List<int>();
        foreach (var (number, program) in programs)
            foreach (var (line, _, clause) in Clauses(program).Where(c => c.Clause.Type == ScrCatalogue.Action.CallScriptEx))
                if (Reach(program, Successors(program, line, clause)).Any(after => Taken(program.Lines[after]).Any(c => c.Type == ScrCatalogue.Action.Dialog))) exThenDialog.Add(number);
        report.Add($"scripts with a call script ex before a dialog action: {string.Join(",", exThenDialog.Distinct())}");
        report.AddRange(candidates.Distinct());
        report.Add($"candidates {candidates.Distinct().Count()}");
        if (Environment.GetEnvironmentVariable("ARC_TRANSFORM_DIALOG_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
        foreach (var row in report) Console.WriteLine(row);
        // The installed data holds 21 scripts with a transform (action 40; none reaches another by call script) and none of them can open a dialog action after it in one run, in its own lines or through a call script
        // either way: the dialog host's refusal of that shape (AreaScriptHost.Apply) is never met by the installed game. A change of these numbers means a script appeared that does: model the conversation with the made critter (issue #2507).
        CollectionAssert.AreEqual(new[] { 1022, 1023, 1105, 1198, 1343, 1344, 1528, 2339, 2794, 2795, 2796, 2867, 2908, 2909, 3087, 29017, 29018, 30008, 30009, 30300, 30301 }, transformScripts, string.Join("\n", report));
        Assert.AreEqual(21, transforming.Count, string.Join("\n", report));
        Assert.AreEqual(0, candidates.Distinct().Count(), string.Join("\n", report));
    }

    private static IEnumerable<(int Line, string Side, ScrClause Clause)> Clauses(Arcanum.Data.Scripts.ScrProgram program)
    {
        foreach (var line in program.Lines)
        {
            yield return (line.Number, "T", line.OnTrue);
            if (line.Condition.Type != ScrCatalogue.Condition.True) yield return (line.Number, "F", line.OnFalse);   // an always-true line never runs its false side
        }
    }

    private static IEnumerable<ScrClause> Taken(ScrLine line) => line.Condition.Type == ScrCatalogue.Condition.True ? [line.OnTrue] : [line.OnTrue, line.OnFalse];

    // The lines the run can go on with after the clause at `line`.
    private static IEnumerable<int> Successors(Arcanum.Data.Scripts.ScrProgram program, int line, ScrClause clause)
    {
        switch (clause.Type)
        {
            case ScrCatalogue.Action.ReturnSkipDefault or ScrCatalogue.Action.ReturnRunDefault or ScrCatalogue.Action.Dialog: yield break;
            case ScrCatalogue.Action.Goto: if (clause.Value(0) >= 0 && clause.Value(0) < program.Count) yield return clause.Value(0); yield break;
        }
        if (line + 1 < program.Count) yield return line + 1;
        if (clause.Type == ScrCatalogue.Action.LoopEnd)
            foreach (var other in program.Lines)
                if (Taken(other).Any(c => c.Type == ScrCatalogue.Action.LoopFor)) yield return Math.Min(other.Number + 1, program.Count - 1);
    }

    private static HashSet<int> Reach(Arcanum.Data.Scripts.ScrProgram program, IEnumerable<int> start)
    {
        var seen = new HashSet<int>();
        var work = new Stack<int>(start);
        while (work.Count > 0)
        {
            var line = work.Pop();
            if (!seen.Add(line)) continue;
            foreach (var clause in Taken(program.Lines[line]))
                foreach (var next in Successors(program, line, clause)) work.Push(next);
        }
        return seen;
    }

    // Whether the callee (run from any line) has a dialog action reachable, through the scripts it calls as well.
    private static bool CalleeDialog(int number, SortedDictionary<int, Arcanum.Data.Scripts.ScrProgram> programs, HashSet<int> visiting)
    {
        if (!visiting.Add(number)) return false;
        var program = programs[number];
        foreach (var (_, _, clause) in Clauses(program))
        {
            if (clause.Type == ScrCatalogue.Action.Dialog) return true;
            if (clause.Type == ScrCatalogue.Action.CallScript && programs.ContainsKey(clause.Value(0)) && CalleeDialog(clause.Value(0), programs, visiting)) return true;
        }
        return false;
    }
}
