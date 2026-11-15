using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1165 (E5-c, ledger SQ1–SQ12): the viewer's script hosts hold the story and time commands of set S34 and answer them. The opt-in check counts the installed scripts that use one and how many each host
/// set admits whole.</summary>
[TestClass]
public sealed class AreaStoryScriptAdmissionTests
{
    [TestMethod]
    public void The_use_heartbeat_dying_and_talk_sets_hold_the_story_and_time_commands_and_the_dialog_set_without_a_query_host_does_too()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission,
                     AreaScriptHost.Admission, AreaScriptHost.SkillAdmission, AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
        {
            foreach (var condition in ScrAdmissionSet.StoryConditions) Assert.IsTrue(set.Conditions.Contains(condition), $"{set.Id}: condition {condition}");
            foreach (var action in ScrAdmissionSet.StoryActions) Assert.IsTrue(set.Actions.Contains(action), $"{set.Id}: action {action}");
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S63), set.Id); // ARC-SCRIPT-1241: S35 is S34 plus the fade, which the use and dialog sets hold; ARC-SCRIPT-1163: S36 adds the NPC state actions the use and heartbeat sets hold
        }
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_the_story_and_time_commands_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in story-command check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission), ("heartbeat", AreaUseScriptHost.HeartbeatAdmission),
            ("dying", AreaUseScriptHost.DyingAdmission), ("talk", AreaScriptHost.SkillTalkAdmission) };
        var without = new (string Name, ScrAdmissionSet Set)[] { ("S33", ScrAdmissionSet.S33), ("S34", ScrAdmissionSet.S34) };
        var counts = sets.Concat(without).ToDictionary(s => s.Name, _ => 0);
        var withStory = 0;
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            bool Uses(ScrClause clause, bool condition) => (condition ? ScrAdmissionSet.StoryConditions : ScrAdmissionSet.StoryActions).Contains(clause.Type);
            if (!program.Lines.Any(l => Uses(l.Condition, true) || Uses(l.OnTrue, false) || Uses(l.OnFalse, false))) continue;
            withStory++;
            foreach (var (name, set) in sets.Concat(without)) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with a story or time command {withStory}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, counts["S33"], "S33 refuses every one of them by construction");
        // Ledger SQ13: 96 installed scripts use one of the 19 commands; S34 admits 52 of them whole (the others also hold a command of the other planned slices, which the census ranks), the hosts' sets
        // 23 (use), 35 (placed), 24 (heartbeat), 47 (dying) and 26 (talk); ARC-SCRIPT-1163 (S36: the use and heartbeat sets also hold the NPC state actions) moves placed to 36, heartbeat to 25 and dying to 51.
        // ARC-SCRIPT-1164 (set S37, the world-object commands): the use set admits 24, placed 37, heartbeat 26 and dying 52 (one more each); the talk set stays at 26.
        // ARC-SCRIPT-1279 (the four item commands got a host): the use set admits 33, placed 47 and dying 62 (9, 10 and 10 more); the heartbeat set stays at 26 and the talk set at 26.
        // ARC-SCRIPT-1481 (the shared use set also holds the vitals commands, heal fatigue, magick-tech adjustment, script eye candy and the self-destroy): the dying set admits 63 (one more); the others stay.
        // ARC-SCRIPT-1168 (the talk sets hold the six cast actions): the talk set admits one more, 31 (30 before). The placed, heartbeat and dying counts had moved with later slices (the critter-state conditions of
        // ARC-SCRIPT-1733, 1771 and others: 52, 29 and 72, the talk set 30) without this assertion following; it is brought to the measured counts here.
        // ARC-SCRIPT-1815: the heartbeat set gained the scenery and object-variable focus kinds, assign object and float line (heartbeats 41, from 29).
        Assert.AreEqual((96, 52, 37, 59, 46, 80, 34), (withStory, counts["S34"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"], counts["talk"]));
    }
}
