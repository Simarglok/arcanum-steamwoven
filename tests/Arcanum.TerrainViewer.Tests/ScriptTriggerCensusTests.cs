using System.Text.Json;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-047 opt-in census (DAT_ROOT = the game folder) for the script trigger audit (docs/research/script-trigger-audit.md): every
/// object script attachment of the 79 maps map mode opens, by attachment point, and how many of them the host that runs that point here
/// admits from line 0 (the use host for use and catching thief, the dialog host with a hero and its query host (ARC-SCRIPT-050) for dialog, the heartbeat set for the first
/// heartbeat and the heartbeat, the session's item set for the item points; any other point has no host). A run can still stop on an effect
/// its host cannot apply; admission is the static half. <c>ARC_SCRIPT_TRIGGER_CENSUS</c>, when set, names a JSON file the numbers are
/// written to (numbers only).
/// </summary>
[TestClass]
public sealed class ScriptTriggerCensusTests
{
    // The set the heartbeat host ran before ARC-SCRIPT-047 (ARC-WORLD-114), for the comparison column.
    private static readonly ScrAdmissionSet World114Heartbeat = new("S2-area-heartbeat-state-kill", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    private static ScrAdmissionSet? HostSet(int point) => point switch
    {
        ScrPoint.Use or AreaUseScriptHost.CatchingThiefPoint => AreaUseScriptHost.Admission,
        ScrPoint.Dialog => AreaScriptHost.SkillTalkAdmission,
        ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat => AreaUseScriptHost.HeartbeatAdmission,
        // ARC-SCRIPT-1483: the hit step's points (hit, critter hits, critical hit) run under the placed set.
        ScrPoint.Hit or ScrPoint.CritterHits or ScrPoint.CriticalHit => AreaUseScriptHost.CombatPointAdmission,
        SessionScriptPoint.Get or SessionScriptPoint.Drop or SessionScriptPoint.InsertItem or SessionScriptPoint.WieldOn or SessionScriptPoint.WieldOff
            or SessionScriptPoint.RemoveItem or SessionScriptPoint.Transfer => SessionItemScripts.Admission,
        _ => null,
    };

    [TestMethod, TestCategory("RealData")]
    public void Every_object_attachment_is_counted_by_point_with_what_its_host_admits()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        // Per point: attachment rows, distinct scripts, rows whose script is missing, rows admitted by the host's set, by the old heartbeat set.
        var rows = new int[ScrPoint.Count]; var missing = new int[ScrPoint.Count]; var admitted = new int[ScrPoint.Count]; var old = new int[ScrPoint.Count];
        var scripts = Enumerable.Range(0, ScrPoint.Count).Select(_ => new HashSet<int>()).ToArray();
        var refusals = new Dictionary<(int Point, string Refusal), int>();
        var maps = 0; var opening = new List<string>(); var openingAdmitted = new List<string>();
        foreach (var destination in first.MapDestinations!.Values)
        {
            var result = AreaLoadOperation.Execute(1, Arguments(root, destination.Name), default);
            if (result.Area is null) continue;
            maps++;
            foreach (var row in result.Area.Snapshot.Objects.GroupBy(AreaUseScriptHost.EntityId).Select(g => g.First()))
            for (var point = 0; point < ScrPoint.Count; point++)
            {
                if (point == ScrPoint.DialogOverride || AreaUseScriptHost.LoadedSlot(row, point) is not { Script: > 0 } slot) continue;
                rows[point]++; scripts[point].Add(slot.Script);
                if (content.Script(slot.Script) is not { Status: ScriptContentStatus.Read, Program: { } program }) { missing[point]++; continue; }
                if (HostSet(point) is { } set)
                {
                    var admission = ScrAdmission.AdmitFrom(program, set, 0);
                    if (admission.IsAdmitted) admitted[point]++;
                    else if (admission.Refusal is { } refusal)
                    {
                        // The first refusal of the reachable lines: an action or condition type, or a focus or value kind.
                        var key = refusal.Code switch
                        {
                            ScrRefusalCode.ActionNotAdmitted => $"action {refusal.Type}",
                            ScrRefusalCode.ConditionNotAdmitted => $"condition {refusal.Type}",
                            ScrRefusalCode.FocusKindNotAdmitted => $"focus {refusal.Kind}",
                            ScrRefusalCode.ValueKindNotAdmitted => $"value {refusal.Kind}",
                            _ => refusal.Code.ToString(),
                        };
                        refusals[(point, key)] = refusals.GetValueOrDefault((point, key)) + 1;
                    }
                }
                if (point is ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat && ScrAdmission.AdmitFrom(program, World114Heartbeat, 0).IsAdmitted) old[point]++;
                // The heartbeats that call an NPC's own dialog point (1100's opening talk).
                if (point == ScrPoint.Heartbeat && program.Lines.Any(l => l.OnTrue.Type == ScrCatalogue.Action.CallScriptEx && l.OnTrue.Value(1) == ScrPoint.Dialog))
                {
                    opening.Add($"{destination.Name}:{slot.Script}");
                    if (ScrAdmission.AdmitFrom(program, AreaUseScriptHost.HeartbeatAdmission, 0).IsAdmitted) openingAdmitted.Add(slot.Script.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        if (Environment.GetEnvironmentVariable("ARC_SCRIPT_TRIGGER_CENSUS") is { Length: > 0 } output)
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                maps,
                points = Enumerable.Range(0, ScrPoint.Count).Where(p => rows[p] > 0).Select(p => new
                {
                    point = p, rows = rows[p], scripts = scripts[p].Count, missing = missing[p], host = HostSet(p)?.Id, admitted = HostSet(p) is null ? (int?)null : admitted[p],
                    world114 = p is ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat ? old[p] : (int?)null,
                    top_refusals = refusals.Where(r => r.Key.Point == p).OrderByDescending(r => r.Value).ThenBy(r => r.Key.Refusal, StringComparer.Ordinal).Take(10)
                        .Select(r => new { refusal = r.Key.Refusal, rows = r.Value }),
                }),
                opening_heartbeats = opening.GroupBy(o => o).Select(g => new { at = g.Key, rows = g.Count() }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.AreEqual(79, maps);
        // Nine heartbeats call an NPC's own dialog point; Virgil's at the Crash Site is one the heartbeat set admits now. The data and the
        // ARC-WORLD-114 set are fixed; the host's set may widen (audit slices), so its counts are pinned from below: 33 of 141 after ARC-SCRIPT-047,
        // 81 of 141 and 23 of 23 after ARC-SCRIPT-049 (slice 1).
        Assert.AreEqual(9, opening.Count, string.Join(", ", opening));
        CollectionAssert.Contains(opening, "arcanum1-024-fixed:1100");
        // ARC-SCRIPT-049: five of them run (1717, 2602, 1490 and 1015 stop on commands of later slices).
        CollectionAssert.IsSubsetOf(new[] { "1100", "2700", "1130", "3081", "2106" }, openingAdmitted, string.Join(", ", openingAdmitted));
        Assert.AreEqual((141, 32, 23, 21), (rows[ScrPoint.Heartbeat], old[ScrPoint.Heartbeat], rows[ScrPoint.FirstHeartbeat], old[ScrPoint.FirstHeartbeat]));
        Assert.IsTrue(admitted[ScrPoint.Heartbeat] >= 81 && admitted[ScrPoint.FirstHeartbeat] >= 23, $"{admitted[ScrPoint.Heartbeat]}, {admitted[ScrPoint.FirstHeartbeat]}");
        // ARC-SCRIPT-050: the talk set admits 115 of the 180 dialog rows (52 before float line, get area, random, the guard conditions and obj is dead).
        Assert.AreEqual(180, rows[ScrPoint.Dialog]);
        Assert.IsTrue(admitted[ScrPoint.Dialog] >= 115, admitted[ScrPoint.Dialog].ToString());
    }

    /// <summary>
    /// ARC-SCRIPT-1483 (AC3, AC4; poison survey S9, ledger CP5): the installed scripts of the hit step's points (hit 7, critter hits 26, critical hit 34) and of the dialog point (9) that deal poison, as <c>--poison-census</c> names them (its
    /// <c>scripts_by_point</c>): 30032 at the hit point deals poison (action 60, damage type 1) and 30076 casts the college poison spell 11 there, 1345 at critter hits and 30199 at the critical hit deal poison, and 1059, the one
    /// poison site at the dialog point. The placed set (<see cref="AreaUseScriptHost.CombatPointAdmission"/>) admits 1345 whole; the three weapon scripts (30032, 30076, 30199: the magick-tech weapons, which scale their chance by the
    /// weapon's power ratio, action 91, and play eye candy, 120) are refused by it at their first command until the item power ratio is admitted for combat points (issue filed by ARC-SCRIPT-1483). The dialog site is read against
    /// the talk set. Every answer is reported (the file named by <c>ARC_COMBAT_POINT_POISON</c>, when set, receives it); only 1345 is pinned. Opt-in: <c>DAT_ROOT</c>.
    /// </summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_poison_scripts_of_the_combat_points_and_the_dialog_point_are_read_against_their_sets()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var report = new List<string>();
        ScrAdmissionResult Read(int point, int number, ScrAdmissionSet set)
        {
            Assert.AreEqual(ScriptContentStatus.Read, content.Script(number).Status, $"script {number}");
            var admission = ScrAdmission.AdmitFrom(content.Script(number).Program!, set, 0);
            report.Add($"point {point} script {number} set {set.Id} admitted={admission.IsAdmitted} refusal={admission.Refusal?.Code}:{admission.Refusal?.Type}");
            return admission;
        }
        Assert.IsTrue(Read(ScrPoint.CritterHits, 1345, AreaUseScriptHost.CombatPointAdmission).IsAdmitted, "1345, the critter-hits poison script, runs");
        foreach (var (point, number) in new[] { (ScrPoint.Hit, 30032), (ScrPoint.Hit, 30076), (ScrPoint.CriticalHit, 30199) }) Read(point, number, AreaUseScriptHost.CombatPointAdmission);
        Read(ScrPoint.Dialog, 1059, AreaScriptHost.SkillTalkAdmission);
        if (Environment.GetEnvironmentVariable("ARC_COMBAT_POINT_POISON") is { Length: > 0 } output) File.WriteAllLines(output, report);
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
