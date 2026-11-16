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
        Assert.IsTrue(admitted[ScrPoint.Heartbeat] >= 98 && admitted[ScrPoint.FirstHeartbeat] >= 23, $"{admitted[ScrPoint.Heartbeat]}, {admitted[ScrPoint.FirstHeartbeat]}");
        // ARC-SCRIPT-050: the talk set admits 115 of the 180 dialog rows (52 before float line, get area, random, the guard conditions and obj is dead).
        Assert.AreEqual(180, rows[ScrPoint.Dialog]);
        Assert.IsTrue(admitted[ScrPoint.Dialog] >= 115, admitted[ScrPoint.Dialog].ToString());
        // ARC-SCRIPT-1562: the combat-point set admitted 24 of the 26 hit rows (2 before: the rest stopped at 91, 80 and condition 37), all 18 critter-hits rows and 3 of the 4 critical-hit rows. ARC-SCRIPT-1661: with obj is in combat (31) and adjust stat (137) it admits all 26, 18 and 4.
        // ARC-SCRIPT-2335 (ledger IV7): the item set (S14-item) admits 6 of the 28 insert-item rows on the shown maps' windows (3 before: the loop for, the container vicinity kinds, the name test and change art number free the three temple
        // display-case scripts 2992 to 2994); the other 22 stop on toggle state (56) in 16 rows, get story state (42), is dead (22), condition 46, quest state (6), the scenery and critter vicinity kinds (15, 8), one each.
        // ARC-SCRIPT-2346 (ledger IV9-IV11): toggle state (56) of the attachee item frees the 16 rows that stopped on it: the item set admits 22 of the 28; the other 6 stop on get story state (42), is dead (22), condition 46, quest state (6),
        // the scenery and critter vicinity kinds (15, 8), one each.
        Assert.AreEqual(28, rows[SessionScriptPoint.InsertItem]);
        // ARC-SCRIPT-2347 (ledger IV16): with the quest state condition, assign object, get object type, obj is dead and monster generator disabled the item set admits 23 of the 28 (22 before: the row that stopped on condition 22 or 46 joins); the 5 left stop on toggle
        // monster generator (110), set PC quest state (16), get story state (42) and the scenery and critter vicinity kinds (15, 8), one each.
        // ARC-SCRIPT-2374 (ledger IV21): set PC quest state (16) and give quest experience (63) free the row that stopped on action 16: 24 of the 28; the 4 left stop on toggle monster generator (110), get story state (42) and the scenery and critter vicinity kinds (15, 8), one row each.
        // ARC-SCRIPT-2375 (ledger IV25): get and set story state (42, 43) free the row that stopped on get story state (1094): 25 of the 28; the 3 left stop on toggle monster generator (110) and the scenery and critter vicinity kinds (15, 8), one row each. The set rumor
        // and rename scripts (1103, 1566, 1573) have no placed row on these maps (they are prototype scripts); the next test reads them whole.
        Assert.AreEqual(25, admitted[SessionScriptPoint.InsertItem]);
        Assert.IsTrue(admitted[ScrPoint.Hit] >= 26 && admitted[ScrPoint.CritterHits] == 18 && admitted[ScrPoint.CriticalHit] >= 4, $"{admitted[ScrPoint.Hit]}, {admitted[ScrPoint.CritterHits]}, {admitted[ScrPoint.CriticalHit]}");
    }

    /// <summary>
    /// ARC-SCRIPT-2375 (ledger IV25; AC2, AC3): the four installed point 21 scripts that set the story state (1094), a rumor (1103, 1566) or an item's name number (1573) are admitted whole by the item set, which is the static half of the claim that
    /// the item event runs them; the opt-in census above counts only the rows placed on the shown maps' windows (1094 is one of them). Opt-in: <c>DAT_ROOT</c>.
    /// </summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_story_state_rumor_and_rename_item_scripts_are_admitted_whole_by_the_item_set()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        foreach (var script in new[] { 1094, 1103, 1566, 1573 })
        {
            Assert.IsTrue(content.Script(script) is { Status: ScriptContentStatus.Read, Program: { } program }, $"script {script} is read");
            var admission = ScrAdmission.AdmitFrom(content.Script(script).Program!, SessionItemScripts.Admission, 0);
            Assert.IsTrue(admission.IsAdmitted, $"script {script}: {admission.Refusal}");
        }
    }

    /// <summary>
    /// ARC-SCRIPT-1483 (AC3, AC4; poison survey S9, ledger CP5): the installed scripts of the hit step's points (hit 7, critter hits 26, critical hit 34) and of the dialog point (9) that deal poison, as <c>--poison-census</c> names them (its
    /// <c>scripts_by_point</c>): 30032 at the hit point deals poison (action 60, damage type 1) and 30076 casts the college poison spell 11 there, 1345 at critter hits and 30199 at the critical hit deal poison, and 1059, the one
    /// poison site at the dialog point. The combat-point set (<see cref="AreaUseScriptHost.CombatPointAdmission"/>) admits 1345 whole and, since ARC-SCRIPT-1562, the three magick-tech weapon scripts (30032, 30076, 30199: they scale their damage by the
    /// weapon's power ratio, action 91, and the chance the victim's aptitude cancels it, action 80, and play eye candy, 120) and the hunting weapon 28007 (obj is animal, condition 37). The dialog site is read against
    /// the talk set (ARC-SCRIPT-1563: admitted whole, pinned). Every answer is reported (the file named by <c>ARC_COMBAT_POINT_POISON</c>, when set, receives it); only 1345 and 1059 are pinned. Opt-in: <c>DAT_ROOT</c>.
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
        // ARC-SCRIPT-1562: the poison weapons' hit (30032, 30076) and critical-hit (30199) scripts run since the combat-point set holds the magick-tech adjustments (91, 80) and play script eye candy (120); the hunting weapon 28007 and the NPC script 2188 need obj is animal (37).
        foreach (var (point, number) in new[] { (ScrPoint.Hit, 30032), (ScrPoint.Hit, 30076), (ScrPoint.CriticalHit, 30199), (ScrPoint.Hit, 28007) })
            Assert.IsTrue(Read(point, number, AreaUseScriptHost.CombatPointAdmission).IsAdmitted, $"{number}, the installed combat-point script, runs");
        // ARC-SCRIPT-1661: the area weapons 30064 and 30074 (obj is in combat, 31) and the alignment weapon 30252 (adjust stat, 137) run too; the hit and critical-hit scripts the poison census counts are all admitted.
        foreach (var number in new[] { 30064, 30074, 30252 })
            Assert.IsTrue(Read(ScrPoint.Hit, number, AreaUseScriptHost.CombatPointAdmission).IsAdmitted, $"{number}, the installed combat-point script, runs");
        // ARC-SCRIPT-1563: the dialog host applies a talk's damage, so the talk set admits 1059 whole (the plain dialog set, without the shown map's use host, still refuses it).
        Assert.IsTrue(Read(ScrPoint.Dialog, 1059, AreaScriptHost.SkillTalkAdmission).IsAdmitted, "1059, the dialog-point poison script, runs");
        Assert.IsFalse(Read(ScrPoint.Dialog, 1059, AreaScriptHost.Admission).IsAdmitted, "without the use host's queries the plain set refuses 1059");
        if (Environment.GetEnvironmentVariable("ARC_COMBAT_POINT_POISON") is { Length: > 0 } output) File.WriteAllLines(output, report);
    }

    /// <summary>
    /// ARC-SCRIPT-2319 (ledger GF8): the installed scripts that name the group focus kinds (13, 14), read against the sets of the hosts that run their points: the combat-point set (hit 7, miss 8, critical hit 34), the placed set (taking damage 23)
    /// and the talk set (dialog 9). The census of ARC-RES-2262 found 12 roots naming kind 13 (11 free alone, on 61 sites) and 4 naming kind 14 (2232, 2234, 2239, 1750); 2232, 2234 and 2239 also need the group in is undead and get armor (ARC-SCRIPT-2326). Every answer is reported (the file named by
    /// <c>ARC_GROUP_FOCUS_REPORT</c>, when set, receives it). Opt-in: <c>DAT_ROOT</c>.
    /// </summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_group_focus_scripts_are_read_against_the_sets_of_their_hosts()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var report = new List<string>();
        ScrAdmissionResult Read(int number, ScrAdmissionSet set)
        {
            Assert.AreEqual(ScriptContentStatus.Read, content.Script(number).Status, $"script {number}");
            var admission = ScrAdmission.AdmitFrom(content.Script(number).Program!, set, 0);
            report.Add($"script {number} set {set.Id} admitted={admission.IsAdmitted} refusal={admission.Refusal?.Code}:{admission.Refusal?.Type}");
            return admission;
        }
        // Kind 13: the combat-point set (hit 7, miss 8, critical hit 34) and the placed set (taking damage 23) run 1160, 2350, 2506, 2507 and 1749 whole; the dialog script 1160 (point 9) runs under the talk set; the use set holds 2350, 2506 and 2507.
        foreach (var number in new[] { 1160, 2350, 2506, 2507, 1749 })
        {
            var combat = Read(number, AreaUseScriptHost.CombatPointAdmission);
            Assert.IsTrue(combat.IsAdmitted, $"{number} under the combat-point set: {combat.Refusal?.Code} type {combat.Refusal?.Type} line {combat.Refusal?.Line}");
            Assert.IsTrue(Read(number, AreaUseScriptHost.PlacedAdmission).IsAdmitted, $"{number} under the placed set");
        }
        Assert.IsTrue(Read(1160, AreaScriptHost.SkillTalkAdmission).IsAdmitted, "1160, the dialog-point script, under the talk set");
        // Without the use host's queries the plain dialog set keeps the group kinds out (the host answers the followers).
        Assert.IsFalse(Read(1160, AreaScriptHost.SkillAdmission).IsAdmitted, "the plain dialog set refuses 1160");
        // Kind 14 (ARC-SCRIPT-2326, ledger AR6): 2232, 2234 and 2239 ask the group in the is undead condition (38) and the armour look (action 32, S62); the dialog point 9 runs them whole under the talk set, the plain dialog set still refuses them.
        foreach (var number in new[] { 2232, 2234, 2239 })
        {
            var talk = Read(number, AreaScriptHost.SkillTalkAdmission);
            Assert.IsTrue(talk.IsAdmitted, $"{number} under the talk set: {talk.Refusal?.Code} type {talk.Refusal?.Type} line {talk.Refusal?.Line}");
            Assert.IsFalse(Read(number, AreaScriptHost.SkillAdmission).IsAdmitted, $"{number} under the plain dialog set");
        }
        if (Environment.GetEnvironmentVariable("ARC_GROUP_FOCUS_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
