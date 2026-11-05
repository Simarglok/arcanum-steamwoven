using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-125 (MIG12 S6) native check (part of the map stage on a map with a session and a lighting driver): the world time the rules read
// follows the session's frame clocks when they are driven by literal host stamps, exactly as in play. The consumers read the frame
// participant's world milliseconds (a literal 16 ms frame is 16 ms); a frame under 5 ms adds nothing and the next adds the sum; the hero's
// click route walks on that clock and arrives; the Esc pause stops the world time and the hero stands still behind it without a refusal of
// the actor's observations, and the walk resumes after it; a time jump of five minutes moves game time and not the world time. The actor's
// place and the smoke's own test time are restored afterwards. Prints counts only.
public partial class TerrainViewerApp
{
    private const int FrameClockSmokeFrames = 3000;
    // Short, so that the later stages of the battery start in the same game hour as before.
    private const long JumpMilliseconds = 300_000;

    private void RunAreaFrameClockSmoke()
    {
        if (_areaSession is not { } session || _areaAmbient is not { } driver || _frameTap is not { } tap || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-DEV-125 native-smoke frame clock SKIP map={_readyMapName} (no session or lighting driver)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-125 frame-clock smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_FRAME_CLOCK_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var start = grid.State.CommittedCell;
        CellCoord? goal = null;
        foreach (var (dx, dy) in new[] { (5, 0), (-5, 0), (0, 5), (0, -5), (4, 4), (-4, -4), (3, 0), (-3, 0), (0, 3), (0, -3) })
        {
            var near = new CellCoord(start.X + dx, start.Y + dy);
            if (!map.Chart.Contains(near)) continue;
            CellCoord cell;
            try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
            if (Math.Max(Math.Abs(cell.X - start.X), Math.Abs(cell.Y - start.Y)) >= 3) { goal = cell; break; }
        }
        if (goal is not { } target) { GD.Print($"ARC-DEV-125 native-smoke frame clock SKIP map={_readyMapName} (no standable goal near the hero)"); return; }

        var testTime = _worldTestTime; var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        var host = 20_000_000L;
        long Frame(long step, bool modal = false)
        {
            host += step;
            if (driver.Tick(host, modal, AmbientSector())) PushAreaAmbient();
            WorldObservation(() => ApplyWorldObservedFrame(.016, true, new(-1, default), new[] { StickLevel(default) }, "ARC-DEV-125 frame clock smoke"));
            return tap.Clocks!.Value.WorldMs;
        }
        void Place()
        {
            ConfigureAreaActor(map, start, NeighborDirection.XPlus);
            for (var i = 0; i < 64 && !_worldGate.IsOpen; i++) Frame(16);
            Require(_worldGate.IsOpen, "the actor's gate opens on the frame clock");
        }
        object? walked = null, paused = null;
        try
        {
            _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
            _worldTestTime = null; _frameClockForSelfTest = true;
            session.ResetFrameReference();
            // A fresh actor at the frame clock's time (its input queue starts empty: the test time's observations are not carried over).
            Place();
            Require(LiveFrameClock && _worldTimeline.FollowsFrames, "the world time follows the frame clock, not the host");

            // The consumers' clock is the participant's world milliseconds: a 16 ms frame is 16 ms, a 2 ms frame adds nothing.
            var ms = WorldMilliseconds(); var world = tap.Clocks!.Value.WorldMs;
            for (var i = 0; i < 10; i++) Frame(16);
            Require(WorldMilliseconds() - ms == 160 && tap.Clocks!.Value.WorldMs - world == 160, $"ten 16 ms frames are 160 world ms ({WorldMilliseconds() - ms})");
            ms = WorldMilliseconds();
            Frame(2); Frame(2);
            Require(WorldMilliseconds() == ms, "frames under 5 ms add nothing");
            Frame(2);
            Require(WorldMilliseconds() - ms == 6, $"short frames add up from the unmoved reference ({WorldMilliseconds() - ms})");

            // The hero's click route walks on the frame clock and arrives.
            var route = WorldRoute((AreaGridSession)_worldActor.Session!) ?? throw new InvalidOperationException("AREA_ROUTE_REQUIRED");
            route.SetTarget(target, WorldTime);
            var frames = 0; var before = WorldMilliseconds();
            while (frames < FrameClockSmokeFrames && route.Status == RouteFollowStatus.Following) { Frame(16); frames++; }
            var there = (AreaGridSession)_worldActor.Session!;
            Require(route.Status == RouteFollowStatus.Arrived && there.State.CommittedCell == target, $"the route arrives on the frame clock ({DescribeWorldRoute(route)}, frames {frames})");
            var elapsed = WorldMilliseconds() - before;
            Require(elapsed == frames * 16L, $"the walk took the admitted time ({elapsed} ms for {frames} frames)");
            walked = new { Frames = frames, WorldMs = elapsed };

            // The Esc pause: the world time and the hero stand still, the actor's observations are not refused, the walk resumes after it.
            Place();
            route = WorldRoute((AreaGridSession)_worldActor.Session!) ?? throw new InvalidOperationException("AREA_ROUTE_REQUIRED");
            route.SetTarget(target, WorldTime);
            for (var i = 0; i < 4; i++) Frame(16);
            var cell = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var held = WorldMilliseconds();
            for (var i = 0; i < 120; i++) Frame(16, modal: true);
            Require(tap.Flags.Paused && driver.MenuPaused, "the menu pause is held");
            Require(WorldMilliseconds() == held, $"the world time stands still behind the pause ({WorldMilliseconds() - held} ms)");
            Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == cell && route.Status == RouteFollowStatus.Following, "the hero stands still behind the pause");
            Frame(16);
            Require(!tap.Flags.Paused && WorldMilliseconds() - held == 16, $"the pause releases and the next frame credits 16 ms, not the 1920 behind it ({WorldMilliseconds() - held})");
            frames = 0;
            while (frames < FrameClockSmokeFrames && route.Status == RouteFollowStatus.Following) { Frame(16); frames++; }
            Require(route.Status == RouteFollowStatus.Arrived && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == target, "the walk resumes after the pause and arrives");
            paused = new { Held = 120, ResumedFrames = frames };

            // A jump moves game and animation time, never the world time.
            var game = tap.Clocks!.Value.GameMs; var worldNow = WorldMilliseconds();
            driver.Frame.Jump(JumpMilliseconds);
            Frame(16);
            Require(WorldMilliseconds() - worldNow == 16 && tap.Clocks!.Value.GameMs - game >= JumpMilliseconds, "a jump of five minutes leaves the world time alone");

            // ARC-DEV-126 (MIG12 S7, GQ3 A): the production tick reads the pause from the Esc menu alone. An open window (the inventory) does not stop the
            // world time; the Esc menu does, and the next frame after it credits one frame, not the time behind it.
            long Production(long step)
            {
                host += step; TickAreaClock(host);
                WorldObservation(() => ApplyWorldObservedFrame(.016, true, new(-1, default), new[] { StickLevel(default) }, "ARC-DEV-126 frame clock smoke"));
                return WorldMilliseconds();
            }
            Production(16);
            InventoryKey(Key.I);
            Require(InventoryOpen && WorldModal, "I opened the inventory: " + _inventoryStatus);
            var open = WorldMilliseconds();
            for (var i = 0; i < 10; i++) Production(16);
            Require(WorldMilliseconds() - open == 160 && !session.QueryClockSnapshot().PauseDisposition.Paused && !tap.Flags.Paused,
                $"the world runs behind the open inventory ({WorldMilliseconds() - open} ms for ten frames, paused {tap.Flags.Paused})");
            InventoryKey(Key.I);
            Require(!InventoryOpen, "I closed the inventory again");
            OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!), AreaMenuWindow.InPlay);
            Require(MainMenuOpen && WorldMenuPause, "the Esc menu is open and is the pause source");
            Production(16);
            var menuAt = WorldMilliseconds();
            for (var i = 0; i < 120; i++) Production(16);
            Require(tap.Flags.Paused && WorldMilliseconds() == menuAt, $"the world time stands behind the Esc menu ({WorldMilliseconds() - menuAt} ms)");
            EndMainMenu("frame clock smoke");
            Production(16);
            Require(!tap.Flags.Paused && WorldMilliseconds() - menuAt == 16, $"the next frame after the menu credits 16 ms ({WorldMilliseconds() - menuAt})");
        }
        finally
        {
            _frameClockForSelfTest = false; driver.Tick(host + 16, false, AmbientSector());
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; _worldTestTime = SimulationTime.At(0);
            AreaSmokePlace(map, start);
            RestoreWorldTestTime(testTime);
        }
        GD.Print("ARC-DEV-125 native-smoke frame clock PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Walked = walked, Paused = paused, TapFrames = tap.Frames }));
    }
}
