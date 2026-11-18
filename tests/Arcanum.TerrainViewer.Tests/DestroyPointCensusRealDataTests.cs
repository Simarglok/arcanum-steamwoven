using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2686 opt-in census (DAT_ROOT = the game folder, ledger BS2, BS3a): the destroy points (2) the 79 maps' objects carry, by object type and script, each script against
/// <see cref="AreaUseScriptHost.DestroyPointAdmission"/>. The original always runs the point (0x43CCA0); the host keeps the object when a point's run does not apply, so a script the set refuses
/// would make an object that can never be broken. <c>ARC_DESTROY_POINT_REPORT</c>, when set, names a text file the rows are written to.
/// </summary>
[TestClass]
public sealed class DestroyPointCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_destroy_points_by_object_type_against_the_destroy_point_set()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var report = new List<string>();
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        var rows = new Dictionary<(int Type, int Script), int>();
        var maps = 0;
        foreach (var destination in first.MapDestinations!.Values)
        {
            var result = AreaLoadOperation.Execute(1, Arguments(root, destination.Name), default);
            if (result.Area is null) continue;
            maps++;
            foreach (var row in result.Area.Snapshot.Objects.GroupBy(AreaUseScriptHost.EntityId).Select(g => g.First()))
                if (AreaUseScriptHost.LoadedSlot(row, ScrPoint.Destroy) is { Script: > 0 } slot)
                    rows[(row.Type, slot.Script)] = rows.GetValueOrDefault((row.Type, slot.Script)) + 1;
        }
        var refused = new List<(int Type, int Script, string Why)>();
        foreach (var ((type, script), count) in rows.OrderBy(r => r.Key.Type).ThenBy(r => r.Key.Script))
        {
            var admitted = content.Script(script) is { Status: ScriptContentStatus.Read, Program: { } program } && ScrAdmission.Admit(program, AreaUseScriptHost.DestroyPointAdmission) is { IsAdmitted: true };
            var why = content.Script(script) is { Status: ScriptContentStatus.Read, Program: { } read } ? ScrAdmission.Admit(read, AreaUseScriptHost.DestroyPointAdmission).Refusal is { } r ? $"{r.Code}@{r.Line}/{r.Type}/{r.Kind}" : "-" : "unreadable";
            report.Add($"destroy point: object type {type} script {script}: {count} rows, admitted {admitted} {why}");
            if (!admitted) refused.Add((type, script, why));
        }
        report.Add($"maps {maps}");
        if (Environment.GetEnvironmentVariable("ARC_DESTROY_POINT_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
        foreach (var line in report) Console.WriteLine("DESTROY-POINT-CENSUS|" + line);
        Assert.AreEqual(79, maps);
        // Ledger BS3f: 17 rows over seven scripts; no door (1), container (2) or trap carries a point, and the set admits every script (a run that did not apply keeps the object, so a refused one would never break).
        CollectionAssert.AreEqual(new[] { (3, 2403, 1), (3, 2560, 16), (5, 1275, 1), (7, 1256, 2), (14, 2519, 1), (16, 1257, 1), (16, 2687, 4) },
            rows.OrderBy(r => r.Key.Type).ThenBy(r => r.Key.Script).Select(r => (r.Key.Type, r.Key.Script, r.Value)).ToArray());
        Assert.AreEqual(0, refused.Count, string.Join("; ", refused.Select(r => $"type {r.Type} script {r.Script}: {r.Why}")));
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
