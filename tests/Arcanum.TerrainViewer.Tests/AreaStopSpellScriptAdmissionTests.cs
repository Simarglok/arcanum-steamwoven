using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1294 (ledger RM6): the placed, heartbeat and dying sets hold stop spell on object (130) and stay inside S42; the opt-in check counts the installed scripts that carry it and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaStopSpellScriptAdmissionTests
{
    [TestMethod]
    public void The_placed_heartbeat_and_dying_sets_hold_the_spell_stop_and_stay_inside_S43()
    {
        foreach (var set in new[] { AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
        {
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.StopSpellOnObject), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S56), set.Id);
        }
        // The use click's set holds the cast actions no more than this one, and the dialog host has no spell run to stop.
        Assert.IsFalse(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.StopSpellOnObject));
        foreach (var set in new[] { AreaScriptHost.Admission, AreaScriptHost.SkillAdmission, AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
            Assert.IsFalse(set.Actions.Contains(ScrCatalogue.Action.StopSpellOnObject), set.Id);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_a_spell_stop_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in spell stop check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("S40", ScrAdmissionSet.S40), ("S42", ScrAdmissionSet.S42), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var carriers = new List<int>();
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Any(c => c.Type == ScrCatalogue.Action.StopSpellOnObject)) continue;
            carriers.Add(number);
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with a spell stop {string.Join(",", carriers)}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, counts["S40"], "S40 refuses every one of them by construction");
        // Four installed scripts carry the action (1054, 2789, 2792, 3022; the census reaches one of them by a live run); S42 admits 3 whole (the fourth holds a command of another slice), the placed set 1 and the dying set 1 (2 since ARC-SCRIPT-1481 widened the shared set), the heartbeat set none and the use set none.
        CollectionAssert.AreEqual(new[] { 1054, 2789, 2792, 3022 }, carriers);
        Assert.AreEqual((0, 3, 0, 1, 0, 2), (counts["S40"], counts["S42"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"]));
    }
}
