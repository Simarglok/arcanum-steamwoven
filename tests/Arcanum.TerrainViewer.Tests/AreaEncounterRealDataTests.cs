using Arcanum.Data.Content;
using Arcanum.Simulation.Travel;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in check of the installed module's encounter tables (ARC-WORLD-1159, <c>DAT_ROOT</c> = the game folder): the loader accepts the file the way the original's does, with the counts of the census pinned (numbers only, no content).</summary>
[TestClass]
public sealed class AreaEncounterRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_loads_its_charts_and_tables()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in encounter check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root, "Arcanum");
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var tables = AreaWorldMap.ReadEncounterTables(stack);
        Assert.AreEqual(EncounterTablesStatus.Loaded, tables.Status, tables.Detail);
        // The census of the installed file: the 54 terrain tables and the two special ones, 211 rows of 297 groups in all, 45 rows behind a global flag and one row with a use cap.
        Assert.AreEqual((56, 23, 19, 2), (tables.Tables.Count, tables.Frequency.Count, tables.Power.Count, tables.Override.Count));
        Assert.AreEqual((211, 0, 45, 1, 297), (tables.Tables.Sum(t => t.Count), tables.Tables.Count(t => t.Count == 0), tables.Tables.SelectMany(t => t).Count(r => r.GlobalFlag >= 0),
            tables.Tables.SelectMany(t => t).Count(r => r.TriggerCap >= 0), tables.Tables.SelectMany(t => t).Sum(r => r.Groups.Count)));
    }
}
