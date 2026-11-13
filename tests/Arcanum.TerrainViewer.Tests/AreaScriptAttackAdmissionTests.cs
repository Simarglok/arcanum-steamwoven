using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1620 (ledger TI6): the use, placed, heartbeat, dying and talk sets hold the script action attack (36) and stay inside S43; the opt-in check counts the installed scripts that carry it and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaScriptAttackAdmissionTests
{
    [TestMethod]
    public void The_use_placed_heartbeat_dying_and_talk_sets_hold_attack_and_stay_inside_S43()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission,
            AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
        {
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.Attack), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S58), set.Id);
        }
        // A talk with no use host of its map has no AI path to raise the event through: the plain dialog sets do not hold it.
        foreach (var set in new[] { AreaScriptHost.Admission, AreaScriptHost.SkillAdmission })
            Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.Attack), set.Id);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_an_attack_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in attack check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("S33", ScrAdmissionSet.S33), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission), ("talk", AreaScriptHost.TalkAdmission), ("skill talk", AreaScriptHost.SkillTalkAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var carriers = new List<int>();
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Any(c => c.Type == ScrCatalogue.Action.Attack)) continue;
            carriers.Add(number);
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with an attack {carriers.Count}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        // 79 installed scripts carry the action (111 clauses, the census of ScrAttackActionCensusRealDataTests); its sets admit whole: S33 31 (the thrown item's 29017 among them), the use click 2, tile and sector scripts 34, heartbeats 10,
        // dying scripts 47 (the union of the placed and heartbeat sets), the talk sets 10 (the others hold a command of another slice).
        Assert.AreEqual((79, 31, 2, 34, 10, 47, 10, 10), (carriers.Count, counts["S33"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"], counts["talk"], counts["skill talk"]));
    }
}
