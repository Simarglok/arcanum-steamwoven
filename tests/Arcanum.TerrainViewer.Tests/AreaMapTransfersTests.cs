using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-031 over the public strict synthetic AREA fixture: map-mode transfers from hand-made transition sites
// (the data reader is ARC-DATA-025's, tested there).
[TestClass]
public sealed class AreaMapTransfersTests
{
    private static (AreaMapRuntime Map, AreaObjectRecord Object) Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var snapshot = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        var target = snapshot.Objects.First(o => o.Key.Kind == AreaSourceKind.Static && o.WorldX is not null
            && steps.Terrain(new(o.WorldX.Value, o.WorldY!.Value)) is { Disposition: not CardinalTraversalDisposition.Blocked, Coverage: CardinalTraversalCoverage.Supported });
        return (new(snapshot, steps, AreaChart.Exterior), target);
    }

    private static MapTransitionSite Use(AreaObjectRecord row, int runtime, WorldLocation arrival) => new(MapTransitionTrigger.Use,
        new(row.WorldX!.Value, row.WorldY!.Value), new(WorldSectorCoordinate.TryFromId(row.Key.Sector!.Value, out var sector) ? sector : default, row.Key.SourceIndex,
        row.Key.ObjectIdSha256, row.Type, row.PrototypeNumber), 10, [10], 0, false, false, runtime + 4999, runtime, arrival, MapTransitionGuard.None);

    private static MapTransitionSet Set(AreaMapRuntime map, params MapTransitionSite[] sites) =>
        new(new WorldMap("test", new(2, 0, 64, 64), null, null, null, [], [], [], null, []), new(map.RuntimeMap + 4999, "test", null), sites, []);

    private static Dictionary<int, AreaMapDestination> Destinations(AreaMapRuntime map) => new()
    {
        [map.RuntimeMap] = new(map.RuntimeMap, map.RuntimeMap + 4999, "test"),
        [77] = new(77, 5076, "elsewhere"),
    };

    [TestMethod]
    public void A_use_within_range_queues_one_transfer_to_a_map_the_mode_can_open()
    {
        var (map, row) = Load();
        var transfers = new AreaMapTransfers(map, Set(map, Use(row, 77, new(5, 6))), Destinations(map));
        Assert.AreEqual(1, transfers.Triggers.Count);
        Assert.AreEqual(0, transfers.Unmatched);
        var at = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var far = transfers.RequestUse(row.Key, new(at.X + 5, at.Y));
        Assert.AreEqual((AreaUseOutcome.TooFar, 5), (far.Outcome, far.Distance));
        var accepted = transfers.RequestUse(row.Key, at);
        Assert.AreEqual(AreaUseOutcome.Accepted, accepted.Outcome, accepted.Reason);
        Assert.AreEqual(new AreaMapTransfer(accepted.Site!, map.RuntimeMap, new(77, 5076, "elsewhere"), new(5, 6), row.Key), transfers.Pending);
        Assert.IsFalse(transfers.Pending!.SameMap);
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.RequestUse(row.Key, at).Outcome);
        Assert.AreEqual(transfers.Pending, transfers.Clear());
        Assert.IsNull(transfers.Clear());
        var other = map.Snapshot.Objects.First(o => o.Key != row.Key);
        Assert.AreEqual(AreaUseOutcome.NotATrigger, transfers.RequestUse(other.Key, at).Outcome);
    }

    [TestMethod]
    public void A_destination_the_mode_cannot_open_and_an_unadmitted_arrival_are_refused()
    {
        var (map, row) = Load();
        var at = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var start = new AreaMapTransfers(map, Set(map, Use(row, 1, new(1, 1))), new Dictionary<int, AreaMapDestination>());
        var refused = start.RequestUse(row.Key, at);
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "DESTINATION_NOT_OPENED_IN_MAP_MODE 5000"), (refused.Outcome, refused.Reason));
        Assert.IsNull(start.Pending);
        var same = new AreaMapTransfers(map, Set(map, Use(row, map.RuntimeMap, new(1, 1))), Destinations(map));
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "ARRIVAL_NOT_ADMITTED"), (same.RequestUse(row.Key, at).Outcome, same.RequestUse(row.Key, at).Reason));
    }

    [TestMethod]
    public void Entering_a_jump_point_queues_its_transfer_and_a_jump_point_wins_over_a_tile_script()
    {
        var (map, row) = Load();
        var cell = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var arrival = map.Snapshot.Terrain.Select(t => new AreaStepCell(t.WorldX, t.WorldY)).First(c => map.Steps.IsAdmitted(c) && c != cell);
        MapTransitionSite Cell(MapTransitionTrigger trigger, WorldLocation to) => new(trigger, new(cell.X, cell.Y), null, 0, [], 0, false, false,
            map.RuntimeMap + 4999, map.RuntimeMap, to, MapTransitionGuard.None);
        var guarded = Cell(MapTransitionTrigger.TileEntry, new(1, 1)) with { Guards = MapTransitionGuard.ConditionalPath };
        var transfers = new AreaMapTransfers(map, Set(map, Cell(MapTransitionTrigger.TileEntry, new(1, 1)), guarded, Cell(MapTransitionTrigger.JumpPoint, new(arrival.X, arrival.Y))),
            Destinations(map));
        Assert.AreEqual(1, transfers.TriggerCells.Count);
        Assert.IsNull(transfers.Enter(new(cell.X + 1, cell.Y)));
        var entered = transfers.Enter(cell)!;
        Assert.AreEqual(AreaUseOutcome.Accepted, entered.Outcome, entered.Reason);
        Assert.AreEqual(MapTransitionTrigger.JumpPoint, transfers.Pending!.Site.Trigger);
        Assert.IsTrue(transfers.Pending.SameMap);
        Assert.AreEqual(arrival, transfers.Pending.Arrival);
        Assert.IsNull(transfers.Pending.Trigger);
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.Enter(cell)!.Outcome);
    }

    [TestMethod]
    public void A_site_on_another_map_or_an_object_the_snapshot_does_not_hold_is_rejected_or_counted()
    {
        var (map, row) = Load();
        var wrong = new MapTransitionSet(new WorldMap("x", new(2, 0, 64, 64), null, null, null, [], [], [], null, []), new(5099, "x", null), [], []);
        Assert.AreEqual("AREA_MAP_TRANSFERS_MAP_MISMATCH", Assert.ThrowsExactly<ArgumentException>(() => new AreaMapTransfers(map, wrong, Destinations(map))).Message);
        var missing = Use(row, 77, new(5, 6)) with { Object = new(new(0, 0), 999, row.Key.ObjectIdSha256, 3, null) };
        var transfers = new AreaMapTransfers(map, Set(map, missing), Destinations(map));
        Assert.AreEqual((0, 1), (transfers.Triggers.Count, transfers.Unmatched));
    }
}
