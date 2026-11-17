using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2324 opt-in census (DAT_ROOT = the game folder, ledger OT7, OT8): every installed script that runs toggle state (56), by the focus kind of its object operand,
/// and every object attachment of the 79 maps whose script toggles, by the object's type and the attachment point. The toggle flips the off flag of an object of any type
/// (ledger OT1); the census says which non-NPC objects an installed script can switch off. <c>ARC_TOGGLE_STATE_REPORT</c>, when set, names a text file the rows are written to.
/// </summary>
[TestClass]
public sealed class ToggleStateCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_toggle_state_scripts_by_focus_kind_and_the_attachments_that_run_them()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var report = new List<string>();
        // Static half: every script of the name index, each toggle line with the focus kind of operand 0.
        var toggling = new Dictionary<int, List<(int Line, int Kind)>>();
        foreach (var name in content.Names.Names)
        {
            if (content.Script(name.Number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse })
                    if (clause.Type == ScrCatalogue.Action.ToggleState)
                    {
                        if (!toggling.TryGetValue(name.Number, out var list)) toggling[name.Number] = list = [];
                        list.Add((line.Number, clause.Kind(0)));
                    }
        }
        foreach (var number in new[] { 1179, 1198, 1239, 1734, 2088, 2221, 2407, 2475, 2476, 2477, 2478 })
            if (content.Script(number) is { Status: ScriptContentStatus.Read, Program: { } shown })
                foreach (var line in shown.Lines)
                    report.Add($"dump {number} line {line.Number}: if {Show(line.Condition)} then {Show(line.OnTrue)} else {Show(line.OnFalse)}");
        foreach (var (script, lines) in toggling.OrderBy(t => t.Key))
            report.Add($"script {script}: " + string.Join(", ", lines.Select(l => $"line {l.Line} kind {(ScrFocusKind)l.Kind}")));
        // Placed half: the attachment rows of the 79 maps whose script toggles.
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        var rows = new Dictionary<(int Point, int Type, int Script), int>();
        var maps = 0; var dataOff = new Dictionary<int, int>();
        foreach (var destination in first.MapDestinations!.Values)
        {
            var result = AreaLoadOperation.Execute(1, Arguments(root, destination.Name), default);
            if (result.Area is null) continue;
            maps++;
            foreach (var row in result.Area.Snapshot.Objects.GroupBy(AreaUseScriptHost.EntityId).Select(g => g.First()))
            {
                if (row.Type is 1 or 2 or 3 && row.EffectiveFields.FirstOrDefault(f => f.Slot == 18) is { Presence: Arcanum.Data.Area.AreaPresence.Value, Number: { } flags } && (flags & 0x2) != 0)
                    dataOff[row.Type] = dataOff.GetValueOrDefault(row.Type) + 1;
                for (var point = 0; point < ScrPoint.Count; point++)
                    if (AreaUseScriptHost.LoadedSlot(row, point) is { Script: > 0 } slot && toggling.ContainsKey(slot.Script))
                        rows[(point, row.Type, slot.Script)] = rows.GetValueOrDefault((point, row.Type, slot.Script)) + 1;
            }
        }
        foreach (var ((point, type, script), count) in rows.OrderBy(r => r.Key.Point).ThenBy(r => r.Key.Type).ThenBy(r => r.Key.Script))
            report.Add($"placed: point {point} object type {type} script {script}: {count} rows");
        foreach (var (type, count) in dataOff.OrderBy(d => d.Key)) report.Add($"data off: object type {type}: {count} rows");
        report.Add($"maps {maps}");
        if (Environment.GetEnvironmentVariable("ARC_TOGGLE_STATE_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
        Assert.AreEqual(79, maps);
        // Ledger OT9: 209 installed scripts toggle, 198 of them their attachee; the other eleven name the looped or the extra object.
        Assert.AreEqual(209, toggling.Count);
        var named = toggling.Where(t => t.Value.Any(l => l.Kind != (int)ScrFocusKind.Attachee)).Select(t => t.Key).Order().ToArray();
        CollectionAssert.AreEqual(new[] { 1179, 1198, 1239, 1734, 2088, 2221, 2407, 2475, 2476, 2477, 2478 }, named);
        // The placed attachments of the windows that run a toggle are an NPC's or the item set's (item type 7, script 3030, and 1239 on item type 5): none is a scenery, portal or container.
        Assert.IsFalse(rows.Keys.Any(k => k.Type is 1 or 2 or 3), "a placed portal, container or scenery runs a toggle");
        // The map data stores 16 sceneries and 3 containers off (the view drew them before ARC-SCRIPT-2324).
        Assert.AreEqual((16, 3), (dataOff.GetValueOrDefault(3), dataOff.GetValueOrDefault(2)));
    }

    private static string Show(ScrClause clause) => $"{clause.Type}(" + string.Join(",", Enumerable.Range(0, 4).Select(i => $"{clause.Kind(i)}:{clause.Value(i)}")) + ")";

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
