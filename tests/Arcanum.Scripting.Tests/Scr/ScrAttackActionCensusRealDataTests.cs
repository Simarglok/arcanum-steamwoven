using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// Opt-in census of ARC-SCRIPT-1620 (<c>DAT_ROOT</c>, the game folder): which installed scripts use the action attack (36, 0x447D2F, ledger TI6), and which object operand 1 names as the attacker (the first object of its focus,
/// <see cref="ScrFocusKind"/>). Only script numbers, line numbers and focus kinds are printed, never original text.
/// </summary>
[TestClass]
public sealed class ScrAttackActionCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_that_use_action_36_and_their_attacker_focus()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in attack census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var rows = new List<string>();
        var scripts = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".scr", StringComparison.Ordinal)))
        {
            scripts++;
            var read = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!);
            if (read.Program is not { } program) continue;
            foreach (var line in program.Lines)
                foreach (var (side, clause) in new[] { ("true", line.OnTrue), ("false", line.OnFalse) })
                    if (clause.Type == ScrCatalogue.Action.Attack)
                        rows.Add($"{Path.GetFileNameWithoutExtension(key)}|line {line.Number}|{side}|victims {(ScrFocusKind)clause.Kind(0)}|attacker {(ScrFocusKind)clause.Kind(1)}");
        }
        foreach (var row in rows) Console.WriteLine("ATTACK-CENSUS|" + row);
        Console.WriteLine($"ATTACK-CENSUS|scripts {scripts}|attack clauses {rows.Count}");
        // 111 clauses (79 scripts): the attacker is the triggerer in 45, the looped object in 36, the player in 25, the attachee in 3 (1298, 1632 and the thrown creature item 29017) and the extra object in 2 (the final battle's 2719 and the dark plate's 30152).
        var attackers = rows.GroupBy(row => row.Split('|')[^1]).ToDictionary(g => g.Key, g => g.Count());
        Assert.AreEqual(111, rows.Count);
        CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["attacker Triggerer"] = 45, ["attacker CurrentLoopedObject"] = 36, ["attacker Player"] = 25, ["attacker Attachee"] = 3, ["attacker ExtraObject"] = 2 }, attackers);
    }
}
