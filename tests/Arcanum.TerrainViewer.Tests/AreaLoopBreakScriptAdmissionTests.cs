using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1166 (E5-e, ledger PG15): the use, placed, heartbeat and dying sets hold loop break (20); the opt-in check counts the installed scripts that use it and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaLoopBreakScriptAdmissionTests
{
    [TestMethod]
    public void The_use_placed_heartbeat_and_dying_sets_hold_loop_break_and_stay_inside_S43()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
        {
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.LoopBreak), set.Id);
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.LoopFor) && set.Actions.Contains(ScrCatalogue.Action.LoopEnd), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S61), set.Id);
        }
        // The dialog host runs no loops: its sets stay without the break.
        foreach (var set in new[] { AreaScriptHost.Admission, AreaScriptHost.SkillAdmission, AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
            Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.LoopBreak), set.Id);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_a_loop_break_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in loop break check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("S38", ScrAdmissionSet.S38), ("S40", ScrAdmissionSet.S40), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var withBreak = 0;
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Any(c => c.Type == ScrCatalogue.Action.LoopBreak)) continue;
            withBreak++;
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with a loop break {withBreak}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, counts["S38"], "S38 refuses every one of them by construction");
        // 22 installed scripts carry a loop break; S40 admits 12 whole (the rest hold a command of another slice), the use set none (its scripts hold other refused commands), the placed and dying sets 7 each and the heartbeat set 1.
        // ARC-SCRIPT-1815: the heartbeat set gained the scenery and object-variable focus kinds, assign object and float line (heartbeats 8, from 5).
        Assert.AreEqual((22, 12, 1, 9, 8, 19), (withBreak, counts["S40"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"]));
    }
}
