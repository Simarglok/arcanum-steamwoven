using System.Text.Json;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-047 opt-in census (DAT_ROOT = the game folder) for the script trigger audit (docs/research/script-trigger-audit.md): every
/// object script attachment of the 79 maps map mode opens, by attachment point, and how many of them the host that runs that point here
/// admits from line 0 (the use host for use and catching thief, the dialog host with a hero for dialog, the heartbeat set for the first
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
        ScrPoint.Dialog => AreaScriptHost.SkillAdmission,
        ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat => AreaUseScriptHost.HeartbeatAdmission,
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
        var maps = 0; var opening = new List<string>();
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
                    opening.Add($"{destination.Name}:{slot.Script}");
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
        // Nine heartbeats call an NPC's own dialog point; Virgil's at the Crash Site is the one the heartbeat set admits now: of the 141
        // heartbeat rows it admits 33 from line 0, one more than before.
        Assert.AreEqual(9, opening.Count, string.Join(", ", opening));
        CollectionAssert.Contains(opening, "arcanum1-024-fixed:1100");
        Assert.AreEqual((141, 33, 32), (rows[ScrPoint.Heartbeat], admitted[ScrPoint.Heartbeat], old[ScrPoint.Heartbeat]));
        Assert.AreEqual((23, 21, 21), (rows[ScrPoint.FirstHeartbeat], admitted[ScrPoint.FirstHeartbeat], old[ScrPoint.FirstHeartbeat]));
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
