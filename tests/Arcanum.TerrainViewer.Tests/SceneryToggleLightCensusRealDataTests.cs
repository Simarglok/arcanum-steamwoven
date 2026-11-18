using Arcanum.Data.Lighting;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2585 opt-in survey (DAT_ROOT = the game folder, ledger OT2, OT10): which sceneries of the 79 maps carry a light, and which of the sceneries the installed scripts 1179 and 2407
/// toggle (the ones named 1609 and 1668, ledger OT9) do. <c>ARC_SCENERY_LIGHT_REPORT</c>, when set, names a text file the rows are written to.
/// </summary>
[TestClass]
public sealed class SceneryToggleLightCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Sceneries_the_installed_toggle_scripts_loop_over_and_their_lights()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in survey; this is not a synthetic pass."); return; }
        var report = new List<string>();
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        int maps = 0, sceneries = 0, lit = 0;
        var byName = new SortedDictionary<int, (int Rows, int Lit, int Lights)>();
        foreach (var destination in first.MapDestinations!.Values)
        {
            var result = AreaLoadOperation.Execute(1, Arguments(root, destination.Name), default);
            if (result.Area is null) continue;
            maps++;
            var snapshot = result.Area.Snapshot;
            Assert.AreEqual(LightingFailure.None, ObjectLightReader.Describe(snapshot, out var lights));
            foreach (var row in snapshot.Objects.Where(o => o.Type == 3 && o.Scope == Arcanum.Data.Area.AreaScope.Core))
            {
                sceneries++;
                var name = AreaUseScriptHost.NameOf(row);
                var own = lights.Count(l => l.Object == row.Key && l.Type == 3);
                if (own > 0) lit++;
                if (name is 1609 or 1668)
                {
                    var known = byName.GetValueOrDefault(name);
                    byName[name] = (known.Rows + 1, known.Lit + (own > 0 ? 1 : 0), known.Lights + own);
                    report.Add($"map {destination.Name}: scenery named {name} cell ({row.WorldX},{row.WorldY}) lights {own}");
                }
            }
        }
        foreach (var (name, found) in byName) report.Add($"name {name}: {found.Rows} sceneries, {found.Lit} with a light, {found.Lights} lights");
        report.Add($"maps {maps}: sceneries {sceneries}, with a light {lit}");
        if (Environment.GetEnvironmentVariable("ARC_SCENERY_LIGHT_REPORT") is { Length: > 0 } output) File.WriteAllLines(output, report);
        Assert.AreEqual(79, maps);
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
