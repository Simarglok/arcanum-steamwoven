using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-138 (MIG13 S7, smoke plan SM10 `door-close`, bug B15) native check, part of the map battery and alone with --stages door-close (a hero, a map with a door the player can open): a use opens
// an unlocked door that keeps no portal flag 0x100 as the door's own run (goal 70); the run writes its frames at the ART's pace and turns into the pending close 72; the door is still open one second
// later; the Esc menu stops the run (five seconds behind it change nothing and the next frame credits one frame, not the time behind it); after the menu the door closes by itself, no sooner than
// the original's two waits of 1.5 s after its last open frame and (pause excluded) no later than those plus the closing steps. The frame clock is driven with literal host stamps, as the AI smokes do.
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
        var door = portals.All.Where(p => p.Frames.Count > 1 && p.OpenFrame != 0 && !p.UnlockScript && portals.Frame(session, p) == 0 && doors.GoalOf(p) is null)
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
            GD.Print("ARC-DEV-138 native-smoke door-close PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, door.Entity, door.OpenFrame, door.StepMs, Frames = seen.Count, ClosedAfterMs = elapsed, PausedMs = pausedMs, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            PortalSmokeClockEnd();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
