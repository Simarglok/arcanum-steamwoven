using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    // ARC-WORLD-1805 (TT18, TT20): a door or window is no use trigger of the map transfers; the door's use node queues the teleport its script asked for.
    // ARC-WORLD-2261 (TT20): the hero's teleport of a dialog script or a deferred call is a script transfer (the viewer takes it with the dialog open); a door's is not.
    [TestMethod]
    public void A_dialog_or_call_teleport_of_the_hero_is_queued_as_a_script_transfer_and_a_doors_is_not()
    {
        var (map, row) = Load();
        var transfers = new AreaMapTransfers(map, Set(map), Destinations(map));
        var pc = new ScrObjectRef(map.RuntimeMap, "pc");
        var queued = transfers.QueueScriptTeleport(new ScrTeleport(pc, 5076, 5, 6));
        Assert.AreEqual(AreaUseOutcome.Accepted, queued.Outcome, queued.Reason);
        Assert.IsTrue(transfers.Pending!.ByTalkScript);
        // TR4: a second request while one is pending is dropped and leaves the first as it was.
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.QueueScriptTeleport(new ScrTeleport(pc, 5076, 7, 8)).Outcome);
        Assert.AreEqual(new AreaStepCell(5, 6), transfers.Pending!.Arrival);
        transfers.Clear();
        Assert.AreEqual(AreaUseOutcome.Accepted, transfers.QueueDoorTeleport(row, 710, new ScrTeleport(pc, 5076, 5, 6)).Outcome);
        Assert.IsFalse(transfers.Pending!.ByTalkScript);
    }

    [TestMethod]
    public void A_door_is_no_use_trigger_and_its_scripts_teleport_is_queued_through_the_door_entry()
    {
        var (map, row) = Load();
        var asDoor = Use(row, 77, new(5, 6)) with { Object = new(WorldSectorCoordinate.TryFromId(row.Key.Sector!.Value, out var sector) ? sector : default, row.Key.SourceIndex, row.Key.ObjectIdSha256, AreaMapTransfers.PortalObjectType, row.PrototypeNumber) };
        var transfers = new AreaMapTransfers(map, Set(map, asDoor, asDoor with { Guards = MapTransitionGuard.ConditionalPath, Script = 11, Chain = [11] }), Destinations(map));
        Assert.AreEqual(0, transfers.Triggers.Count);
        Assert.AreEqual(0, transfers.ScriptedTriggers.Count);
        Assert.AreEqual(0, transfers.Unmatched);
        var pc = new ScrObjectRef(map.RuntimeMap, "pc");
        Assert.AreEqual(AreaUseOutcome.NotATrigger, transfers.RequestUse(row.Key, new(row.WorldX!.Value, row.WorldY!.Value)).Outcome);
        var elsewhere = new ScrTeleport(pc, 5076, 5, 6);
        Assert.IsNull(transfers.DoorTeleportRefusal(row, elsewhere));
        Assert.IsNotNull(transfers.DoorTeleportRefusal(row, new(pc, 5001, 1, 1)), "a map the mode cannot open refuses");
        var queued = transfers.QueueDoorTeleport(row, 710, elsewhere);
        Assert.AreEqual(AreaUseOutcome.Accepted, queued.Outcome, queued.Reason);
        Assert.AreEqual(new AreaMapTransfer(queued.Site!, map.RuntimeMap, new(77, 5076, "elsewhere"), new(5, 6), row.Key), transfers.Pending);
        Assert.AreEqual((MapTransitionTrigger.Use, 710), (transfers.Pending!.Site.Trigger, transfers.Pending.Site.Script));
        // TR4: while the hero's change is pending, another request is refused and its teleport dropped (no refusal asked of the caller).
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.QueueDoorTeleport(row, 710, elsewhere).Outcome);
        Assert.IsNull(transfers.DoorTeleportRefusal(row, new(pc, 5001, 1, 1)));
        Assert.AreEqual(77, transfers.Clear()!.To.Runtime);
        Assert.AreEqual(AreaUseOutcome.DestinationUnavailable, transfers.QueueDoorTeleport(row, 710, new(pc, 5001, 1, 1)).Outcome);
        Assert.IsNull(transfers.Pending);
    }

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
        Assert.AreEqual(0, transfers.Enter(new(cell.X + 1, cell.Y)).Count);
        // ARC-DEV-351 (RL21, RL24): the hero's arrival inside a same-map change of place asks for nothing: its own request is pending, so the jump point queues no transfer.
        var arrived = transfers.Enter(cell, requestsRefused: true).Single();
        Assert.AreEqual((AreaUseOutcome.TransitionPending, "REQUEST_REFUSED_DURING_CHANGE_OF_PLACE"), (arrived.Outcome, arrived.Reason));
        Assert.IsNull(transfers.Pending);
        var both = transfers.Enter(cell);
        Assert.AreEqual(1, both.Count, "without a script runner the tile's own teleport is not a second trigger");
        var entered = both[0];
        Assert.AreEqual(AreaUseOutcome.Accepted, entered.Outcome, entered.Reason);
        Assert.AreEqual(MapTransitionTrigger.JumpPoint, transfers.Pending!.Site.Trigger);
        Assert.IsTrue(transfers.Pending.SameMap);
        Assert.AreEqual(arrival, transfers.Pending.Arrival);
        Assert.IsNull(transfers.Pending.Trigger);
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.Enter(cell)[0].Outcome);
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
    // ARC-SCRIPT-015: a use site whose teleport stands behind guards runs its script; the runner stands in for the host.
    [TestMethod]
    public void A_guarded_use_site_runs_its_script_and_its_teleport_gives_the_destination()
    {
        var (map, row) = Load();
        var at = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var guarded = Use(row, 1, new(1, 1)) with { Guards = MapTransitionGuard.ConditionalPath | MapTransitionGuard.ComputedDestination, DestinationRuntime = 0 };
        var transfers = new AreaMapTransfers(map, Set(map, guarded), Destinations(map));
        Assert.AreEqual(0, transfers.Triggers.Count);
        CollectionAssert.AreEqual(new[] { row.Key }, transfers.ScriptedTriggers.ToArray());
        Assert.AreEqual(guarded, transfers.ScriptedSite(row.Key));
        Assert.AreEqual(row, transfers.Object(row.Key));

        var noRunner = transfers.RequestUse(row.Key, at);
        Assert.AreEqual((AreaUseOutcome.ScriptRefused, "NO_SCRIPT_RUNNER"), (noRunner.Outcome, noRunner.Reason));
        Assert.IsNull(transfers.LastScripted);
        var answer = new AreaScriptedUse(AreaScriptedUseOutcome.NoTeleport, null, ["It is locked."], null, "no teleport");
        var runs = 0;
        string? refusal = null;
        transfers.ScriptRunner = (site, obj, refuse) => { runs++; Assert.AreEqual((guarded, row), (site, obj)); refusal = answer.Teleport is { } t ? refuse(t) : null; return answer; };
        Assert.AreEqual(AreaUseOutcome.TooFar, transfers.RequestUse(row.Key, new(at.X + 5, at.Y)).Outcome);
        Assert.AreEqual(0, runs, "the use rule comes first");
        var ended = transfers.RequestUse(row.Key, at);
        Assert.AreEqual((AreaUseOutcome.ScriptEnded, "no teleport", 1), (ended.Outcome, ended.Reason, runs));
        Assert.AreEqual(answer, transfers.LastScripted);
        Assert.IsNull(transfers.Pending);

        answer = answer with { Outcome = AreaScriptedUseOutcome.Refused, Detail = "outside the set" };
        Assert.AreEqual((AreaUseOutcome.ScriptRefused, "outside the set"), (transfers.RequestUse(row.Key, at).Outcome, transfers.LastScripted!.Detail));

        var pc = new Arcanum.Scripting.Scr.ScrObjectRef(map.RuntimeMap, "pc");
        answer = answer with { Outcome = AreaScriptedUseOutcome.Teleport, Teleport = new(pc, 5076, 5, 6) };
        var accepted = transfers.RequestUse(row.Key, at);
        Assert.AreEqual(AreaUseOutcome.Accepted, accepted.Outcome, accepted.Reason);
        Assert.AreEqual((5076, 77, new WorldLocation(5, 6)), (transfers.Pending!.Site.DestinationCatalogue, transfers.Pending.Site.DestinationRuntime, transfers.Pending.Site.Arrival));
        Assert.AreEqual((new AreaMapDestination(77, 5076, "elsewhere"), row.Key), (transfers.Pending.To, transfers.Pending.Trigger));
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.RequestUse(row.Key, at).Outcome);
        Assert.IsNull(transfers.LastScripted, "a request that runs no script clears the last run");
        transfers.Clear();

        // The runner is told why a teleport to a map the mode cannot open would be refused, and its refusal is the answer.
        answer = answer with { Outcome = AreaScriptedUseOutcome.TeleportRefused, Teleport = new(pc, 5001, 5, 6), Detail = "DESTINATION_NOT_OPENED_IN_MAP_MODE 5001" };
        var refused = transfers.RequestUse(row.Key, at);
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "DESTINATION_NOT_OPENED_IN_MAP_MODE 5001", "DESTINATION_NOT_OPENED_IN_MAP_MODE 5001"),
            (refused.Outcome, refused.Reason, refusal));
        Assert.AreEqual(5001, refused.Site!.DestinationCatalogue);
        Assert.IsNull(transfers.Pending);
        // Map 4,998 keeps the current map and names its catalogue number.
        var here = transfers.Teleported(guarded, new(pc, 4998, 7, 8));
        Assert.AreEqual((map.Snapshot.CatalogueMap, map.RuntimeMap, new WorldLocation(7, 8)), (here.DestinationCatalogue, here.DestinationRuntime, here.Arrival));
    }

    [TestMethod]
    public void Only_conditional_or_computed_use_sites_are_scripted_and_a_plain_exit_of_the_same_object_wins()
    {
        var (map, row) = Load();
        var plain = Use(row, 77, new(5, 6));
        MapTransitionSite Guarded(MapTransitionGuard guards) => plain with { Guards = MapTransitionGuard.ConditionalPath | guards };
        foreach (var guard in new[] { MapTransitionGuard.TrapScript, MapTransitionGuard.MovesOtherObject, MapTransitionGuard.ContainerOpenCheck,
            MapTransitionGuard.UseRunsOtherAction, MapTransitionGuard.ObjectNotClickable, MapTransitionGuard.ObjectOffOrDestroyed,
            MapTransitionGuard.UnlistedDestination, MapTransitionGuard.CallChangesTriggerer })
            Assert.AreEqual(0, new AreaMapTransfers(map, Set(map, Guarded(guard)), Destinations(map)).ScriptedTriggers.Count, guard.ToString());
        var both = new AreaMapTransfers(map, Set(map, Guarded(MapTransitionGuard.None), plain), Destinations(map));
        Assert.AreEqual((1, 0), (both.Triggers.Count, both.ScriptedTriggers.Count));
        Assert.IsNull(both.ScriptedSite(row.Key));
    }
    // ARC-SCRIPT-017: tile and sector scripts run through the host's runner when the player enters them.
    [TestMethod]
    public void Entered_tile_and_sector_scripts_run_and_their_teleport_of_the_player_is_the_transfer()
    {
        var (map, row) = Load();
        var cell = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var sector = WorldSectorCoordinate.Containing(new(cell.X, cell.Y));
        var arrival = map.Snapshot.Terrain.Select(t => new AreaStepCell(t.WorldX, t.WorldY)).First(c => map.Steps.IsAdmitted(c) && c != cell);
        var tile = new MapPlacedScript(MapTransitionTrigger.TileEntry, sector, 5, new(cell.X, cell.Y), 0, 0, 600);
        var own = new MapPlacedScript(MapTransitionTrigger.SectorEntry, sector, -1, new(sector.X * 64L, sector.Y * 64L), 0, 0, 601);
        var set = new MapTransitionSet(new WorldMap("test", new(2, 0, 64, 64), null, null, null, [], [], [], null, []), new(map.RuntimeMap + 4999, "test", null), [], [], [tile, own]);
        var transfers = new AreaMapTransfers(map, set, Destinations(map));
        Assert.AreEqual((1, 1), (transfers.ScriptTiles.Count, transfers.ScriptSectors.Count));
        Assert.AreEqual(0, transfers.Enter(cell).Count, "without a runner a script tile is no trigger");
        Assert.IsNull(transfers.EnterSector(new(cell.X - 64, cell.Y), cell));

        var pc = new Arcanum.Scripting.Scr.ScrObjectRef(map.RuntimeMap, "pc");
        var answers = new Dictionary<int, AreaScriptedUse>
        {
            [600] = new(AreaScriptedUseOutcome.Teleport, new(pc, 4998, checked((int)arrival.X), checked((int)arrival.Y)), [], null, "teleport"),
            [601] = new(AreaScriptedUseOutcome.NoTeleport, null, ["line"], null, "no teleport"),
        };
        var ran = new List<int>();
        transfers.PlacedRunner = (placed, refuse) =>
        {
            ran.Add(placed.Script);
            var answer = answers[placed.Script];
            return answer.Teleport is { } t && refuse(t) is { } refusal ? answer with { Outcome = AreaScriptedUseOutcome.TeleportRefused, Detail = refusal } : answer;
        };
        Assert.IsNull(transfers.EnterSector(new(cell.X + 1, cell.Y), cell), "a step inside the sector enters nothing");
        var entered = transfers.EnterSector(new(sector.X * 64L - 1, cell.Y), cell)!;
        Assert.AreEqual((AreaUseOutcome.ScriptEnded, MapTransitionTrigger.SectorEntry, "line"), (entered.Outcome, entered.Site!.Trigger, transfers.LastScripted!.Lines.Single()));
        var stepped = transfers.Enter(cell).Single();
        Assert.AreEqual(AreaUseOutcome.Accepted, stepped.Outcome, stepped.Reason);
        Assert.AreEqual((true, arrival, MapTransitionTrigger.TileEntry), (transfers.Pending!.SameMap, transfers.Pending.Arrival, transfers.Pending.Site.Trigger));
        // With a transfer pending the run still happens; only its teleport is dropped (TR4).
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transfers.Enter(cell).Single().Outcome);
        CollectionAssert.AreEqual(new[] { 601, 600, 600 }, ran);
        transfers.Clear();
        // ARC-DEV-351: the hero's arrival inside a same-map change of place runs the script, and its teleport is dropped, with nothing pending afterwards.
        var landed = transfers.Enter(cell, requestsRefused: true).Single();
        Assert.AreEqual((AreaUseOutcome.TransitionPending, "REQUEST_REFUSED_DURING_CHANGE_OF_PLACE"), (landed.Outcome, landed.Reason));
        Assert.IsNull(transfers.Pending);
        CollectionAssert.AreEqual(new[] { 601, 600, 600, 600 }, ran, "the script ran");

        answers[600] = answers[600] with { Teleport = new(pc, 5001, 1, 1) };
        var refused = transfers.Enter(cell).Single();
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "DESTINATION_NOT_OPENED_IN_MAP_MODE 5001"), (refused.Outcome, refused.Reason));
        Assert.IsNull(transfers.Pending);
    }

    // ARC-SCRIPT-046: an NPC's step runs only the tile's script (the jump point is the PC's, ledger TR3), through its own runner, and
    // leaves the player's last run and the pending transfer alone.
    [TestMethod]
    public void An_npc_entering_a_script_tile_runs_only_its_script_through_the_critter_runner()
    {
        var (map, row) = Load();
        var cell = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var sector = WorldSectorCoordinate.Containing(new(cell.X, cell.Y));
        var jump = new MapTransitionSite(MapTransitionTrigger.JumpPoint, new(cell.X, cell.Y), null, 600, [600], 0, false, false,
            map.RuntimeMap + 4999, map.RuntimeMap, new(cell.X, cell.Y), MapTransitionGuard.None);
        var tile = new MapPlacedScript(MapTransitionTrigger.TileEntry, sector, 0, new(cell.X, cell.Y), 0, 0, 600);
        var set = new MapTransitionSet(new WorldMap("test", new(2, 0, 64, 64), null, null, null, [], [], [], null, []), new(map.RuntimeMap + 4999, "test", null),
            [jump], [], [tile]);
        var transfers = new AreaMapTransfers(map, set, Destinations(map));
        Assert.IsNull(transfers.EnterByCritter(cell, "oid:npc"), "without a runner an NPC's step fires nothing");
        Assert.ThrowsExactly<ArgumentException>(() => transfers.EnterByCritter(cell, ""));

        var asked = new List<(int Script, string Entity)>();
        var answer = new AreaScriptedUse(AreaScriptedUseOutcome.NoTeleport, null, [], null, "talk")
            { Dialog = new(new(map.RuntimeMap, "pc"), new(map.RuntimeMap, "oid:npc"), 601, 0, 1) };
        transfers.CritterTileRunner = (placed, critter) => { asked.Add((placed.Script, critter.Code)); return answer; };
        Assert.AreSame(answer, transfers.EnterByCritter(cell, "oid:npc"));
        Assert.IsNull(transfers.EnterByCritter(new(cell.X + 1, cell.Y), "oid:npc"), "a tile without a script");
        Assert.AreEqual((600, "oid:npc"), asked.Single());
        // ARC-DEV-328: a visitor is named by the map whose objects hold it; the shown map is the default.
        ScrObjectRef? named = null;
        transfers.CritterTileRunner = (_, critter) => { named = critter; return answer; };
        transfers.EnterByCritter(cell, "oid:npc");
        Assert.AreEqual(new ScrObjectRef(map.RuntimeMap, "oid:npc"), named);
        transfers.EnterByCritter(cell, "oid:guest", map.RuntimeMap + 7);
        Assert.AreEqual(new ScrObjectRef(map.RuntimeMap + 7, "oid:guest"), named);
        Assert.IsNull(transfers.Pending, "the jump point is not the NPC's");
        Assert.IsNull(transfers.LastScripted);
    }
    [TestMethod]
    public void A_jump_point_fires_before_the_tile_script_and_a_refused_run_leaves_the_unconditional_teleport()
    {
        var (map, row) = Load();
        var cell = new AreaStepCell(row.WorldX!.Value, row.WorldY!.Value);
        var sector = WorldSectorCoordinate.Containing(new(cell.X, cell.Y));
        var arrival = map.Snapshot.Terrain.Select(t => new AreaStepCell(t.WorldX, t.WorldY)).First(c => map.Steps.IsAdmitted(c) && c != cell);
        MapTransitionSite Cell(MapTransitionTrigger trigger, AreaStepCell at) => new(trigger, new(at.X, at.Y), null, 600, [600], 0, false, false,
            map.RuntimeMap + 4999, map.RuntimeMap, new(arrival.X, arrival.Y), MapTransitionGuard.None);
        var other = new AreaStepCell(cell.X + 1, cell.Y);
        var placed = new[] { cell, other }.Select((c, i) => new MapPlacedScript(MapTransitionTrigger.TileEntry, sector, i, new(c.X, c.Y), 0, 0, 600)).ToArray();
        var set = new MapTransitionSet(new WorldMap("test", new(2, 0, 64, 64), null, null, null, [], [], [], null, []), new(map.RuntimeMap + 4999, "test", null),
            [Cell(MapTransitionTrigger.JumpPoint, cell), Cell(MapTransitionTrigger.TileEntry, other)], [], placed);
        var transfers = new AreaMapTransfers(map, set, Destinations(map));
        var pc = new Arcanum.Scripting.Scr.ScrObjectRef(map.RuntimeMap, "pc");
        var answer = new AreaScriptedUse(AreaScriptedUseOutcome.Teleport, new(pc, 4998, 1, 1), [], null, "teleport");
        var runs = 0;
        transfers.PlacedRunner = (_, refuse) => { runs++; return answer.Teleport is { } t && refuse(t) is { } why ? answer with { Outcome = AreaScriptedUseOutcome.TeleportRefused, Detail = why } : answer; };

        // The jump point queues first; the tile script still runs and only its teleport is dropped (TR3, TR4).
        var both = transfers.Enter(cell);
        Assert.AreEqual((AreaUseOutcome.Accepted, MapTransitionTrigger.JumpPoint), (both[0].Outcome, both[0].Site!.Trigger));
        Assert.AreEqual((AreaUseOutcome.TransitionPending, MapTransitionTrigger.TileEntry, 1), (both[1].Outcome, both[1].Site!.Trigger, runs));
        Assert.AreEqual(arrival, transfers.Pending!.Arrival);
        transfers.Clear();

        // A run refused before it started leaves the tile's unconditional teleport (ARC-WORLD-031).
        answer = new(AreaScriptedUseOutcome.Refused, null, [], null, "outside the set");
        var fallback = transfers.Enter(other).Single();
        Assert.AreEqual((AreaUseOutcome.Accepted, 2), (fallback.Outcome, runs), fallback.Reason);
        Assert.AreEqual(arrival, transfers.Pending!.Arrival);
        transfers.Clear();
        answer = new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "guards held");
        Assert.AreEqual(AreaUseOutcome.ScriptEnded, transfers.Enter(other).Single().Outcome, "a run that happened decides, teleport or not");
        Assert.IsNull(transfers.Pending);
    }
}
