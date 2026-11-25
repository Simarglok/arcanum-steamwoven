using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const string TakeReturnCode = "ARC-WORLD-3122";

    // ARC-WORLD-3122 (FR-WORLD-ITEM ground items) native observation (stage "take-return", only when named, with --hero and --debug-mode, on a main-map window with a map exit: the Crash Site): the hero takes up to three
    // ground items the window draws and loots up to three items of the holders (a body, a container), and gives one taken ground item to a living NPC of the window (as the exchange with a companion does); the taken items
    // are hidden and are no pickup target. The hero uses an exit into another map and the exit back, twice; then the window moves to the farthest area of the main map and back to the first one (the world map's Teleport).
    // After each step the same items are still hidden (not drawn, not a pickup target, not on the session's ground) and an item not taken is still a pickup target.
    private async Task RunAreaTakeReturnSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{TakeReturnCode} take-return smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TAKE_RETURN_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is null || _mapTransfers is null || _mapWindowCentre is null)
        { GD.Print($"{TakeReturnCode} native-smoke take-return NONE (needs --hero, an area session, its inventory host and a window of a map with exits)"); return; }
        var homeRuntime = CurrentAreaMap;
        var mapId = new SessionMapId(homeRuntime);
        if (_inventoryHost.Map(homeRuntime) is not { } window || session.QueryMapInventory(mapId) is not { } inventory)
        { GD.Print($"{TakeReturnCode} native-smoke take-return NONE (the shown map has no inventory)"); return; }
        var candidates = inventory.Ground.Select(ground => ground.Item.Entity).Where(entity => window.TryGetRow(entity, out _)
            && _inventoryHost.Target(session, homeRuntime, window.Row(entity).Key) is { Kind: AreaInventoryTargetKind.GroundItem }).OrderBy(e => e, StringComparer.Ordinal).ToArray();
        var taken = new List<string>();
        foreach (var entity in candidates)
        {
            if (taken.Count == 3) break;
            if (AreaInventoryHost.Moved(_inventoryHost.PickUp(session, entity))) taken.Add(entity);
        }
        // The items of a holder (a dead body or a container): looted into the pack; they were never drawn on the ground.
        static bool Admitted(SessionInventoryItem item) => item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo);
        var looted = new List<string>();
        foreach (var holder in inventory.Holders.Where(h => h.Items.Any(Admitted) && window.TryGetRow(h.Entity, out _)).OrderBy(h => h.Entity, StringComparer.Ordinal))
        {
            if (looted.Count >= 3) break;
            foreach (var item in holder.Items.Where(Admitted).Take(3 - looted.Count).ToArray())
                if (window.TryGetRow(item.Entity, out _) && AreaInventoryHost.Moved(_inventoryHost.Loot(session, holder.Entity, item.Entity))) looted.Add(item.Entity);
        }
        GD.Print($"{TakeReturnCode} stage picked={taken.Count} looted={looted.Count}");
        // The hero gives one taken ground item to a living NPC of the window: it leaves the hero's list, and the window still must not draw it (the report: items given to a companion in the cave lay on the ground again).
        var receiver = inventory.Holders.Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && window.TryGetRow(h.Entity, out _)).OrderBy(h => h.Entity, StringComparer.Ordinal).FirstOrDefault();
        var gaveOn = 0;
        // An ammunition stack merges and gold is no item to hand on: the first other taken item goes.
        var giveable = taken.FirstOrDefault(entity => inventory.Ground.Any(g => g.Item.Entity == entity && g.Item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo)));
        if (receiver is not null && giveable is not null)
        {
            var give = session.GiveItemToNpc(session.Version, receiver.Entity, giveable, _inventoryHost.InsertCheck);
            Require(give.Disposition == GameSessionDisposition.Changed, $"the hero's item was given to {receiver.Entity}: {give.Failure}");
            Require(session.QueryGivenItems().Any(g => g.Entity == giveable), "the given item is in the NPC's list");
            Require(!session.QueryPcInventory().Any(item => item.Entity == giveable), "the given item left the hero's list");
            gaveOn = 1;
        }
        else GD.Print($"{TakeReturnCode} stage give skipped: receiver={receiver is not null} giveable={giveable is not null}");
        var rest = candidates.Except(taken).ToArray();
        if (taken.Count == 0) { GD.Print($"{TakeReturnCode} native-smoke take-return NONE (no ground item of the window can be taken)"); return; }
        var keys = taken.Concat(looted).Select(entity => window.Row(entity).Key).ToArray();

        void Hidden(string when)
        {
            SyncAreaInventory();
            var now = _inventoryHost!.Map(CurrentAreaMap);
            Require(now is not null && _areaSession is { } s && _inventoryHost.Taken(s, CurrentAreaMap).IsSupersetOf(keys), $"the taken items are in the host's taken set {when}");
            Require(_inventoryHidden is not null && keys.All(_inventoryHidden.Contains), $"the taken items are hidden from the drawn set {when}");
            Require(keys.All(key => _inventoryHost.Target(_areaSession!, CurrentAreaMap, key) is null), $"a taken item is no pickup target {when}");
            var lying = _areaSession!.QueryMapInventory(new SessionMapId(CurrentAreaMap))?.Ground.Select(g => g.Item.Entity).ToHashSet(StringComparer.Ordinal) ?? [];
            Require(!taken.Concat(looted).Any(lying.Contains), $"a taken item is on the session's ground {when}: {string.Join(",", taken.Concat(looted).Where(lying.Contains))}");
            var still = rest.Where(entity => now!.TryGetRow(entity, out _)).ToArray();
            Require(still.Length > 0 && still.Any(entity => _inventoryHost.Target(_areaSession!, CurrentAreaMap, now!.Row(entity).Key) is { Kind: AreaInventoryTargetKind.GroundItem }), $"an item not taken is still a pickup target {when}");
        }
        Hidden("after the pickup");

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
        var legs = 0; var windowMoved = false;
        try
        {
            for (var round = 1; round <= 2; round++)
            {
                if (await UseMapExitAsync(site => site.DestinationRuntime != homeRuntime) is not { } there)
                { GD.Print($"{TakeReturnCode} native-smoke take-return NONE (no usable exit to another map in the window)"); return; }
                Require(ReferenceEquals(_areaSession, session), $"the session was carried out (round {round})");
                for (var i = 0; i < 5; i++) AreaSmokeFrame();
                var back = await UseMapExitAsync(site => site.DestinationRuntime == homeRuntime) ?? throw new InvalidOperationException("AREA_TAKE_RETURN_NO_EXIT_BACK");
                Require(ReferenceEquals(_areaSession, session) && CurrentAreaMap == homeRuntime, $"the session was carried back to the home map (round {round})");
                for (var i = 0; i < 5; i++) AreaSmokeFrame();
                Hidden($"after the return {round} from {there.To}");
                legs++;
            }
            // AC2: the window moves away and back.
            if (!DebugMode) GD.Print($"{TakeReturnCode} take-return window move skipped (needs --debug-mode)");
            else
            {
                for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
                Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "world map data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
                var data = _worldMapData!.Data!;
                void Settle()
                {
                    var grid = (AreaGridSession)_worldActor.Session!;
                    AreaSmokePlace(new AreaMapRuntime(_readyArea!.Snapshot, grid.Snapshot, grid.Chart), grid.State.CommittedCell);
                    for (var i = 0; i < 10; i++) AreaSmokeFrame();
                }
                async Task TeleportAsync(int area, string what)
                {
                    InventoryKey(Key.W);
                    Require(WorldMapOpen && _worldMap is not null, $"W opened the world map {what}: {_worldMapStatus}");
                    var carried = _areaSession!;
                    _worldMapPanel!.TeleportForSelfTest(area);
                    Require(!WorldMapOpen && _mapHandoff is not null, $"Teleport {what} started the load: {_worldMapPanel.MessageForSelfTest}");
                    for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, carried)); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(_mapHandoff is null && ReferenceEquals(_areaSession, carried), $"the area loaded {what} with the session carried");
                    Settle();
                }
                InventoryKey(Key.W);
                Require(WorldMapOpen && _worldMap is not null, "W opened the world map first: " + _worldMapStatus);
                var map = _worldMap!;
                var here = map.OnStartMap ? Arcanum.Simulation.Travel.WorldTravelGeometry.Location(map.Position, data.Width) : data.TravelAreas[1].Location;
                var home = Arcanum.Simulation.Travel.WorldTravelGeometry.AreaInRadius(data.TravelAreas, here, false, Arcanum.Simulation.Travel.TravelRuleProfile.Steamwoven);
                var far = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Location != default)
                    .OrderBy(a => Arcanum.Simulation.Travel.WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).Last();
                EndWorldMap();
                Require(home > 0, "an area covers the hero's place");
                await TeleportAsync(far, "to the far area");
                await TeleportAsync(home, "back to the first area");
                Require(CurrentAreaMap == homeRuntime, "the window is the first map's again");
                Hidden("after the window moved away and back");
                windowMoved = true;
            }
        }
        finally
        {
            if (WorldMapOpen) EndWorldMap();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
        GD.Print("ARC-WORLD-3122 native-smoke take-return PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Taken = taken.Count, Looted = looted.Count, Given = gaveOn, StillLying = rest.Length, Returns = legs, WindowMoved = windowMoved }));
    }
}
