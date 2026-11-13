using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-138 (MIG13 S7, smoke plan SM10 `door-close`, bug B15) native check, part of the map battery and alone with --stages door-close (a hero, a map with a door the player can open): a use opens
// an unlocked door that keeps no portal flag 0x100 as the door's own run (goal 70); the run writes its frames at the ART's pace and turns into the pending close 72; the door is still open one second
// later; the Esc menu stops the run (five seconds behind it change nothing and the next frame credits one frame, not the time behind it); after the menu the door closes by itself, no sooner than
// the original's two waits of 1.5 s after its last open frame and (pause excluded) no later than those plus the closing steps. The frame clock is driven with literal host stamps, as the AI smokes do.
// ARC-WORLD-1668 (ledger TT18, 0x428A10) last part: the closed door is made trapped (the native trap script 30001 with counters 0, so it hurts nobody, written as the session's map changes over its use slot): the hero's use meets the trap on the use node, which ends the run with the door shut
// (the trap spotted or gone off); with the trap taken away the next use opens it. NONE for this part when the map has no trap host or cast host.
// ARC-WORLD-1805 (ledger TT20) second extra part: on a map with one of the installed doors whose use script teleports the hero (2591 on K'na Tha: `./arcanum smoke map "k'na tha" --stages door-close --hero human-male`), the hero's use runs the
// door's script on the use node, the teleport is queued as the map transfer (the pending transfer's arrival is the script's) and the door opens as the script's "run default" says; the pending transfer is then cleared (the move itself is the transfer
// stage's). NONE for this part when the map has no such door.
public partial class TerrainViewerApp
{
    private void RunAreaDoorCloseSmoke()
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session || _areaDoors is not { } doors || _readyArea is null || _frameTap is not { } tap)
        { GD.Print($"ARC-DEV-138 native-smoke door-close NONE map={_readyMapName} (no session portals or frame clock)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-138 door-close smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DOOR_CLOSE_SMOKE_FAILED");
        }
        // (ARC-DEV-176: a door with a run of its own - a closing a former stage left - is closed again by a use, as the original's goal 49 does, so the smoke takes a door at rest.)
        var door = portals.All.Where(p => p.Frames.Count > 1 && p.OpenFrame != 0 && !p.UnlockScript && portals.Frame(session, p) == 0 && doors.GoalOf(p) is null && !IsTeleportingDoor(p))
            .Where(p => (session.QueryOpenable(portals.Openable(p), p.Baseline).Flags & LockRules.StaysOpenBit) == 0)
            .OrderBy(p => p.Entity, StringComparer.Ordinal).FirstOrDefault(p => portals.Decide(session, p) == AreaPortalUse.Opened);
        if (door is null) { GD.Print($"ARC-DEV-138 native-smoke door-close NONE map={_readyMapName} (no closed door the player can open)"); return; }
        var focused = _worldFocused; var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        // ARC-DEV-176: the use is the hero's goal 38 (it walks to a door out of range), so the hero stands next to the door first, as a player who clicked it from there.
        if (_worldActor.Session is AreaGridSession grid && _readyArea is { } area)
        {
            var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
            var next = ApproachCell(grid, door.Cell.X, door.Cell.Y, 1);
            if (next is null) { GD.Print($"ARC-DEV-138 native-smoke door-close NONE map={_readyMapName} (no cell next to the door)"); return; }
            AreaSmokePlace(map, next.Value);
        }
        PortalSmokeClockStart();
        try
        {
            // A use opens it: the door's own run, frame by frame to the last open frame.
            UsePortal(door);
            // ARC-DEV-176: the use is the hero's goal 38: its first step (5 ms) turns it into the door goals, which start the door's run.
            PortalSmokeUntil(() => doors.GoalOf(door) is not null, 500);
            Require(doors.GoalOf(door) == Arcanum.Simulation.Goals.GoalType.AnimateDoorOpen, $"the use started the opening run: {doors.GoalOf(door)} ({_portalNotice})");
            var seen = new SortedSet<int>();
            Require(PortalSmokeUntil(() => { seen.Add(portals.Frame(session, door)); return portals.Frame(session, door) == door.OpenFrame; }, 10 * door.StepMs + 500),
                $"the door opened to frame {door.OpenFrame}: saw {string.Join(",", seen)}");
            var openedAt = _portalSmokeHostMs;
            Require(seen.Count >= 2, $"the door showed its frames one by one: {string.Join(",", seen)}");

            // Still open a second later, waiting for the close.
            PortalSmokeUntil(() => false, 1000);
            Require(portals.Frame(session, door) == door.OpenFrame && doors.GoalOf(door) == Arcanum.Simulation.Goals.GoalType.PendClosingDoor,
                $"one second after opening the door waits to close: frame {portals.Frame(session, door)} goal {doors.GoalOf(door)}");

            // The Esc menu pauses the run: five seconds behind it change nothing.
            OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!), AreaMenuWindow.InPlay);
            Require(MainMenuOpen && WorldMenuPause, "the Esc menu is open and is the pause source");
            TickAreaClock(_portalSmokeHostMs += 50);
            var pausedFrom = _portalSmokeHostMs;
            PortalSmokeUntil(() => false, 5000);
            var pausedMs = _portalSmokeHostMs - pausedFrom;
            Require(tap.Flags.Paused && portals.Frame(session, door) == door.OpenFrame && doors.GoalOf(door) == Arcanum.Simulation.Goals.GoalType.PendClosingDoor,
                $"the door stands behind the Esc menu: paused {tap.Flags.Paused}, frame {portals.Frame(session, door)}, goal {doors.GoalOf(door)}");
            EndMainMenu("door-close smoke");

            // After the menu it closes by itself: no sooner than two waits of 1.5 s after its last open frame, no later than those plus the closing steps (the pause excluded).
            var closed = PortalSmokeUntil(() => portals.Frame(session, door) == 0, 6000 + 8 * door.StepMs);
            var elapsed = _portalSmokeHostMs - openedAt - pausedMs;
            Require(closed, $"the door closed by itself: frame {portals.Frame(session, door)} goal {doors.GoalOf(door)}");
            Require(elapsed >= 2 * AiExecutorRules.DoorCloseWaitMs && elapsed <= 2 * AiExecutorRules.DoorCloseWaitMs + 8 * door.StepMs + 200,
                $"it closed at the original's interval: {elapsed} ms after its last open frame (pause excluded)");
            Require(doors.GoalOf(door) is null or Arcanum.Simulation.Goals.GoalType.AnimateDoorClosed, $"the closing run ended or is on its last wait: {doors.GoalOf(door)}");
            var trapped = RunDoorTrapPart(door, session, portals, doors);
            var teleporting = RunDoorTeleportPart(session, portals, doors);
            GD.Print("ARC-DEV-138 native-smoke door-close PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, door.Entity, door.OpenFrame, door.StepMs, Frames = seen.Count, ClosedAfterMs = elapsed, PausedMs = pausedMs, TrappedDoor = trapped, TeleportingDoor = teleporting, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            PortalSmokeClockEnd();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }

    /// <summary>ARC-WORLD-1668: the trapped-door part of the door-close smoke; returns what it found ("met" with the meeting's result, or the reason it did not run).</summary>
    private string RunDoorTrapPart(AreaPortal door, GameSessionOwner session, AreaPortals portals, AreaDoorHost doors)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-1668 door-close trapped-door smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DOOR_CLOSE_SMOKE_FAILED");
        }
        if (_readyArea is not { } area || AreaTrapsNow() is not { } traps || SpellCasterNow() is null || UseScriptHost(area.Snapshot) is not { } host
            || area.Snapshot.Objects.FirstOrDefault(o => o.Key == door.Key) is not { } row)
        { GD.Print($"ARC-WORLD-1668 native-smoke door-trap NONE map={_readyMapName} (no trap host or cast host)"); return "none: no trap host or cast host"; }
        PortalSmokeUntil(() => doors.GoalOf(door) is null, 3000);
        Require(portals.Frame(session, door) == 0 && doors.GoalOf(door) is null, $"the door stands shut with no run: frame {portals.Frame(session, door)} goal {doors.GoalOf(door)}");
        var mapId = new SessionMapId(CurrentAreaMap);
        var entity = AreaUseScriptHost.EntityId(row);
        var scriptKey = MapChangeKey.ForEntity(entity, AreaUseScriptHost.SlotProperty(ScrPoint.Use, "script"));
        var countersKey = MapChangeKey.ForEntity(entity, AreaUseScriptHost.SlotProperty(ScrPoint.Use, "counters"));
        var stateKey = MapChangeKey.ForEntity(entity, GameSessionOwner.TrapStateProperty);
        // The three keys as the session held them before the part (null: no change), written back at its end.
        var before = new[] { scriptKey, countersKey, stateKey }.Select(key => session.QueryMapChange(mapId, key)).ToArray();
        // The native script 30001 (normal damage to the triggerer alone) with counters 0 (a damage of 0 to 0) makes the door trapped and harmless; a content without it leaves the part undone.
        Require(session.ApplyChanges(session.Version, null, [new MapChangeEdit(mapId, scriptKey, 30001), new MapChangeEdit(mapId, countersKey, 0)]).Disposition == GameSessionDisposition.Changed, "the trap script is written");
        MapChangeEdit[] RestoreEdits() => [new(mapId, scriptKey, before[0]), new(mapId, countersKey, before[1]), new(mapId, stateKey, before[2])];
        try
        {
            if (host.TrapState(row) is not { Trapped: true })
            { GD.Print($"ARC-WORLD-1668 native-smoke door-trap NONE map={_readyMapName} (the content's script 30001 is no trap script)"); return "none: script 30001 is no trap"; }
            // The use node meets the trap: the opener's run ends, the door stays shut, no opening run starts.
            var (revision, draws) = (session.Version.Revision, session.QueryRandom().Draws);
            UsePortal(door);
            PortalSmokeUntil(() => false, 800);
            var met = (session.Version.Revision, session.QueryRandom().Draws) != (revision, draws);
            Require(met, "the use node met the door's trap (the spot roll or the run wrote something)");
            Require(portals.Frame(session, door) == 0 && doors.GoalOf(door) is null, $"the trapped door stayed shut and started no run: frame {portals.Frame(session, door)} goal {doors.GoalOf(door)} ({_portalNotice})");
            var outcome = traps.KnownTrap(row) ? "spotted" : host.TrapState(row) is { Removed: true } ? "removed" : "fired";
            GD.Print($"ARC-WORLD-1668 door-trap met entity={door.Entity} outcome={outcome} status={_portalNotice}");
            // With the trap taken away (the slot as the data had it) the next use opens the door.
            Require(session.ApplyChanges(session.Version, null, RestoreEdits()).Disposition == GameSessionDisposition.Changed, "the slot is restored");
            UsePortal(door);
            PortalSmokeUntil(() => doors.GoalOf(door) is not null, 800);
            Require(doors.GoalOf(door) == Arcanum.Simulation.Goals.GoalType.AnimateDoorOpen, $"with no trap the use opened the door: {doors.GoalOf(door)} ({_portalNotice})");
            PortalSmokeUntil(() => portals.Frame(session, door) == 0 && doors.GoalOf(door) is null, 8000 + 12 * door.StepMs);
            return "met " + outcome;
        }
        finally
        {
            // The use slot and the trap state as the session held them (the spot roll's draws and any experience it awarded stay).
            session.ApplyChanges(session.Version, null, RestoreEdits());
        }
    }
    // ARC-WORLD-1805: the installed use scripts of doors that teleport the hero (the census of ledger TT20): 2591 on map 5067 (to a cell of the same map), 2595 on 5068 (to the main map) and 2643 on 5071 (to map 5007).
    private static readonly int[] TeleportingDoorScripts = [2591, 2595, 2643];

    private bool IsTeleportingDoor(AreaPortal door) => _readyArea is { } area && UseScriptHost(area.Snapshot) is { } host
        && area.Snapshot.Objects.FirstOrDefault(o => o.Key == door.Key) is { } row && Array.IndexOf(TeleportingDoorScripts, host.UseScriptOf(row)) >= 0;

    /// <summary>ARC-WORLD-1805: the teleporting-door part of the door-close smoke; returns what it found ("queued" with the destination, or the reason it did not run).</summary>
    private string RunDoorTeleportPart(GameSessionOwner session, AreaPortals portals, AreaDoorHost doors)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-1805 door-close teleporting-door smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DOOR_CLOSE_SMOKE_FAILED");
        }
        // The same-map door first: its arrival is a cell this map opens, so the transfer can be queued without another map's load.
        var door = portals.All.Where(p => p.Frames.Count > 1 && p.OpenFrame != 0 && !p.UnlockScript && portals.Frame(session, p) == 0 && doors.GoalOf(p) is null && IsTeleportingDoor(p))
            .Where(p => portals.Decide(session, p) is AreaPortalUse.Opened or AreaPortalUse.Locked).OrderBy(p => p.Entity, StringComparer.Ordinal).FirstOrDefault();
        if (door is null || _mapTransfers is not { } transfers || _worldActor.Session is not AreaGridSession grid || _readyArea is not { } area)
        {
            var teleporting = portals.All.Where(IsTeleportingDoor).ToArray();
            GD.Print($"ARC-WORLD-1805 native-smoke door-teleport NONE map={_readyMapName} (no installed teleporting door the player can open here: {teleporting.Length} teleporting door(s) of {portals.All.Count()}, "
                + string.Join(";", teleporting.Select(p => $"{p.Entity} frames={p.Frames.Count} open={p.OpenFrame} unlock={p.UnlockScript} frame={portals.Frame(session, p)} goal={doors.GoalOf(p)} decide={portals.Decide(session, p)}")) + ")");
            return "none: no teleporting door";
        }
        // A cell next to the door, else a little further (the use goal walks the rest, as a click from there would).
        // The smoke places the hero by the map's own cells (a teleporting door of K'na Tha stands behind a locked one, so no route from the start reaches it).
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var center = grid.Chart.ToChart(door.Cell);
        var ring = Enum.GetValues<NeighborDirection>().Select(d => RoutePlanner.Neighbor(center, d)).ToArray();
        var near = ring.Concat(ring.SelectMany(c => Enum.GetValues<NeighborDirection>().Select(d => RoutePlanner.Neighbor(c, d))))
            .FirstOrDefault(c => grid.Chart.Contains(c) && grid.Snapshot.IsAdmitted(grid.Chart.ToWorld(c)));
        if (near == default) { GD.Print($"ARC-WORLD-1805 native-smoke door-teleport NONE map={_readyMapName} (no admitted cell within use range of the door)"); return "none: no cell near the door"; }
        AreaSmokePlace(map, near);
        PortalSmokeUntil(() => false, 200);
        // The installed teleporting doors of K'na Tha are locked: the node's access test comes before the script, so a locked door queues nothing (before ARC-WORLD-1805 the click took the transfer at once, lock or not); then the lock is taken off.
        var locked = portals.Decide(session, door) == AreaPortalUse.Locked;
        if (locked)
        {
            UsePortal(door);
            PortalSmokeUntil(() => false, 800);
            Require(transfers.Pending is null && portals.Frame(session, door) == 0 && doors.GoalOf(door) is null, $"a locked teleporting door queued nothing and stayed shut ({_portalNotice})");
            Require(session.SetOpenableLocked(session.Version, portals.Openable(door), door.Baseline, false).Disposition == GameSessionDisposition.Changed, "the lock is taken off");
            Require(portals.Decide(session, door) == AreaPortalUse.Opened, "the unlocked door can be opened");
        }
        Require(transfers.Pending is null && transfers.Triggers.All(k => k != door.Key) && transfers.ScriptedTriggers.All(k => k != door.Key), "the door is no use trigger of the map transfers and no transfer is pending");
        UsePortal(door);
        Require(PortalSmokeUntil(() => transfers.Pending is not null, 2000), $"the use node ran the door's script and queued its teleport as the map transfer ({_portalNotice})");
        var pending = transfers.Pending!;
        // The door goes on as the script's "run default" says: it opens (its run starts) while the transfer waits.
        Require(PortalSmokeUntil(() => portals.Frame(session, door) != 0 || doors.GoalOf(door) == Arcanum.Simulation.Goals.GoalType.AnimateDoorOpen, 1500),
            $"the door opened on the script's run default: frame {portals.Frame(session, door)} goal {doors.GoalOf(door)} ({_portalNotice})");
        var host = UseScriptHost(area.Snapshot)!;
        var row = area.Snapshot.Objects.First(o => o.Key == door.Key);
        var report = $"{(locked ? "locked: refused first; unlocked: " : "")}queued to map {pending.To.Catalogue} arrival ({pending.Arrival.X},{pending.Arrival.Y}) by script {host.UseScriptOf(row)} on {door.Entity}";
        GD.Print($"ARC-WORLD-1805 door-teleport queued entity={door.Entity} {report}");
        // The transfer itself is the transfer stage's: clear it so the stage ends on this map, then let the door close by itself.
        transfers.Clear();
        PortalSmokeUntil(() => portals.Frame(session, door) == 0 && doors.GoalOf(door) is null, 8000 + 12 * door.StepMs);
        // The lock as the data had it, so a later stage of the run finds the door as before.
        if (locked) session.SetOpenableLocked(session.Version, portals.Openable(door), door.Baseline, true);
        return report;
    }
}
