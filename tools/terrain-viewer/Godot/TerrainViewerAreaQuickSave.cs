using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-011: quick save and quick load on the original F7 and F8 (docs/requirements/original-keyboard-mouse.md:
// both act on key release), one slot, no save list. They act only while an AREA game session exists; elsewhere the
// keys stay reserved. A key acts on its release when its fresh press and its release both came under the world
// guards (focused window, no palette, no UI focus); otherwise it is ignored and the HUD says so (an open dialog takes
// every key first, so there F7/F8 do nothing). A save
// records the actor's committed cell and facing in the session, captures the whole session and writes it atomically
// (GameSessionSaveSlot); the in-flight step, click route and walk-to-use/talk are host state and are not saved. A
// load reads, decodes and restores a new session before anything here changes; a refused load keeps the running
// session and scene. A successful load replaces the session, drops host state (route, approaches, a queued transfer)
// and stands the actor settled on the saved map and cell, as a transfer does; the dialog host moves to the loaded
// session (ARC-SCRIPT-006), so quests, rumors, flags and NPC dialog state come back with it. Domains that live in the session are
// carried by the snapshot without changes here.
// ARC-WORLD-038: map mode (--area-map) has its own slot (user://saves/map), so the Crash Site slot is never touched. A
// save there records the map's runtime number; a load onto the map shown is adopted in place, and a load whose actor
// stands on another map that map mode can open loads that map through the ordinary load path, carrying the loaded
// session and standing the actor on the saved cell (the running session is kept until the map is published with the
// loaded one, and comes back with the origin map when the destination does not load or refuses the loaded session; the
// saved cell and that map's inventory seed can only be checked once it is loaded).
// ARC-UI-010: the slot is the save list's auto slot (the original's SlotAuto "Auto-Save"): F7 writes it with its details and a
// preview, and F8 loads the newest save of the list by file date, which is the auto slot only when it is the newest (ledger
// SL1, SL2). The save and load window (TerrainViewerAreaSaveList) writes, loads and deletes the other saves of the same folder.
public partial class TerrainViewerApp
{
    private const string QuickSaveDirectory = "user://saves";
    private const string MapQuickSaveDirectory = "user://saves/map";
    private GameSaveLibrary? _saveLibrary;
    private GameSaveLibrary? _mapSaveLibrary;
    // The native smoke writes to its own temporary folder, never to the player's saves.
    private GameSaveLibrary? _saveLibraryForSelfTest;
    private readonly QuickKeyGate _quickKeys = new();
    private string? _quickSaveStatus;

    private enum QuickBusy { None, NoActor, Lifecycle, TransferPending, MapLoading }

    private GameSaveLibrary SaveLibrary => _saveLibraryForSelfTest ?? (_readyMapName is not null
        ? _mapSaveLibrary ??= new(SaveDirectory(MapQuickSaveDirectory))
        : _saveLibrary ??= new(SaveDirectory(QuickSaveDirectory)));

    private GameSessionSaveSlot QuickSaveSlot => SaveLibrary.AutoSlot;

    // ARC-DEV-032: an automated run keeps its quick saves in its isolated folder, never in the player's saves.
    private static string SaveDirectory(string user) => ViewerAutomatedStorage.Automated
        ? System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), ViewerStoragePaths.RelativeUserPath(user)) : ProjectSettings.GlobalizePath(user);

    // Map mode (ARC-RENDER-017) never touches the player's Crash Site quick save slot: it has its own (ARC-WORLD-038).
    private bool QuickKeysActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    /// <summary>Routes F7/F8 while quick save/load is available (<see cref="QuickKeyGate"/>). Returns true when the
    /// key belongs to it, so it is not reported as a reserved, unimplemented original key. An open dialog takes every
    /// key before this route, so F7/F8 do nothing while a conversation is open.</summary>
    private bool RouteQuickKey(WorldKeyDecision decision)
    {
        if (!QuickKeysActive) return false;
        var action = _quickKeys.Route(decision.Key, decision.Pressed, decision.Echo, _worldFocused && !WorldModal && !WorldUiFocus);
        switch (action)
        {
            case QuickKeyAction.NotQuickKey: return false;
            case QuickKeyAction.Ignored:
                SetQuickStatus((decision.Key == WorldKey.F7 ? "F7 quick save" : "F8 quick load") + " ignored: palette, dialog or UI focus owns input");
                break;
            case QuickKeyAction.Save: QuickSave(); break;
            case QuickKeyAction.Load: QuickLoad(); break;
        }
        return true;
    }

    private QuickBusy QuickBusyReason()
    {
        if (_worldActor.Session is not AreaGridSession || _readyArea is null) return QuickBusy.NoActor;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) return QuickBusy.Lifecycle;
        if (_mapHandoff is not null) return QuickBusy.MapLoading;
        if (AreaTransferPending) return QuickBusy.TransferPending;
        return QuickBusy.None;
    }

    private static string Describe(QuickBusy busy) => busy switch
    {
        QuickBusy.NoActor => "no area actor",
        QuickBusy.Lifecycle => "reset/reload in progress",
        QuickBusy.TransferPending => "map transfer pending",
        QuickBusy.MapLoading => "another map is loading",
        _ => "ready",
    };

    private int CurrentAreaMap => _areaTransitions?.CurrentMap ?? _readyArea!.Snapshot.RuntimeMap;

    /// <summary>F7: the auto slot, described "Auto-Save" as the original's F7 save, with a preview of the screen as it is.</summary>
    private void QuickSave() => SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, CaptureSaveThumbnail(), "F7 ");

    /// <summary>Saves the session into the save list: <paramref name="name"/> is the save to write, or null for a new numbered save.
    /// The HUD and the log say what happened; the written save's name, or null when nothing was saved.</summary>
    private string? SaveGame(string? name, string description, byte[]? thumbnail, string prefix)
    {
        var busy = QuickBusyReason();
        if (busy != QuickBusy.None) { SetQuickStatus(prefix + "save refused: " + Describe(busy)); return null; }
        // ARC-MAGIC-015: a casting goal is not saved (no goal state in the session); its run would wait for a release that never comes.
        if (_spellGoal is not null) { SetQuickStatus(prefix + "save refused: a cast is playing"); return null; }
        var grid = (AreaGridSession)_worldActor.Session!;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing);
        // ARC-WORLD-016: the companions' cells are session state too; a refused placement refuses the save.
        if (!RecordAreaCompanions("save")) { SetQuickStatus(prefix + "save refused: " + _companionNotice); return null; }
        var result = AreaQuickSave.Save(_areaSession!, placement, SaveLibrary, name, session => AreaSaveList.Summary(session, description, SaveLocation()), thumbnail);
        GD.Print("ARC-WORLD-011 quick-save " + System.Text.Json.JsonSerializer.Serialize(new {
            result.Saved, Name = result.Library?.Name, Failure = result.Library?.Failure.ToString(), Snapshot = result.Write?.Failure.ToString(), result.Write?.Detail,
            result.Write?.Bytes, result.Library?.Warning, Place = result.PlaceFailure.ToString(), Map = placement.Map.RuntimeMap, placement.Cell,
            Thumbnail = thumbnail?.Length ?? 0, Revision = _areaSession!.Version.Revision }));
        SetQuickStatus(prefix + result.Status);
        return result.Saved ? result.Library!.Name : null;
    }

    /// <summary>The location a save names: the map mode's map, else the Crash Site or its cave.</summary>
    private string SaveLocation() => _readyMapName ?? (InCave ? "Crash Site cave" : "Crash Site");

    /// <summary>A PNG of the frame on screen fitted into the original's fixed 468×300 preview (SL8), or null when the frame cannot be
    /// read (a headless run).</summary>
    private byte[]? CaptureSaveThumbnail()
    {
        if (DisplayServer.GetName() == "headless") return null;
        var image = GetViewport().GetTexture()?.GetImage();
        if (image is null || image.IsEmpty()) return null;
        var scale = Math.Min(468.0 / image.GetWidth(), 300.0 / image.GetHeight());
        image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Bilinear);
        var png = image.SavePngToBuffer();
        return png is { Length: > 0 } && png.Length <= SaveLibrary.Limits.MaxThumbnailBytes ? png : null;
    }

    /// <summary>F8: the newest save of the list (the original's latest-save helper), the auto slot only when it is the newest.</summary>
    private void QuickLoad() => LoadGame(SaveLibrary.Latest() is { } latest ? SaveLibrary.Slot(latest.Name) : QuickSaveSlot, "F8 ");

    /// <summary>Loads one save of the list. True when the loaded session was adopted or its map load started; on false the running
    /// session and scene are unchanged and the HUD says why.</summary>
    private bool LoadGame(GameSessionSaveSlot slot, string prefix)
    {
        // A load supersedes a queued transfer; it waits only for a reset/reload.
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle or QuickBusy.MapLoading) { SetQuickStatus(prefix + "quick load refused: " + Describe(busy)); return false; }
        var maps = AreaRuntimes();
        // ARC-WORLD-038: in map mode a save on another map is loaded with that map, where its cell is placed.
        AreaMapDestination? elsewhere = null;
        string? Refusal(ActorPlacement placement)
        {
            if (_readyMapName is null || maps.ContainsKey(placement.Map.RuntimeMap)) return PlacementRefusal(maps, placement);
            if (_mapTransfers?.Destinations.TryGetValue(placement.Map.RuntimeMap, out var destination) != true) return "map mode cannot open that map";
            elsewhere = destination; return null;
        }
        var result = AreaQuickSave.Load(slot, _areaSession!, Refusal, MapSoundDefault);
        GD.Print("ARC-WORLD-011 quick-load " + System.Text.Json.JsonSerializer.Serialize(new {
            Slot = System.IO.Path.GetFileName(slot.Path), Loaded = result.Owner is not null, Read = result.Read?.Failure.ToString(), Decode = result.Read?.DecodeFailure.ToString(),
            result.Read?.DecodePath, Restore = result.RestoreFailure.ToString() }));
        if (result.Owner is null) { SetQuickStatus(prefix + result.Status); return false; }
        // ARC-UI-004: the loaded maps' inventory seeds are attached to the restored session before anything changes; a
        // save whose taken items disagree with them is refused (the item would be lost or duplicated).
        if (_inventoryHost?.Attach(result.Owner) is { } attached && attached != GameSessionFailure.None)
        {
            result.Owner.Dispose();
            SetQuickStatus($"{prefix}quick load REFUSED: the save's inventory does not match the loaded maps ({attached})");
            return false;
        }
        // ARC-SCRIPT-006: the dialog host reads its state from the session, so it moves to the loaded one; a session
        // whose dialog values it cannot read is refused here, before anything changes.
        AreaConversationHost? talk = null;
        try { talk = _conversationHost?.Rebind(result.Owner); }
        catch (ArgumentException)
        {
            result.Owner.Dispose();
            SetQuickStatus(prefix + "quick load REFUSED: the save's dialog state is not readable");
            return false;
        }
        // The shown map's seeds and dialog state are checked above; the other map binds its own when it is published.
        var from = " from " + System.IO.Path.GetFileNameWithoutExtension(slot.Path);
        if (elsewhere is { } map) { QuickLoadOtherMap(result.Owner, map, prefix + result.Status + from); return true; }
        AdoptLoadedSession(result.Owner, maps, talk);
        SetQuickStatus(prefix + result.Status + from);
        return true;
    }

    /// <summary>ARC-WORLD-038: the loaded session goes with a load of its map (a pending transfer is dropped, the actor's host
    /// state is cleared, and the scene is cleared as the load starts); the running session is kept until the map is published
    /// with the loaded one, and comes back with the shown map when it is not.</summary>
    private void QuickLoadOtherMap(GameSessionOwner loaded, AreaMapDestination map, string status)
    {
        StopWorldRoute("QUICK_LOAD");
        CancelAreaUseApproach("quick load"); CancelTalkApproach("quick load"); CancelInventoryApproach("quick load"); CancelAreaAttack("quick load");
        if (_mapTransfers?.Clear() is { } dropped) GD.Print("ARC-WORLD-038 quick load dropped the queued transfer to=" + dropped.To.Name);
        var grid = (AreaGridSession)_worldActor.Session!;
        var actor = loaded.QueryActor();
        var origin = _arguments!;
        _mapHandoff = new(loaded, _readyArea!.Snapshot.RuntimeMap, map.Name, actor.Facing, origin, grid.Chart.ToWorld(grid.State.CommittedCell), map.Name,
            Previous: _areaSession, Kind: MapHandoffKind.QuickLoad, OriginFacing: grid.State.Facing);
        var next = origin with { AreaMapName = map.Name, AreaMapArrival = new(actor.Cell.X, actor.Cell.Y), AreaMapSector = null };
        GD.Print("ARC-WORLD-038 quick-load map " + System.Text.Json.JsonSerializer.Serialize(new { From = _readyArea.Snapshot.RuntimeMap, To = map, actor.Cell }));
        SetQuickStatus($"{status}; loading '{map.Name}'");
        _lastLoad = LoadTransferAsync(next);
    }

    /// <summary>The maps this run has loaded, by runtime map.</summary>
    private Dictionary<int, AreaMapRuntime> AreaRuntimes() => _areaTransitions is { } transitions
        ? transitions.Maps.ToDictionary(map => map, transitions.Map)
        : new() { [_readyArea!.Snapshot.RuntimeMap] = new(_readyArea.Snapshot, ((AreaGridSession)_worldActor.Session!).Snapshot, ((AreaGridSession)_worldActor.Session!).Chart) };

    private static string? PlacementRefusal(IReadOnlyDictionary<int, AreaMapRuntime> maps, ActorPlacement placement)
    {
        if (!maps.TryGetValue(placement.Map.RuntimeMap, out var map)) return "that map is not loaded in this run";
        var world = new AreaStepCell(placement.Cell.X, placement.Cell.Y);
        if (!map.Steps.IsAdmitted(world)) return "the cell is not admitted for movement";
        var chart = map.Chart.ToChart(world);
        if (!map.Chart.Contains(chart)) return "the cell is outside the map chart";
        return null;
    }

    private void AdoptLoadedSession(GameSessionOwner loaded, IReadOnlyDictionary<int, AreaMapRuntime> maps, AreaConversationHost? talk)
    {
        StopWorldRoute("QUICK_LOAD");
        CancelAreaUseApproach("quick load"); CancelTalkApproach("quick load"); CancelInventoryApproach("quick load"); CancelAreaAttack("quick load");
        if (_areaTransitions?.Cancel() is { } dropped) GD.Print("ARC-WORLD-011 quick load dropped the queued transfer to=" + dropped.ToMap);
        if (_mapTransfers?.Clear() is { } queued) GD.Print("ARC-WORLD-038 quick load dropped the queued transfer to=" + queued.To.Name);
        var actor = loaded.QueryActor();
        ReplaceAreaSession(loaded, maps, actor.Map.RuntimeMap, talk);
        var runtime = maps[actor.Map.RuntimeMap];
        _readyArea = _caveLoad is { } cave && runtime.RuntimeMap == cave.Area.Snapshot.RuntimeMap ? cave.Area : _exteriorArea ?? _readyArea;
        ConfigureAreaActor(runtime, runtime.Chart.ToChart(new AreaStepCell(actor.Cell.X, actor.Cell.Y)), actor.Facing);
        _camera!.SetAreaPlan(_readyArea!.Plan, WorldViewport, window: _mapWindowCentre is not null, _mapOverview);
        StopAreaWalls(); // a loaded game starts the wall check afresh (ARC-RENDER-011)
        ResetAreaRoofs(); // and the roofs from the map's stored state (ARC-RENDER-022)
        _mapEntered.Clear(); // tiles the replaced actor stepped into fire nothing (ARC-WORLD-038)
        RebindAreaMapAmbient();
        RebindAreaSound();
        _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        PublishWorldSelection();
        if (_areaTransitions is not null)
            _areaUseStatus = InCave ? "in the cave: click the exit within 2 cells to return" : "cave: click the entrance within 2 cells";
        GD.Print("ARC-WORLD-011 quick-load adopted " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = actor.Map.RuntimeMap, actor.Cell, Facing = actor.Facing.ToString(), Revision = loaded.Version.Revision }));
    }

    /// <summary>The one place a running area session is replaced by another (today: quick load). Everything that
    /// uses the session reads <c>_areaSession</c> when it needs it and must not keep the owner, its clock or its flags
    /// across calls: the previous owner is disposed here. A component that mirrors session state rebinds here.</summary>
    private void ReplaceAreaSession(GameSessionOwner session, IReadOnlyDictionary<int, AreaMapRuntime> maps, int currentMap,
        AreaConversationHost? talk)
    {
        // Every refusal comes first, then the day/night driver is built: if either refuses, nothing has been swapped.
        if (_conversationHost is not null && talk is null) throw new InvalidOperationException("AREA_CONVERSATION_REBIND_REQUIRED");
        RebindAreaAmbient(session);
        var previous = _areaSession;
        _areaSession = session;
        // The dialog host borrows the session (ARC-SCRIPT-006): it is swapped with it, before the old one is disposed.
        if (_conversationHost is not null) _conversationHost = talk!;
        // ARC-WORLD-048: visitor hosts borrow the old session too; they are built again over the new one when asked.
        _visitorHosts.Clear(); _visitorRefused.Clear();
        if (_areaTransitions is not null) _areaTransitions = new(maps.Values, currentMap, session);
        // ARC-SCRIPT-017: the cached use-script host kept the previous owner.
        _useScriptHost = null;
        previous?.Dispose();
    }

    private void SetQuickStatus(string status)
    {
        _quickSaveStatus = status;
        RefreshAreaStatus();
    }

    private string QuickSaveStatus() => !QuickKeysActive ? ""
        : " | F7 auto-save / F8 load the newest save (" + (_readyMapName is null ? QuickSaveDirectory : MapQuickSaveDirectory) + "; save list in the palette)"
            + (_quickSaveStatus is null ? "" : ": " + _quickSaveStatus);

    // Native observation (ARC-WORLD-011), run after the transition smoke with the exterior published: F7 through the
    // production key route saves next to the entrance; the entrance takes the actor into the cave; F8 brings the actor
    // back to the saved cell on the exterior under a new session. F7 under the palette writes nothing; a damaged slot
    // makes F8 fail and keeps the running session and map. Uses a temporary slot, never the player's quick save.
    private async Task RunAreaQuickSaveSmokeAsync()
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-world-011-").FullName;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _saveLibraryForSelfTest = new(directory);
        try
        {
            static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-011 quick save smoke: " + why); }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var (_, trigger) = _areaTransitions!.Triggers().Single();
            var exterior = _areaTransitions.Current;
            Require(exterior.RuntimeMap == 1, "the exterior is published first");
            var savedCell = UseCell(exterior, new(trigger.WorldX!.Value, trigger.WorldY!.Value));
            AreaSmokePlace(exterior, savedCell);
            QuickKey(Key.F7);
            var slot = _saveLibraryForSelfTest.AutoSlot;
            Require(slot.Exists && _quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            // ARC-UI-010: the auto-save carries its details (the original's "Auto-Save" description, the place) for the save list.
            Require(_saveLibraryForSelfTest.List().Entries is [{ IsAuto: true, Details: GameSaveDetails.Present, Summary.Location: "Crash Site" } auto]
                && auto.DisplayDescription == GameSaveLibrary.AutoDescription, "the auto-save has its details");
            var savedWorld = exterior.Chart.ToWorld(savedCell);
            Require(_areaSession!.QueryActor() is { } placed && placed.Map.RuntimeMap == 1 && placed.Cell == new CellCoord((int)savedWorld.X, (int)savedWorld.Y), "the session holds the saved cell");
            var savedBytes = System.IO.File.ReadAllBytes(slot.Path);
            // ARC-UI-004: the items the PC took before the save come back with it (the seeds are attached again).
            var savedItems = _areaSession.QueryPcInventory().ToArray();

            // Under the palette F7 is ignored and the slot is unchanged.
            ToggleWorldSettings();
            Require(WorldModal, "palette opened");
            QuickKey(Key.F7);
            ToggleWorldSettings();
            Require(!WorldModal && _quickSaveStatus?.Contains("ignored", StringComparison.Ordinal) == true, "F7 ignored under the palette: " + _quickSaveStatus);
            Require(System.IO.File.ReadAllBytes(slot.Path).AsSpan().SequenceEqual(savedBytes), "the palette F7 wrote nothing");

            await UseNextToTriggerAsync(79);
            Require(_areaTransitions.CurrentMap == 79 && InCave, "the entrance took the actor into the cave");
            var caveSession = _areaSession;
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            QuickKey(Key.F8);
            Require(_quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            Require(!ReferenceEquals(caveSession, _areaSession) && _areaTransitions.CurrentMap == 1 && !InCave && ReferenceEquals(_readyArea, _exteriorArea),
                "F8 restored the exterior under a new session");
            var grid = (AreaGridSession)_worldActor.Session!;
            Require(grid.State.CommittedCell == savedCell && grid.State.Phase == MovementPhase.Settled, "the actor stands settled on the saved cell");
            Require(_areaSession!.QueryPcInventory().SequenceEqual(savedItems) && (_inventoryHost is null || _areaSession.QueryMapInventory(new(1)) is not null),
                "F8 restored the PC's items and re-attached the inventory seeds");
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }

            // A damaged slot: F8 fails, the running session and map stay.
            System.IO.File.WriteAllText(slot.Path, "{\"format\":\"arcanum-steamwoven-session\",");
            var running = _areaSession; var revision = _areaSession!.Version.Revision;
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            QuickKey(Key.F8);
            Require(_quickSaveStatus?.StartsWith("F8 quick load FAILED: the save file is damaged", StringComparison.Ordinal) == true, "damaged slot reported: " + _quickSaveStatus);
            Require(ReferenceEquals(running, _areaSession) && _areaSession.Version.Revision == revision && _areaTransitions.CurrentMap == 1
                && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == savedCell, "a failed load changed nothing");
            GD.Print("ARC-WORLD-011 native-smoke quicksave PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Saved = new { Map = 1, Cell = savedWorld }, SavedBytes = savedBytes.Length, PcItems = savedItems.Length, PaletteIgnored = true, LoadedFromCave = true,
                DamagedRefused = true, Session = _areaSession.Version.Revision }));
        }
        finally
        {
            if (WorldModal) ToggleWorldSettings();
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    // One key press and release through the production keyboard route.
    private void QuickKey(Key key)
    {
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }
}
