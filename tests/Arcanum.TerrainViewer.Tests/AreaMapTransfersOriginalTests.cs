using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-031 check (DAT_ROOT = the game folder): map mode reads each map's transitions from the content
/// stack, opens interiors only, and loads a destination at its authored arrival.</summary>
[TestClass]
public sealed class AreaMapTransfersOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void A_staircase_leads_to_the_next_level_at_its_arrival_and_the_way_out_to_the_start_map_is_refused()
    {
        var root = Root();
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        Assert.IsNull(first.MapTransitionsFailure);
        // Every listed map folder but the 2000 × 2000 start map (621 sector files) opens in map mode.
        Assert.AreEqual(78, first.MapDestinations!.Count);
        Assert.IsFalse(first.MapDestinations.ContainsKey(1));
        var map = new AreaMapRuntime(first.Area!.Snapshot, first.AreaStepSnapshot!, first.Area.Plan.Chart);
        var transfers = new AreaMapTransfers(map, first.MapTransitions!, first.MapDestinations);
        Assert.AreEqual(0, transfers.Unmatched);
        var down = transfers.Triggers.Single(key => transfers.Site(key)!.DestinationCatalogue == 5034);
        var exit = transfers.Triggers.Single(key => transfers.Site(key)!.DestinationCatalogue == 5000);
        AreaStepCell At(Arcanum.Data.Area.AreaObjectKey key) => new(transfers.Object(key).WorldX!.Value, transfers.Object(key).WorldY!.Value);
        var refused = transfers.RequestUse(exit, At(exit));
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "DESTINATION_NOT_OPENED_IN_MAP_MODE 5000"), (refused.Outcome, refused.Reason));
        var used = transfers.RequestUse(down, At(down));
        Assert.AreEqual(AreaUseOutcome.Accepted, used.Outcome, used.Reason);
        var pending = transfers.Pending!;
        Assert.AreEqual("crematorium-lev 2", pending.To.Name);

        var second = AreaLoadOperation.Execute(2, Arguments(root, pending.To.Name) with { AreaMapArrival = new(pending.Arrival.X, pending.Arrival.Y) }, default);
        Assert.AreEqual(5034, second.Area!.Snapshot.CatalogueMap);
        Assert.AreEqual(pending.Arrival, second.Area.Plan.Chart.ToWorld(second.Area.Plan.Chart.Spawn));
        var back = new AreaMapTransfers(new(second.Area.Snapshot, second.AreaStepSnapshot!, second.Area.Plan.Chart), second.MapTransitions!, second.MapDestinations!);
        CollectionAssert.AreEquivalent(new[] { 5033, 5035 }, back.Triggers.Select(key => back.Site(key)!.DestinationCatalogue).ToArray());
    }

    [TestMethod, TestCategory("RealData")]
    public void A_jump_point_moves_the_player_on_the_same_map()
    {
        var result = AreaLoadOperation.Execute(1, Arguments(Root(), "tulla interior"), default);
        var map = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, result.Area.Plan.Chart);
        var transfers = new AreaMapTransfers(map, result.MapTransitions!, result.MapDestinations!);
        // The 17 jump-point cells of the map, all on the same map (ARC-RES-035).
        Assert.AreEqual(17, transfers.TriggerCells.Count(cell => transfers.CellSite(cell)!.Trigger == MapTransitionTrigger.JumpPoint));
        var cell = transfers.TriggerCells.First(c => transfers.CellSite(c)!.Trigger == MapTransitionTrigger.JumpPoint && map.Steps.IsAdmitted(new(transfers.CellSite(c)!.Arrival.X, transfers.CellSite(c)!.Arrival.Y)));
        var entered = transfers.Enter(cell)!;
        Assert.AreEqual(AreaUseOutcome.Accepted, entered.Outcome, entered.Reason);
        Assert.IsTrue(transfers.Pending!.SameMap);
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root) return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in map transfer check; this is not a synthetic pass.");
        return string.Empty;
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
