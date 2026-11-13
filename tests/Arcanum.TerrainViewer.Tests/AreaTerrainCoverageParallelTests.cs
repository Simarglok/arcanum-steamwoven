using System.Collections.Concurrent;
using Arcanum.Data.Area;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-1454: the viewer's publication check (<c>AREA_TERRAIN_COVERAGE</c>) holds for every read of the synthetic strict owner, also when many
/// reads run at once. The self-test failed on it once and passed on the repeat; the check is a function of the snapshot and the plan alone, so a
/// failure here would point at shared state in the strict read.
/// </summary>
[TestClass]
public sealed class AreaTerrainCoverageParallelTests
{
    [TestMethod]
    public void EveryParallelReadDrawsExactlyItsCoreTerrain()
    {
        var failures = new ConcurrentBag<string>();
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
        {
            using var source = new AreaOwnerFixture(motionBodies: index % 2 == 0);
            var area = AreaLoadOperation.ReadArea(7, source.Owner, CancellationToken.None);
            var core = area.Snapshot.Terrain.Where(record => record.Scope == AreaScope.Core).Select(record => record.Key).ToHashSet();
            var drawn = area.Plan.Commands.Where(command => command.Identity is null).Select(command => command.Visual).ToArray();
            if (core.Count == 0 || drawn.Length != core.Count || !core.SetEquals(drawn))
                failures.Add($"read {index}: drawn={drawn.Length} core={core.Count} terrain={area.Snapshot.Terrain.Count} commands={area.Plan.Commands.Count}");
        });
        Assert.AreEqual(0, failures.Count, string.Join("; ", failures.Take(5)));
    }
}
