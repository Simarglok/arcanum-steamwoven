using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO1–WO10): the opt-in check counts the installed scripts that use one of the 13 world-object commands and how many each set admits whole.</summary>
[TestClass]
public sealed class AreaWorldObjectScriptAdmissionTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_the_world_object_commands_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in world-object check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("S36", ScrAdmissionSet.S36), ("S37", ScrAdmissionSet.S37), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var withCommand = 0;
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            bool Uses(ScrClause clause, bool condition) => (condition ? ScrAdmissionSet.WorldObjectConditions : ScrAdmissionSet.WorldObjectActions).Contains(clause.Type);
            if (!program.Lines.Any(l => Uses(l.Condition, true) || Uses(l.OnTrue, false) || Uses(l.OnFalse, false))) continue;
            withCommand++;
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with a world-object command {withCommand}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, counts["S36"], "S36 refuses every one of them by construction");
        Assert.IsTrue(withCommand >= counts["S37"]);
        // Ledger WO13, WO18: 117 installed scripts use one of the 13 commands; S37 admits 84 whole, and the hosts' sets 39 (use), 69 (tile and sector), 35 (heartbeat) and 77 (dying) since ARC-SCRIPT-1279 gave the four item commands
        // (transfer item, set written start, destroy item named, toggle item inventory display) a host (ARC-SCRIPT-1164 had left them out: 25, 43, 30, 48); the rest hold a command of another planned slice.
        // ARC-SCRIPT-1815: the heartbeat set gained the scenery and object-variable focus kinds, assign object and float line (heartbeats 45, from 35).
        // Re-measured 2026-11-16 (ARC-SCRIPT-2321, set S63): the any-scenery focus kind (16) alone moves one script into the dying count; the other moves since the numbers above came with the sets merged since (group kinds S61, toggle state, portal kind; issue #1846).
        Assert.AreEqual((117, 84, 53, 92, 45, 102), (withCommand, counts["S37"], counts["use"], counts["placed"], counts["heartbeat"], counts["dying"]));
    }
}
