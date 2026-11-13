using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
// saved cell and that map's inventory seed can only be checked once it is loaded). ARC-WORLD-120: a save on the shown map whose
// cell lies beyond the shown window of a map too large to load whole (the main map, ARC-RENDER-024) loads the same way, the
// window opening around the saved cell; it was refused as not admitted before (bug report triage D1).
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

    // ARC-DEV-407: the folder follows the launch map, so the menu's Load Game before the start map has loaded reads the map saves.
    private GameSaveLibrary SaveLibrary => _saveLibraryForSelfTest ?? (AreaSaveFolder.UsesMapFolder(_readyMapName, _launchArguments?.AreaMapName)
        ? _mapSaveLibrary ??= new(SaveDirectory(MapQuickSaveDirectory))
        : _saveLibrary ??= new(SaveDirectory(QuickSaveDirectory)));

    private GameSessionSaveSlot QuickSaveSlot => SaveLibrary.AutoSlot;

    // ARC-DEV-032: an automated run keeps its quick saves in its isolated folder, never in the player's saves.
    private static string SaveDirectory(string user) => ViewerAutomatedStorage.Automated
        ? System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), ViewerStoragePaths.RelativeUserPath(user)) : ProjectSettings.GlobalizePath(user);

    // Map mode (ARC-RENDER-017) never touches the player's Crash Site quick save slot: it has its own (ARC-WORLD-038).
    private bool QuickKeysActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

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

    /// <summary>
    /// ARC-DEV-323 (SL12): F7 with the steal window open. The original's F7 branch pops the interface stack twice before it saves (0x401834 to 0x40183B): the first
    /// pop leaves the steal mode, whose leave case closes the window (0x573440), the second is a pop at the base. The close ends no goal, so the save holds a theft that
    /// walks (<see cref="EndInventory"/>); the window itself is not saved (the interface is not), and the loaded game shows none. The viewer's busy refusal comes first
    /// and leaves the window open; a refusal inside the save itself comes after the close, as the original closes before it saves. The key acts on its release as everywhere (<see cref="QuickKeyGate"/>); the window is no text focus, as the original's F7 has no such guard.
    /// </summary>
    private void RouteQuickSaveOverWindow(WorldKeyDecision decision)
    {
        if (!QuickKeysActive) return;
        var action = _quickKeys.Route(WorldKey.F7, decision.Pressed, decision.Echo, _worldFocused);
        if (action == QuickKeyAction.Ignored) { SetQuickStatus("F7 quick save ignored: the window is not focused"); return; }
        if (action != QuickKeyAction.Save) return;
        var busy = QuickBusyReason();
        if (busy != QuickBusy.None) { SetQuickStatus("F7 save refused: " + Describe(busy)); return; }
        EndInventory();
        QuickSave();
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
    private void QuickSave()
    {
        RecordEvidence(EvidenceCommand.QuickSave); // ARC-GPAD-1376
        SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, CaptureSaveThumbnail(), "F7 ", AreaSaveMessageRoute.QuickKey);
    }

    /// <summary>Saves the session into the save list: <paramref name="name"/> is the save to write, or null for a new numbered save.
    /// The HUD and the log say what happened; the written save's name, or null when nothing was saved.</summary>
    /// <param name="messages">ARC-DEV-168: the original's messages in the message window (F7's notes, the save window's result);
    /// none for a save that is not a player's (a smoke, a test). A refusal of the viewer's own posts none: the original has none.</param>
    private string? SaveGame(string? name, string description, byte[]? thumbnail, string prefix, AreaSaveMessageRoute messages = AreaSaveMessageRoute.None)
    {
        var busy = QuickBusyReason();
        if (busy != QuickBusy.None) { SetQuickStatus(prefix + "save refused: " + Describe(busy)); return null; }
        // ARC-DEV-321: an attempt goal 86 or 67 asked for in the last frame waits for the next frame's safe point, after its run popped; it is made now, so the save holds its outcome instead of losing it.
        ProcessPickPocketPending(); ProcessLockpickPending(); ProcessItemUsePending();
        // The attempt may run a caught-thief script that starts a transfer: the busy check again.
        if ((busy = QuickBusyReason()) != QuickBusy.None) { SetQuickStatus(prefix + "save refused: " + Describe(busy)); return null; }
        // ARC-UI-1542: a light the session's changes of this frame (an input handler, the attempt just made) cause is recorded before the capture, or a load would take the new stamps as its baseline and lose it.
        SyncBarLights();
        // ARC-DEV-318 (MIG14 S9c): the hero's casting goal and the casts queued beneath it are saved with their runs (the gate ARC-MAGIC-015 set is gone).
        var grid = (AreaGridSession)_worldActor.Session!;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing);
        // ARC-WORLD-016: the companions' cells are session state too; a refused placement refuses the save.
        if (!RecordAreaCompanions("save")) { SetQuickStatus(prefix + "save refused: " + _companionNotice); return null; }
        // ARC-DEV-168 (B34): F7 says it is saving before the save is made (0x5496E0, message 5060).
        if (messages == AreaSaveMessageRoute.QuickKey) PostMessage(AreaSaveMessages.Note(SaveMessageTexts(), AreaSaveMessages.Saving), "save note");
        var result = AreaQuickSave.Save(_areaSession!, placement, SaveLibrary, name, session => AreaSaveList.Summary(session, description, SaveLocation()), thumbnail);
        GD.Print("ARC-WORLD-011 quick-save " + System.Text.Json.JsonSerializer.Serialize(new {
            result.Saved, Name = result.Library?.Name, Failure = result.Library?.Failure.ToString(), Snapshot = result.Write?.Failure.ToString(), result.Write?.Detail,
            result.Write?.Bytes, result.Library?.Warning, Place = result.PlaceFailure.ToString(), Map = placement.Map.RuntimeMap, placement.Cell,
            Thumbnail = thumbnail?.Length ?? 0, Revision = _areaSession!.Version.Revision }));
        SetQuickStatus(prefix + result.Status);
        // ARC-DEV-168 (B34): F7 says it completed (5062) and the save window says it saved or failed (5002, 5003).
        if (messages == AreaSaveMessageRoute.QuickKey) PostMessage(AreaSaveMessages.QuickSaveAfter(SaveMessageTexts(), result.Saved), "save result");
        else if (messages == AreaSaveMessageRoute.Window) PostMessage(AreaSaveMessages.SaveWindowResult(SaveMessageTexts(), result.Saved), "save result");
        return result.Saved ? result.Library!.Name : null;
    }

    /// <summary>ARC-DEV-168: the texts of the save and load messages, read from the game's <c>MainMenu.mes</c> once a run.</summary>
    private AreaMainMenuTexts SaveMessageTexts() => MainMenuTexts(_arguments ?? _launchArguments!);

    /// <summary>ARC-DEV-168 (B35): a load's messages (<see cref="AreaSaveMessages"/>): its result, then for F8 the completion note.
    /// A load that read hands the message window to the loaded session first, so the replaced session's entries go and the result stays.</summary>
    private void PostLoadMessages(bool loaded, AreaLoadMessageRoute route)
    {
        if (route == AreaLoadMessageRoute.None) return;
        var texts = SaveMessageTexts();
        if (loaded) SyncLevelMessageSession();
        if (route != AreaLoadMessageRoute.NotesOnly) PostMessage(AreaSaveMessages.LoadResult(texts, loaded), "load result");
        if (route is AreaLoadMessageRoute.QuickKey or AreaLoadMessageRoute.NotesOnly) PostMessage(AreaSaveMessages.Note(texts, AreaSaveMessages.LoadCompleted), "load note");
    }

    /// <summary>The location a save names: the map mode's map, else the Crash Site or its cave.</summary>
    // ARC-DEV-050: a save made in debug mode says so in its location.
    private string SaveLocation() => ViewerDebugMode.MarkLocation(_readyMapName ?? (InCave ? "Crash Site cave" : "Crash Site"), DebugMode);

    /// <summary>A PNG of the frame on screen fitted into the original's fixed 468×300 preview (SL8), or null when the frame cannot be
    /// read (a headless run).</summary>
    private byte[]? CaptureSaveThumbnail()
    {
        if (DisplayServer.GetName() == "headless") return null;
        var image = GetViewport().GetTexture()?.GetImage();
        return image is null || image.IsEmpty() ? null : SaveThumbnail(image);
    }

    /// <summary>The preview PNG of a frame (ARC-DEV-051 takes it from the frame it keeps; the image is resized in place), or null when
    /// it breaks the save list's limit.</summary>
    private byte[]? SaveThumbnail(Image image)
    {
        var scale = Math.Min(468.0 / image.GetWidth(), 300.0 / image.GetHeight());
        image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Bilinear);
        var png = image.SavePngToBuffer();
        return png is { Length: > 0 } && png.Length <= SaveLibrary.Limits.MaxThumbnailBytes ? png : null;
    }

    /// <summary>F8: the newest save of the list (the original's latest-save helper), the auto slot only when it is the newest.</summary>
    // ARC-DEV-168: with no save listed the original's quick load returns before the load function, so F8 posts its two notes and no result.
    private void QuickLoad()
    {
        RecordEvidence(EvidenceCommand.QuickLoad); // ARC-GPAD-1376
        LoadGame(SaveLibrary.Latest() is { } latest ? SaveLibrary.Slot(latest.Name) : QuickSaveSlot, "F8 ",
            messages: SaveLibrary.Latest() is null ? AreaLoadMessageRoute.NotesOnly : AreaLoadMessageRoute.QuickKey);
    }

    /// <summary>Loads one save of the list. True when the loaded session was adopted or its map load started; on false the running
    /// session and scene are unchanged and the HUD says why.</summary>
    /// <param name="heroTables">ARC-UI-011: a load from the main menu, whose placeholder session has no hero: the game-wide hero
    /// tables a hero save takes (<see cref="AreaQuickSave.Load"/>), and the loaded hero's own tables are adopted with it.</param>
    /// <param name="messages">ARC-DEV-168: the original's messages in the message window (F8's notes around the result, or the result alone
    /// for the load window and the main menu's last save); none for a load that is not a player's (a smoke, the death reload).</param>
    private bool LoadGame(GameSessionSaveSlot slot, string prefix, AreaHeroLoad? heroTables = null, AreaLoadMessageRoute messages = AreaLoadMessageRoute.None)
    {
        using var span = PerfProbe.Span("game.load");
        // A load supersedes a queued transfer; it waits only for a reset/reload.
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle or QuickBusy.MapLoading) { SetQuickStatus(prefix + "quick load refused: " + Describe(busy)); return false; }
        // ARC-DEV-168 (B35): F8 says it is loading before it reads (0x549770, message 5061).
        if (messages is AreaLoadMessageRoute.QuickKey or AreaLoadMessageRoute.NotesOnly) PostMessage(AreaSaveMessages.Note(SaveMessageTexts(), AreaSaveMessages.Loading), "load note");
        var maps = AreaRuntimes();
        // ARC-WORLD-038: in map mode a save on another map is loaded with that map, where its cell is placed. ARC-WORLD-120: so is
        // a save on the shown map beyond its window (a map too large to load whole), the window then opening around the saved cell.
        AreaMapDestination? elsewhere = null;
        string? Refusal(ActorPlacement placement)
        {
            if (_readyMapName is null || (maps.TryGetValue(placement.Map.RuntimeMap, out var shown) && (_mapWindowCentre is null
                || AreaQuickSave.InShownSectors(shown.Snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).Select(s => s.Coordinate), placement.Cell))))
                return PlacementRefusal(maps, placement);
            if (_mapTransfers?.Destinations.TryGetValue(placement.Map.RuntimeMap, out var destination) != true) return "map mode cannot open that map";
            elsewhere = destination; return null;
        }
        var result = AreaQuickSave.Load(slot, _areaSession!, Refusal, MapSoundDefault, heroTables);
        GD.Print("ARC-WORLD-011 quick-load " + System.Text.Json.JsonSerializer.Serialize(new {
            Slot = System.IO.Path.GetFileName(slot.Path), Loaded = result.Owner is not null, Read = result.Read?.Failure.ToString(), Decode = result.Read?.DecodeFailure.ToString(),
            result.Read?.DecodePath, Restore = result.RestoreFailure.ToString() }));
        if (result.Owner is null) { SetQuickStatus(prefix + result.Status); PostLoadMessages(false, messages); return false; }
        // ARC-UI-004: the loaded maps' inventory seeds are attached to the restored session before anything changes; a
        // save whose taken items disagree with them is refused (the item would be lost or duplicated).
        if (_inventoryHost?.Attach(result.Owner) is { } attached && attached != GameSessionFailure.None)
        {
            result.Owner.Dispose();
            SetQuickStatus($"{prefix}quick load REFUSED: the save's inventory does not match the loaded maps ({attached})");
            PostLoadMessages(false, messages);
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
            PostLoadMessages(false, messages);
            return false;
        }
        // The shown map's seeds and dialog state are checked above; the other map binds its own when it is published.
        var from = " from " + System.IO.Path.GetFileNameWithoutExtension(slot.Path);
        if (heroTables is not null) AdoptHeroTables(LoadedMenuHero(result.Owner));
        // ARC-UI-036: a new character's save loaded in play brings its own kit (the opening shop's, by background and gender): its tables
        // are rebuilt from the loaded session, not kept from the running one.
        else if (result.Owner.QueryHero()?.Prototype == AreaStartingHero.GenericPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset)
            AdoptHeroTables(LoadedMenuHero(result.Owner));
        // The other map's load is asynchronous: its messages wait for the map (StartMapTransfers, the failure in the transfer's fallback).
        if (elsewhere is { } map) { QuickLoadOtherMap(result.Owner, map, prefix + result.Status + from, messages); return true; }
        AdoptLoadedSession(result.Owner, maps, talk);
        SetQuickStatus(prefix + result.Status + from);
        PostLoadMessages(true, messages);
        return true;
    }

    /// <summary>ARC-WORLD-038: the loaded session goes with a load of its map (a pending transfer is dropped, the actor's host
    /// state is cleared, and the scene is cleared as the load starts); the running session is kept until the map is published
    /// with the loaded one, and comes back with the shown map when it is not.</summary>
    private void QuickLoadOtherMap(GameSessionOwner loaded, AreaMapDestination map, string status, AreaLoadMessageRoute messages = AreaLoadMessageRoute.None)
    {
        StopWorldRoute("QUICK_LOAD");
        CancelAreaUseApproach("quick load"); CancelTalkApproach("quick load"); CancelInventoryApproach("quick load"); CancelAreaAttack("quick load");
        ClearAreaProjectiles("quick load");
        if (_mapTransfers?.Clear() is { } dropped) GD.Print("ARC-WORLD-038 quick load dropped the queued transfer to=" + dropped.To.Name);
        var grid = (AreaGridSession)_worldActor.Session!;
        var actor = loaded.QueryActor();
        var origin = _arguments!;
        _mapHandoff = new(loaded, _readyArea!.Snapshot.RuntimeMap, map.Name, actor.Facing, origin, grid.Chart.ToWorld(grid.State.CommittedCell), map.Name,
            Previous: _areaSession, Kind: MapHandoffKind.QuickLoad, OriginFacing: grid.State.Facing, LoadMessages: messages);
        var next = origin with { AreaMapName = map.Name, AreaMapArrival = new(actor.Cell.X, actor.Cell.Y), AreaMapSector = null };
        GD.Print("ARC-WORLD-038 quick-load map " + System.Text.Json.JsonSerializer.Serialize(new { From = _readyArea.Snapshot.RuntimeMap, To = map, actor.Cell }));
        SetQuickStatus($"{status}; loading '{map.Name}'");
        // ARC-UI-1362: a load that opens another map shows the load screen (black with the clock) until that map is there, as a change of place does (PS29, LS4).
        BeginTransitionLoad();
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

    /// <param name="reason">What brings the session, for the log (ARC-UI-011: a new game adopts its session the same way).</param>
    private void AdoptLoadedSession(GameSessionOwner loaded, IReadOnlyDictionary<int, AreaMapRuntime> maps, AreaConversationHost? talk, string reason = "quick load")
    {
        StopWorldRoute("QUICK_LOAD");
        CancelAreaUseApproach(reason); CancelTalkApproach(reason); CancelInventoryApproach(reason); CancelAreaAttack(reason);
        // ARC-COMBAT-016: the flights of the replaced session are given up.
        ClearAreaProjectiles(reason);
        if (_areaTransitions?.Cancel() is { } dropped) GD.Print($"ARC-WORLD-011 {reason} dropped the queued transfer to=" + dropped.ToMap);
        if (_mapTransfers?.Clear() is { } queued) GD.Print($"ARC-WORLD-038 {reason} dropped the queued transfer to=" + queued.To.Name);
        var actor = loaded.QueryActor();
        ReplaceAreaSession(loaded, maps, actor.Map.RuntimeMap, talk);
        var runtime = maps[actor.Map.RuntimeMap];
        _readyArea = _caveLoad is { } cave && runtime.RuntimeMap == cave.Area.Snapshot.RuntimeMap ? cave.Area : _exteriorArea ?? _readyArea;
        ConfigureAreaActor(runtime, runtime.Chart.ToChart(new AreaStepCell(actor.Cell.X, actor.Cell.Y)), actor.Facing);
        // ARC-DEV-351: a game loaded on this map opens it again (a quick load to another map goes through StartMapTransfers): marks the save holds here are taken.
        if (_mapTransfers is not null) TakeTeleportedMarks(loaded, actor.Map);
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
        GD.Print((reason == "quick load" ? "ARC-WORLD-011 quick-load" : "ARC-UI-011 " + reason) + " adopted " + System.Text.Json.JsonSerializer.Serialize(new {
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
        // ARC-WORLD-092: the AI host belongs to the replaced session; the next frame builds the new one's.
        _aiHost = null; _aiHostStamp = null; _aiPcOccupancy = null;
        var previous = _areaSession;
        _areaSession = session;
        // ARC-DEV-354: the carried items this host has no row for (taken in a window of the main map it never showed) are read once the session is the
        // running one (the record source's callbacks read it) and before the item art sizes, which read those rows.
        _inventoryHost?.ReadCarriedRows(session, retryUnread: true);
        AttachAreaEncumbranceEffect();
        AttachAreaDyingScript(); AttachAreaHeroGround();
        AttachAreaItemArtSizes();
        // ARC-DEV-099: the attachment is not saved; the restored session gets the prototypes' script arrays again.
        AttachAreaPrototypeScripts(session);
        SyncTurnBasedSetting(session);
        ApplySessionSettings(session);
        // The dialog host borrows the session (ARC-SCRIPT-006): it is swapped with it, before the old one is disposed.
        if (_conversationHost is not null) _conversationHost = talk!;
        // ARC-DEV-081: a walk-to-talk names a speaker of the replaced host (its index is that host's).
        _talkOpenPending = null; _conversationEndPending = null;
        if (_areaTransitions is not null) _areaTransitions = new(maps.Values, currentMap, session);
        // ARC-SCRIPT-017: the cached use-script host kept the previous owner.
        _useScriptHost = null;
        // ARC-COMBAT-016: the replaced session's flights end while it still lives.
        ClearAreaProjectiles("session replaced");
        previous?.Dispose();
    }

    private void SetQuickStatus(string status)
    {
        _quickSaveStatus = status;
        RefreshAreaStatus();
    }

    private string QuickSaveStatus() => !QuickKeysActive ? ""
        : " | F7 auto-save / F8 load the newest save (" + (AreaSaveFolder.UsesMapFolder(_readyMapName, _launchArguments?.AreaMapName) ? MapQuickSaveDirectory : QuickSaveDirectory) + "; save list in the palette)"
            + (_quickSaveStatus is null ? "" : ": " + _quickSaveStatus);

    // Native observation (ARC-WORLD-011), run after the transition smoke with the exterior published: F7 through the
    // production key route saves next to the entrance; the entrance takes the actor into the cave; F8 brings the actor
    // back to the saved cell on the exterior under a new session. F7 under the palette writes nothing; a damaged slot
    // makes F8 fail and keeps the running session and map. Uses a temporary slot, never the player's quick save.
    private async Task RunAreaQuickSaveSmokeAsync()
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-world-011-").FullName;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var messageShift = _messageClockShiftForSelfTest;
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
            // ARC-DEV-168 (B34): the message window starts empty and quiet, so F7's notes can be drawn.
            var messages = SaveMessageTexts();
            ClearLevelMessages(); _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
            // ARC-UI-1542 (ledger GB13): the top strip's lit buttons and the Map button's face are part of the save. A quest accepted lights the Logbook
            // button (the production route: the stamp of the session's logbook), the Map face is turned to the other one, then F7 saves.
            var lightQuest = Enumerable.Range(1000, 40).First(number => _areaSession!.QueryQuest(number) is null);
            var faceBefore = _gameBar.Lights.MapMode; var litBefore = _gameBar.Lights.LitMask;
            var savedFace = faceBefore == AreaLightSlot.TownMap ? AreaLightSlot.WorldMap : AreaLightSlot.TownMap;
            _gameBar.Lights.SetMapMode(savedFace);
            var lightSession = _areaSession!; RecordBarLights(lightSession);
            Require(lightSession.SetQuestState(lightSession.Version, lightQuest, SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, "a quest was accepted");
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Earlier steps of the smoke may have lit other buttons: the save holds whatever is lit, with the Logbook added.
            var savedMask = _gameBar.Lights.LitMask;
            Require(_gameBar.Lights.IsLit(AreaLightSlot.Logbook) && _gameBar.Lights.MapMode == savedFace && savedMask == (litBefore | 1 << (int)AreaLightSlot.Logbook),
                $"the quest lit the Logbook button: before {litBefore}, now {savedMask}");
            Require(_areaSession!.QueryBarLights() == new SessionBarLightsState(savedMask, (int)savedFace), "the session holds the lit state for the save: " + _areaSession.QueryBarLights());
            QuickKey(Key.F7);
            var slot = _saveLibraryForSelfTest.AutoSlot;
            Require(slot.Exists && _quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            Require(_messageLog.Count == 0 && _messageLog.Shown is { Kind: AreaMessageKind.Hover } saved && saved.Title == messages.Text(AreaSaveMessages.SaveCompleted),
                "F7 ends with the completion note, which is not stored: " + _messageLog.Shown?.Title);
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
            // ARC-DEV-168 (B35): the load's result is the window's one stored entry (the replaced session's went with it); the completion note
            // that follows it is held back by the 3 s rule.
            Require(_messageLog.Count == 1 && _messageLog.Shown is { Kind: AreaMessageKind.Message } loaded && loaded.Title == messages.Text(AreaSaveMessages.LoadDone),
                "F8 says the game loaded: " + _messageLog.Shown?.Title);
            var grid = (AreaGridSession)_worldActor.Session!;
            Require(grid.State.CommittedCell == savedCell && grid.State.Phase == MovementPhase.Settled, "the actor stands settled on the saved cell");
            Require(_areaSession!.QueryPcInventory().SequenceEqual(savedItems) && (_inventoryHost is null || _areaSession.QueryMapInventory(new(1)) is not null),
                "F8 restored the PC's items and re-attached the inventory seeds");
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }

            // ARC-UI-1542: the loaded session shows the Logbook button lit again and the saved Map face; the sector's answer holds no sway until it changes.
            Require(!ReferenceEquals(caveSession, _areaSession) && _areaSession!.QueryBarLights() is { } restoredLights && restoredLights.LitMask == savedMask && restoredLights.MapMode == (int)savedFace,
                "the loaded session holds the saved lights: " + _areaSession.QueryBarLights());
            Require(_gameBar.Lights.LitMask == savedMask && _gameBar.Lights.ArtOf(AreaBarButton.Logbook) == 560 && _gameBar.Lights.MapMode == savedFace,
                $"F8 brought back the lit buttons {savedMask} (the Logbook among them) and the Map face {savedFace}: lit {_gameBar.Lights.LitMask}, face {_gameBar.Lights.MapMode}");
            _gameBar.Lights.Light(AreaLightSlot.Logbook, false, true); _gameBar.Lights.SetMapMode(faceBefore); RecordBarLights(_areaSession!); _gameBarPanel?.Render();

            // A damaged slot: F8 fails, the running session and map stay.
            System.IO.File.WriteAllText(slot.Path, "{\"format\":\"arcanum-steamwoven-session\",");
            var running = _areaSession; var revision = _areaSession!.Version.Revision;
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            QuickKey(Key.F8);
            Require(_quickSaveStatus?.StartsWith("F8 quick load FAILED: the save file is damaged", StringComparison.Ordinal) == true, "damaged slot reported: " + _quickSaveStatus);
            Require(_messageLog.Count == 2 && _messageLog.Shown is { Kind: AreaMessageKind.Message } failedLoad && failedLoad.Title == messages.Text(AreaSaveMessages.LoadFailed),
                "F8 says the load failed: " + _messageLog.Shown?.Title);
            Require(ReferenceEquals(running, _areaSession) && _areaSession.Version.Revision == revision && _areaTransitions.CurrentMap == 1
                && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == savedCell, "a failed load changed nothing");
            // ARC-DEV-168: with no save listed F8 stores no result (the original returns before its load function; only the notes are posted).
            System.IO.File.Delete(slot.Path);
            var lastEntry = _messageLog.Shown;
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            QuickKey(Key.F8);
            Require(_messageLog.Count == 2 && ReferenceEquals(_messageLog.Shown, lastEntry), "F8 with no save stored no result: " + _messageLog.Shown?.Title + " " + _messageLog.Count);
            GD.Print("ARC-WORLD-011 native-smoke quicksave PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Saved = new { Map = 1, Cell = savedWorld }, SavedBytes = savedBytes.Length, PcItems = savedItems.Length, PaletteIgnored = true, LoadedFromCave = true,
                DamagedRefused = true, Messages = new { Saved = true, Loaded = true, Failed = true }, Session = _areaSession.Version.Revision }));
        }
        finally
        {
            if (WorldModal) ToggleWorldSettings();
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            ClearLevelMessages(); _messageClockShiftForSelfTest = messageShift;
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
