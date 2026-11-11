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
    // the worn slots are unchanged and the made item still has its wear facts. ARC-DEV-314 adds plan step 5, the report's own map change: the window moves to the Shrouded Hills town area, an exit object there is used into the
    // Panarii temple map, F7 and F8 are done inside it, and the exit back is used; the same checks run after each, and the window's text of every held item stays the same.
    // ARC-DEV-354 adds step 6, a load into another window as a fresh run does it: F7 in the town window, the window moves to the far area, and F8 opens the town window with a new
    // host that was handed no rows; the note, which the town window does not list, has its row read from the window it was taken in, and the same checks pass.
    private const string ReportNoteEntity = "oid:385e7e885ab0ea9f85b35e3fb2b0fef166e70562766f5d163d7aa1ddcd72cc7a";

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
        // ARC-MAGIC-031: the item names load on a worker; the stage reads the window's own text, so it waits for them as the other stages do (a race that a slow load turned into "name unavailable").
        for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
        Require(_hoverNames?.Status == AreaNamesStatus.Ready, "names " + _hoverNames?.Status);
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
        // ARC-DEV-336 (B51, B32): the report's note too (the written item of the report's save, a note of prototype 14083 taken from the holder of the Crash Site that lists it). Its use facts and its
        // reading are checked after every step: a note taken in one window of the main map has to read in the next. The stage fails when the map has no such note, so the check cannot pass vacuously.
        // The use runs the note's own use script at each check (a read has no other effect here).
        var noteSource = first.QueryMapInventory(mapId)?.Holders.SelectMany(h => h.Items.Where(i => i.Entity == ReportNoteEntity && i.Facts.Kind.Type == SessionItemType.Written).Select(i => (Holder: h, Item: i)))
            .OrderBy(p => p.Item.Entity, StringComparer.Ordinal).FirstOrDefault();
        Require(noteSource is { Item.Entity: not null }, $"the map lists the report's note (object {ReportNoteEntity})");
        var note = noteSource!.Value.Item.Entity!;
        Require(AreaInventoryHost.Moved(_inventoryHost.Loot(first, noteSource.Value.Holder.Entity, note)), "the report's note was taken");
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
        string[]? baselineTexts = null;
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
            var texts = view.Pc.Where(line => held.Contains(line.Entity, StringComparer.Ordinal)).Select(line => line.Text).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            EndInventory();
            Require(held.All(listed.Contains), $"every held item is listed {when}: {string.Join(", ", held.Where(entity => !listed.Contains(entity)))}");
            baselineTexts ??= texts;
            Require(texts.SequenceEqual(baselineTexts), $"the window's text of the held items changed {when}: {string.Join(" / ", baselineTexts.Except(texts))} lost, {string.Join(" / ", texts.Except(baselineTexts))} new");
            Require(session.QueryMadeItems().Any(item => item.Entity == madeEntity), $"the made item is still held {when}");
            Require(_inventoryHost!.Wearable(session, madeEntity, _areaKitWear) is not null, $"the made item has wear facts {when}");
            return $"{when}:{now.Length}{ReadNote(when)}";
        }
        // ARC-DEV-336: the note's use facts read, and the use icon on it opens the reading window.
        string ReadNote(string when)
        {
            var session = _areaSession!;
            Require(session.QueryPcInventory().Any(item => item.Entity == note), $"the note is still held {when}");
            var facts = _inventoryHost!.UseFacts(session, note, _areaKitUse);
            Require(facts is { CanUseBox: true, Type: AreaItemUseFacts.WrittenType }, $"the note's use facts read {when}: {(facts is null ? "none" : $"type {facts.Type} flags 0x{facts.ItemFlags:X}")}");
            OpenInventory();
            Require(InventoryOpen, $"the inventory window opens for the note {when}");
            UseHeroItem(note, AreaItemUseSource.UseIcon, shift: false);
            Require(ReadingOpen, $"the use icon on the note opened the reading window {when}: {LastItemUseForSelfTest?.Gate} {LastItemUseForSelfTest?.Message} {_readingStatus}");
            CloseReadingByPlayer();
            if (InventoryOpen) EndInventory();
            return "+note";
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
        var padSelected = false;
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

            // Step 5 (ARC-DEV-314, B01): the report's own map change: the Shrouded Hills town, into the Panarii temple map and back.
            SelectSimulatedPadForSelfTest(); padSelected = true;
            var town = Arcanum.Simulation.Travel.WorldTravelGeometry.AreaInRadius(data.TravelAreas, new Arcanum.Simulation.Travel.TravelLocation(90208, 84128), false, Arcanum.Simulation.Travel.TravelRuleProfile.Steamwoven);
            Require(town > 0, "an area covers the Shrouded Hills town");
            InventoryKey(Key.W);
            Require(WorldMapOpen && _worldMap is not null, "W opened the world map before the town: " + _worldMapStatus);
            var carried = _areaSession!;
            _worldMapPanel!.TeleportForSelfTest(town);
            Require(!WorldMapOpen && _mapHandoff is not null, "Teleport to the town started the load: " + _worldMapPanel.MessageForSelfTest);
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, carried)); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && ReferenceEquals(_areaSession, carried), "the town area loaded with the session carried");
            Settle();
            checks.Add(Check("in the town window"));
            var homeRuntime = CurrentAreaMap;
            bool Temple(Arcanum.Data.Maps.MapTransitionSite site) => _mapTransfers!.Destinations.Values.Any(d => d.Runtime == site.DestinationRuntime && d.Name.Contains("panarii temple", StringComparison.OrdinalIgnoreCase));
            var inside = await UseMapExitAsync(Temple);
            Require(inside is not null && _readyMapName?.Contains("panarii temple", StringComparison.OrdinalIgnoreCase) == true, $"the exit into the Panarii temple was used from {_readyMapName}: {inside?.To}");
            Require(ReferenceEquals(_areaSession, carried), "the session was carried into the temple map");
            Settle();
            checks.Add(Check("in the temple map"));
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved in the temple map: " + _quickSaveStatus);
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded in the temple map: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null && _readyMapName?.Contains("panarii temple", StringComparison.OrdinalIgnoreCase) == true, $"the load published a new session in the temple map: {_quickSaveStatus} {_readyMapName}");
            Settle();
            checks.Add(Check("after the quick load in the temple map"));
            var outside = await UseMapExitAsync(site => site.DestinationRuntime == homeRuntime);
            Require(outside is not null && _readyMapName?.Contains("panarii temple", StringComparison.OrdinalIgnoreCase) != true, $"the exit back to the town was used from {_readyMapName}: {outside?.To}");
            Settle();
            checks.Add(Check("back from the temple map"));

            // Step 6 (ARC-DEV-354): a load into another window, as a fresh run loads the save: saved in the town window, loaded from the far one. The load
            // opens the town window with a new host and no rows handed over (a quick load keeps none), and the town window does not list the note, so its
            // row has to be read by identity from the window it was taken in.
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved in the town window: " + _quickSaveStatus);
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            InventoryKey(Key.W);
            Require(WorldMapOpen && _worldMap is not null, "W opened the world map before the far area again: " + _worldMapStatus);
            var townSession = _areaSession!;
            _worldMapPanel!.TeleportForSelfTest(target);
            Require(!WorldMapOpen && _mapHandoff is not null, "Teleport to the far area started the load again: " + _worldMapPanel.MessageForSelfTest);
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, townSession)); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && ReferenceEquals(_areaSession, townSession), "the far area loaded again with the session carried");
            Settle();
            running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null, "F8 loads the town window's save with its window (a load into another window): " + _quickSaveStatus);
            await (_lastLoad ?? Task.CompletedTask);
            for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load into the town window published a new session: " + _quickSaveStatus);
            Require(_inventoryHost is not null && !_inventoryHost.Maps.Any(shown => shown.TryGetRow(note, out _)), "the town window does not list the note (its row is read, not shown)");
            Require(_inventoryHost!.KnownRow(note) is not null, "the note's row was read from the window it was taken in");
            Settle();
            checks.Add(Check("after a load into another window"));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            if (padSelected) ReleaseSimulatedPadForSelfTest();
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
        GD.Print("ARC-DEV-305 native-smoke inv-carry PASS " + System.Text.Json.JsonSerializer.Serialize(new { Body = body.Entity, Taken = taken.Count, Made = madeEntity, Slot = slot.ToString(), Note = note, Held = baseline.Length, Worn = wornBaseline, Checks = checks }));
    }
}
