using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS1–NS8): the use, placed, heartbeat and dying sets hold the nine NPC state actions the area hosts apply (change art number 59 since ARC-SCRIPT-1735); the opt-in check counts the installed scripts
/// that use one of the nine actions and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaNpcStateScriptAdmissionTests
{
    [TestMethod]
    public void The_use_placed_heartbeat_and_dying_sets_hold_the_nine_applied_actions_change_art_included()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
        {
            foreach (var action in ScrAdmissionSet.NpcStateHostActions) Assert.IsTrue(set.Actions.Contains(action), $"{set.Id}: action {action}");
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.ChangeArtNumber), set.Id);
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S63), set.Id);
        }
        // The dialog host does not apply the requests yet: its sets stay without them.
        foreach (var set in new[] { AreaScriptHost.Admission, AreaScriptHost.SkillAdmission, AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
            foreach (var action in ScrAdmissionSet.NpcStateActions) Assert.IsFalse(set.Actions.Contains(action), $"{set.Id}: action {action}");
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_the_npc_state_actions_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in NPC state check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("S35", ScrAdmissionSet.S35), ("S36", ScrAdmissionSet.S36), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var perAction = ScrAdmissionSet.NpcStateActions.ToDictionary(a => a, _ => 0);
        var withState = 0;
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            var used = program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Select(c => c.Type).Where(ScrAdmissionSet.NpcStateActions.Contains).Distinct().ToList();
            if (used.Count == 0) continue;
            withState++;
            foreach (var action in used) perAction[action]++;
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with an NPC state action {withState} (by action {string.Join(", ", perAction.Select(p => $"{p.Key}:{p.Value}"))}); admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, counts["S35"], "S35 refuses every one of them by construction");
        // Ledger NS8: 270 installed scripts use one of the nine actions (disband 34, set reaction 11, adjust reaction 13, day standpoint 73, night standpoint 62, the extended two 1 each, change art 51, change script 109);
        // S36 admits 208 whole, the hosts' sets 56 (use), 117 (placed), 89 (heartbeat) and 143 (dying); the rest hold change art number (no host applies it) or a command of another planned slice.
        // ARC-SCRIPT-1164 (set S37, the world-object commands): the placed set admits 120, the heartbeat set 91 and the dying set 147 of them (3, 2 and 4 more); the use set stays at 56.
        // ARC-SCRIPT-1279 (the four item commands got a host): the placed set admits 123 and the dying set 150 (3 more each); the use and heartbeat sets stay at 56 and 91.
        // ARC-SCRIPT-1359 (set S38, get location 95): the heartbeat set admits 94 and the dying set 153 (3 more each, among them the Crash Site agent's heartbeat 1717).
        // ARC-SCRIPT-1481 (set S43, the vitals commands and the use set's heal, destroy and magick-tech commands): the dying set admits one more.
        // ARC-SCRIPT-1735 (change art number got its host, ledger NS5, NS9): the use set admits 85 (29 more), the placed set 163 (40 more), the heartbeat set 97 (3 more) and the dying set 205 (51 more), and with the set the later condition sets (S53 to S55) the placed set 168, the heartbeat set 98 and the dying set 214; whether a run applies is the host's, which refuses a
        // target it cannot draw with another art before anything applies.
        // ARC-SCRIPT-1815: the heartbeat set gained the scenery and object-variable focus kinds, assign object and float line (heartbeats 160, from 98).
        Assert.AreEqual((270, 208, 87, 175, 172, 223), (withState, counts["S36"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"]));
    }
}
