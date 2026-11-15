using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1842 (#1322, ledger TI6): the heartbeat and dying sets hold make follow (21) and stay inside S59; the opt-in check counts the installed scripts that carry it and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaScriptMakeFollowAdmissionTests
{
    [TestMethod]
    public void The_heartbeat_and_dying_sets_hold_make_follow_and_stay_inside_S59()
    {
        foreach (var set in new[] { AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
        {
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.MakeFollow), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S64), set.Id);
        }
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_a_make_follow_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in make follow check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("thrown", AreaThrownItemScripts.Admission), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission), ("talk", AreaScriptHost.TalkAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => new List<int>());
        var carriers = new List<int>();
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Any(c => c.Type == ScrCatalogue.Action.MakeFollow)) continue;
            carriers.Add(number);
            foreach (var (name, set) in sets)
            {
                if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name].Add(number);
            }
        }
        Console.WriteLine($"installed scripts with a make follow {carriers.Count} [{string.Join(",", carriers)}]; admitted whole: " + string.Join("; ", counts.Select(c => $"{c.Key} [{string.Join(",", c.Value)}]")));
        // 20 installed scripts carry the action. The thrown item set admits the four creature item scripts (1022, 1343, 30008, 30300, ledger TI6); the heartbeat set the five NPC-leader scripts (2201 at the first heartbeat, 30117, 30119,
        // 30124 and 30126 at the heartbeat, which make the attachee follow a looped NPC picked by name: #1322's census) and, with ARC-SCRIPT-1815's float line and object variable, 1058 and 1673; the dying set those and seven more (the
        // dying set is the union of the placed and heartbeat sets; where the seven are attached is not read here); the use, placed and talk sets none (they hold no make follow).
        CollectionAssert.AreEqual(new[] { 1022, 1046, 1058, 1116, 1196, 1280, 1343, 1605, 1647, 1673, 1680, 1825, 2201, 2632, 30008, 30117, 30119, 30124, 30126, 30300 }, carriers);
        CollectionAssert.AreEqual(new[] { 1022, 1343, 30008, 30300 }, counts["thrown"]);
        CollectionAssert.AreEqual(new[] { 1058, 1673, 2201, 30117, 30119, 30124, 30126 }, counts["heartbeat"]);
        CollectionAssert.AreEqual(new[] { 1046, 1058, 1196, 1280, 1673, 1825, 2201, 2632, 30117, 30119, 30124, 30126 }, counts["dying"]);
        Assert.AreEqual(0, counts["use"].Count + counts["placed"].Count + counts["talk"].Count);
        // The dying scripts 30118 and 30127 (a free Create Undead, spell 58, by the looped leader at the dying attachee) carry no follow and run whole under the dying set.
        foreach (var number in new[] { 30118, 30127 })
        {
            Assert.IsTrue(content.Script(number) is { Status: ScriptContentStatus.Read, Program: { } program } && ScrAdmission.Admit(program, AreaUseScriptHost.DyingAdmission).IsAdmitted, number.ToString());
        }
    }
}
