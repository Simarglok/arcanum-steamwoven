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
public partial class TerrainViewerApp
{
    private const string QuickSaveDirectory = "user://saves";
    private GameSessionSaveSlot? _quickSaveSlot;
    // The native smoke writes to its own temporary slot, never to the player's quick save.
    private GameSessionSaveSlot? _quickSaveSlotForSelfTest;
    private readonly QuickKeyGate _quickKeys = new();
    private string? _quickSaveStatus;

    private enum QuickBusy { None, NoActor, Lifecycle, TransferPending }

    private GameSessionSaveSlot QuickSaveSlot => _quickSaveSlotForSelfTest
        ?? (_quickSaveSlot ??= GameSessionSaveSlot.QuickSave(ProjectSettings.GlobalizePath(QuickSaveDirectory)));

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
        if (_areaTransitions?.Pending is not null) return QuickBusy.TransferPending;
        return QuickBusy.None;
    }

    private static string Describe(QuickBusy busy) => busy switch
    {
        QuickBusy.NoActor => "no area actor",
        QuickBusy.Lifecycle => "reset/reload in progress",
        QuickBusy.TransferPending => "map transfer pending",
        _ => "ready",
    };

    private int CurrentAreaMap => _areaTransitions?.CurrentMap ?? _readyArea!.Snapshot.RuntimeMap;

    private void QuickSave()
    {
        var busy = QuickBusyReason();
        if (busy != QuickBusy.None) { SetQuickStatus("F7 quick save refused: " + Describe(busy)); return; }
        var grid = (AreaGridSession)_worldActor.Session!;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing);
        var result = AreaQuickSave.Save(_areaSession!, placement, QuickSaveSlot);
        GD.Print("ARC-WORLD-011 quick-save " + System.Text.Json.JsonSerializer.Serialize(new {
            result.Saved, Failure = result.Write?.Failure.ToString(), result.Write?.Detail, result.Write?.Bytes, Place = result.PlaceFailure.ToString(),
            Map = placement.Map.RuntimeMap, placement.Cell, Revision = _areaSession!.Version.Revision }));
        SetQuickStatus("F7 " + result.Status);
    }

    private void QuickLoad()
    {
        // A load supersedes a queued transfer; it waits only for a reset/reload.
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle) { SetQuickStatus("F8 quick load refused: " + Describe(busy)); return; }
        var maps = AreaRuntimes();
        var result = AreaQuickSave.Load(QuickSaveSlot, _areaSession!, placement => PlacementRefusal(maps, placement));
        GD.Print("ARC-WORLD-011 quick-load " + System.Text.Json.JsonSerializer.Serialize(new {
            Loaded = result.Owner is not null, Read = result.Read?.Failure.ToString(), Decode = result.Read?.DecodeFailure.ToString(),
            result.Read?.DecodePath, Restore = result.RestoreFailure.ToString() }));
        if (result.Owner is null) { SetQuickStatus("F8 " + result.Status); return; }
        // ARC-SCRIPT-006: the dialog host reads its state from the session, so it moves to the loaded one; a session
        // whose dialog values it cannot read is refused here, before anything changes.
        AreaConversationHost? talk = null;
        try { talk = _conversationHost?.Rebind(result.Owner); }
        catch (ArgumentException)
        {
            result.Owner.Dispose();
            SetQuickStatus("F8 quick load REFUSED: the save's dialog state is not readable");
            return;
        }
        AdoptLoadedSession(result.Owner, maps, talk);
        SetQuickStatus("F8 " + result.Status);
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
        if (chart.X is < AreaGridSession.MinimumChartCell or > AreaGridSession.MaximumChartCell ||
            chart.Y is < AreaGridSession.MinimumChartCell or > AreaGridSession.MaximumChartCell) return "the cell is outside the map chart";
        return null;
    }

    private void AdoptLoadedSession(GameSessionOwner loaded, IReadOnlyDictionary<int, AreaMapRuntime> maps, AreaConversationHost? talk)
    {
        StopWorldRoute("QUICK_LOAD");
        CancelAreaUseApproach("quick load"); CancelTalkApproach("quick load");
        if (_areaTransitions?.Cancel() is { } dropped) GD.Print("ARC-WORLD-011 quick load dropped the queued transfer to=" + dropped.ToMap);
        var actor = loaded.QueryActor();
        ReplaceAreaSession(loaded, maps, actor.Map.RuntimeMap, talk);
        var runtime = maps[actor.Map.RuntimeMap];
        _readyArea = _caveLoad is { } cave && runtime.RuntimeMap == cave.Area.Snapshot.RuntimeMap ? cave.Area : _exteriorArea ?? _readyArea;
        ConfigureAreaActor(runtime, runtime.Chart.ToChart(new AreaStepCell(actor.Cell.X, actor.Cell.Y)), actor.Facing);
        _camera!.SetAreaPlan(_readyArea!.Plan, WorldViewport);
        PushAreaAmbient(); FollowAreaAmbient();
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
        // The day/night driver is built first: if it refused the session, nothing has been swapped.
        RebindAreaAmbient(session);
        var previous = _areaSession;
        _areaSession = session;
        // The dialog host borrows the session (ARC-SCRIPT-006): it is swapped with it, before the old one is disposed.
        if (_conversationHost is not null) _conversationHost = talk ?? throw new InvalidOperationException("AREA_CONVERSATION_REBIND_REQUIRED");
        if (_areaTransitions is not null) _areaTransitions = new(maps.Values, currentMap, session);
        previous?.Dispose();
    }

    private void SetQuickStatus(string status)
    {
        _quickSaveStatus = status;
        RefreshAreaStatus();
    }

    private string QuickSaveStatus() => !QuickKeysActive ? ""
        : " | F7 quick save / F8 quick load (one slot " + QuickSaveDirectory + "/" + GameSessionSaveSlot.QuickSaveFileName + ")"
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
        _quickSaveSlotForSelfTest = GameSessionSaveSlot.QuickSave(directory);
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
            var slot = _quickSaveSlotForSelfTest;
            Require(slot.Exists && _quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            var savedWorld = exterior.Chart.ToWorld(savedCell);
            Require(_areaSession!.QueryActor() is { } placed && placed.Map.RuntimeMap == 1 && placed.Cell == new CellCoord((int)savedWorld.X, (int)savedWorld.Y), "the session holds the saved cell");
            var savedBytes = System.IO.File.ReadAllBytes(slot.Path);

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
                Saved = new { Map = 1, Cell = savedWorld }, SavedBytes = savedBytes.Length, PaletteIgnored = true, LoadedFromCave = true,
                DamagedRefused = true, Session = _areaSession.Version.Revision }));
        }
        finally
        {
            if (WorldModal) ToggleWorldSettings();
            _quickSaveSlotForSelfTest = null; _quickKeys.Reset();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; _worldTestTime = testTime;
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
