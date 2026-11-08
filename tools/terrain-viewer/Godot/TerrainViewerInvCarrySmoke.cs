using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-305 (B01, B36, smoke plan SM02) native observation (stage "inv-carry", only when named, with --hero and --debug-mode, at the Crash Site): the hero takes what a dead
    // body of the map holds (at most six items, as the Take all button does), and an item the session makes (a copy of the kit's wearable, laid on the ground and picked up: a
    // `made:` item, which has no object row in any map) is worn through the window's own wear step: the inventory host reads its wear facts from its prototype. The multiset of
    // (prototype, units, place) of the hero's items is recorded; the hero's window moves to a far area of the main map (the debug travel's Teleport, the window rebuild that left
    // the town unlisted), and F7, a step away and F8 bring the same hero back. After every step the multiset is unchanged, the inventory window opens and lists every held item,
    // the worn slots are unchanged and the made item still has its wear facts. NOT BUILT: the step into the temple map and back (the report's own map change, plan step 5).
    private async Task RunAreaInvCarrySmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-305 inv-carry smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_INV_CARRY_FAILED");
        }
        if (!DebugMode) { GD.Print("ARC-DEV-305 native-smoke inv-carry NONE (the stage needs --debug-mode)"); return; }
        if (_areaSession is not { } first || first.QueryHero() is null || _inventoryHost is null)
        { GD.Print("ARC-DEV-305 native-smoke inv-carry NONE (needs --hero, an area session and its inventory host)"); return; }
        if (InventoryOpen) EndInventory();
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "world map data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
        var data = _worldMapData!.Data!;
        var mapId = new SessionMapId(CurrentAreaMap);

        // Step 1a: what a dead body holds, taken one item at a time through the host.
        static bool Admitted(SessionInventoryItem item) => item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Key or SessionItemType.KeyRing or SessionItemType.Ammo);
        var body = first.QueryMapInventory(mapId)?.Holders.Where(h => h.Kind == SessionHolderKind.Npc && h.Dead && h.Items.Any(Admitted)).OrderBy(h => h.Entity, StringComparer.Ordinal).FirstOrDefault();
        if (body is null) { GD.Print($"ARC-DEV-305 native-smoke inv-carry NONE map={CurrentAreaMap} (no dead body holds an item to take)"); return; }
        var taken = new List<string>();
        foreach (var item in body.Items.Where(Admitted).Take(6).ToArray())
            if (AreaInventoryHost.Moved(_inventoryHost.Loot(first, body.Entity, item.Entity))) taken.Add(item.Entity);
        Require(taken.Count > 0, $"an item of {body.Entity} was taken");
        // Step 1b: a made item with wear facts: a copy of the kit's first wearable kind, laid down next to the hero and picked up again.
        var kit = first.QueryHeroKit().FirstOrDefault(item => _inventoryHost.Wearable(first, item.Entity, _areaKitWear) is not null);
        if (kit.Entity is null) { GD.Print("ARC-DEV-305 native-smoke inv-carry NONE (the hero's kit holds no wearable to copy)"); return; }
        var drop = AreaDropMapNow();
        Require(drop is not null, "the drop map of the shown map");
        var created = first.CreateGroundItem(first.Version, kit.Kind, mapId, first.QueryActor().Cell, drop!, DropOwner.PcSide);
        Require(created.Disposition == GameSessionDisposition.Changed && created.Item is { } made && made.StartsWith("made:", StringComparison.Ordinal), $"the made item was created: {created.Disposition} {created.Failure}");
        var madeEntity = created.Item!;
        var pick = _inventoryHost.PickUp(first, madeEntity);
        Require(AreaInventoryHost.Moved(pick), $"the made item was picked up: {pick.Failure}");
        Require(first.QueryMadeItems().Any(item => item.Entity == madeEntity), "the picked-up item is in the hero's made list");
        // Step 1c: worn through the window's own step.
        OpenInventory();
        Require(InventoryOpen, "the inventory window opens");
        var wearable = _inventoryHost.Wearable(first, madeEntity, _areaKitWear);
        Require(wearable is not null, "the made item has wear facts (B36)");
        var slot = AreaInventoryHost.WearSlots(wearable)[0];
        WearItem(madeEntity, slot);
        Require(first.QueryEquipment().Worn.Any(item => item.Entity == madeEntity && item.Slot == slot), $"the made item is worn as {slot}: {_inventoryStatus}");
        EndInventory();

        // The multiset of (prototype, units, place) of everything the hero holds.
        string[] Held(GameSessionOwner session)
        {
            var worn = session.QueryEquipment().Worn.ToDictionary(item => item.Entity, item => item.Slot);
            return session.QueryHeroKit().Select(item => (item.Entity, item.Kind)).Concat(session.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
                .Concat(session.QueryMadeItems().Select(item => (item.Entity, item.Kind)))
                .Select(item => $"{item.Kind.Prototype}|{session.QueryAmmo(item.Entity)?.Units ?? 1}|{(worn.TryGetValue(item.Entity, out var at) ? "worn:" + at : "bag")}")
                .OrderBy(line => line, StringComparer.Ordinal).ToArray();
        }
        var baseline = Held(first);
        var wornBaseline = first.QueryEquipment().Worn.Count;
        Require(baseline.Length >= 2 && baseline.Any(line => line.EndsWith("|bag", StringComparison.Ordinal)) && baseline.Any(line => line.Contains("|worn:", StringComparison.Ordinal)), $"the hero holds worn and bag items: {baseline.Length}");

        // After every step: the same multiset, the window opens and lists every held item, and the made item is wearable still.
        string Check(string when)
        {
            var session = _areaSession!;
            var now = Held(session);
            Require(now.SequenceEqual(baseline), $"the held items changed {when}: {string.Join(", ", baseline.Except(now))} lost, {string.Join(", ", now.Except(baseline))} new");
            Require(session.QueryEquipment().Worn.Count == wornBaseline, $"the worn slots changed {when}");
            Require(_inventoryHost is not null && InventoryActive && !_inventoryStatus.Contains("unavailable", StringComparison.Ordinal), $"the inventory is available {when}: {_inventoryStatus}");
            OpenInventory();
            Require(InventoryOpen, $"the inventory window opens {when}");
            var view = InventoryView(null);
            var listed = view.Pc.Select(line => line.Entity).ToHashSet(StringComparer.Ordinal);
            var held = session.QueryHeroKit().Select(item => item.Entity).Concat(session.QueryPcInventory().Select(item => item.Entity)).Concat(session.QueryMadeItems().Select(item => item.Entity)).ToArray();
            EndInventory();
            Require(held.All(listed.Contains), $"every held item is listed {when}: {string.Join(", ", held.Where(entity => !listed.Contains(entity)))}");
            Require(session.QueryMadeItems().Any(item => item.Entity == madeEntity), $"the made item is still held {when}");
            Require(_inventoryHost!.Wearable(session, madeEntity, _areaKitWear) is not null, $"the made item has wear facts {when}");
            return $"{when}:{now.Length}";
        }
        var checks = new List<string> { Check("before the move") };

        // The new grid starts its clock at 0: the hero is placed on its own cell again (the smoke clock restarts) before the frames, as the follower-far stage does.
        void Settle()
        {
            var grid = (AreaGridSession)_worldActor.Session!;
            AreaSmokePlace(new AreaMapRuntime(_readyArea!.Snapshot, grid.Snapshot, grid.Chart), grid.State.CommittedCell);
            for (var i = 0; i < 10; i++) AreaSmokeFrame();
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-305-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            // Step 3: the window moves to the farthest placed area of the main map (as the follower-far stage's does).
            InventoryKey(Key.W);
            Require(WorldMapOpen && _worldMap is not null, "W opened the world map: " + _worldMapStatus);
            var map = _worldMap!;
            var here = map.OnStartMap ? Arcanum.Simulation.Travel.WorldTravelGeometry.Location(map.Position, data.Width) : data.TravelAreas[1].Location;
            var target = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Location != default)
                .OrderBy(a => Arcanum.Simulation.Travel.WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).Last();
            var session = _areaSession!;
            _worldMapPanel!.TeleportForSelfTest(target);
            Require(!WorldMapOpen && _mapHandoff is not null, "Teleport started the load: " + _worldMapPanel.MessageForSelfTest);
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, session)); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && ReferenceEquals(_areaSession, session), "the far area loaded with the session carried");
            Settle();
            checks.Add(Check("after the window moved"));

            // Step 4: the report's quick save and quick load there.
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            var running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            Settle();
            checks.Add(Check("after the quick load"));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
        GD.Print("ARC-DEV-305 native-smoke inv-carry PASS " + System.Text.Json.JsonSerializer.Serialize(new { Body = body.Entity, Taken = taken.Count, Made = madeEntity, Slot = slot.ToString(), Held = baseline.Length, Worn = wornBaseline, Checks = checks }));
    }
}
