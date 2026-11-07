using Arcanum.Session;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-048 native smoke (map mode on the start map, stage "worldmap", last): W opens the world map; a far area refuses its
// one-waypoint route; the nearest area whose route plans is added; Enter travels at the original's pace; the journey discovers
// the area, jumps the session clock by an hour per sector step, lands and loads the start map around the area, with the
// session carried and the actor at the landing. MIG12 S8 (ARC-DEV-127): the steps are the session's real-time events (the first 50 ms
// after Enter, each next 62 ms after it), which the smoke's own frame stamps move (an automated run has no live flow) while the open map
// holds the Menu pause (until MIG12 S7): the steps fall due behind it and only their jumps move game time. Prints counts and outcomes only.
public partial class TerrainViewerApp
{
    private async Task RunAreaWorldMapSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-048 native-smoke worldmap FAIL " + why);
            throw new InvalidOperationException("ARC-UI-048 world-map smoke: " + why);
        }
        if (_areaSession is null) { GD.Print("ARC-UI-048 native-smoke worldmap SKIP (no area session)"); return; }
        // Only the start map (runtime map 1) opens it; an interior skips without waiting for the data.
        if (_readyArea!.Snapshot.RuntimeMap != 1) { GD.Print($"ARC-UI-048 native-smoke worldmap SKIP (map {_readyMapName} is not the start map)"); return; }
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
        // ARC-UI-073: the window's art reads beside the data; the stage waits for it so the window opens in its final look.
        for (var i = 0; i < 1200 && _worldMapArtTask is { IsCompleted: false }; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var artLook = AdoptWorldMapArt();
        var data = _worldMapData!.Data!;
        if (_readyArea!.Snapshot.RuntimeMap != data.StartRuntime) { GD.Print($"ARC-UI-048 native-smoke worldmap SKIP (map {_readyMapName} is not the start map)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var session = _areaSession!;
            InventoryKey(Key.W);
            Require(WorldMapOpen && WorldModal && _worldMap is not null, "W opened the world map: " + _worldMapStatus);
            // ARC-DEV-050: the area list and Teleport exist only in debug mode.
            Require(_worldMapPanel!.DebugControlsShownForSelfTest == DebugMode, $"area list and teleport shown={_worldMapPanel.DebugControlsShownForSelfTest} debugMode={DebugMode}");
            var map = _worldMap!;
            // ARC-UI-073: with the game's art read (and no debug mode) the window shows the original look: centred on the party, scrolled by
            // the compass' and the arrows' steps, clamped to the picture.
            Require(DebugMode || artLook is not null, "the world-map art read from the installed game");
            if (!DebugMode && artLook is not null)
            {
                var artView = _worldMapPanel.ArtView;
                Require(_worldMapPanel.ArtMode && artView.Visible, "the window shows the original look");
                var max = AreaWorldMapArtLayout.MaxOffset(data.Width, data.Width);
                Require(artView.Offset == AreaWorldMapArtLayout.CentreOn(map.Position.X, map.Position.Y, max), $"opened centred on the party: offset {artView.Offset}");
                var scrollFrom = artView.Offset;
                Require(artView.ScrollBy(2, AreaWorldMapArtLayout.KeyStep) == (scrollFrom.X < max.X) && artView.Offset.Y == scrollFrom.Y,
                    $"an east step moved the view by 20 px or stopped at the edge: {scrollFrom} -> {artView.Offset}");
                for (var i = 0; i < 200; i++) artView.ScrollBy(7, AreaWorldMapArtLayout.CompassStep);
                Require(artView.Offset == (0, 0), "scrolling clamps at the north-west corner");
                for (var i = 0; i < 200; i++) artView.ScrollBy(3, AreaWorldMapArtLayout.CompassStep);
                Require(artView.Offset == max, "scrolling clamps at the south-east corner");
                _worldMapPanel.CentreOnParty();
                Require(artView.Offset == AreaWorldMapArtLayout.CentreOn(map.Position.X, map.Position.Y, max), "Home centres on the party");
                GD.Print($"ARC-UI-073 native-smoke worldmap-art PASS offset={artView.Offset} max={max}");
            }
            else GD.Print($"ARC-UI-073 native-smoke worldmap-art SKIP (debugMode={DebugMode}, art {(artLook is null ? "not read" : "read")})");
            var here = WorldTravelGeometry.Location(map.Position, data.Width);
            var byDistance = Enumerable.Range(1, data.TravelAreas.Count - 1)
                .Where(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here) > data.TravelAreas[a].Radius)
                .OrderBy(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).ToArray();
            // The farthest placed area: its straight route meets mountains or water it cannot pass around.
            var far = byDistance.Last(a => data.TravelAreas[a].Radius > 0 && data.TravelAreas[a].Location != default);
            WorldMapWaypoint(() => map.AddArea(far), $"area {far}");
            Require(map.Waypoints.Count == 0 && _worldMapPanel!.MessageForSelfTest.Contains("601", StringComparison.Ordinal), $"far area {far} refused: {_worldMapPanel!.MessageForSelfTest}");
            var target = 0;
            // An area of negative radius is never discovered by travel (WT3): the target is one that can be.
            // An unknown target: a known one snaps the end onto its location, where the last step may take no hour.
            foreach (var area in byDistance.Where(a => data.TravelAreas[a].Radius > 0 && !session.IsAreaKnown(a)))
            {
                if (map.AddArea(area) != AreaWorldMapRefusal.None) continue;
                target = area; break;
            }
            Require(target > 0, "no area plans from here");
            RenderWorldMap(null);
            if (!DebugMode && _worldMapPanel.ArtMode)
            {
            // ARC-UI-083: a marker for each known area; the pointer over one shows its description and the readouts; Z and the buttons switch the kind.
                // A new game knows no area: one that is neither the target nor the far one is found now, as a journey's step finds one (WM11).
                var probe = byDistance.First(a => a != target && a != far && !session.IsAreaKnown(a) && data.TravelAreas[a].Location != default);
                session.DiscoverArea(session.Version, probe); session.ClearLastDiscoveredArea(session.Version);
                RenderWorldMap(null);
                var known = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(session.IsAreaKnown).OrderByDescending(a => a).ToArray();
                var artView = _worldMapPanel.ArtView;
                var max = AreaWorldMapArtLayout.MaxOffset(data.Width, data.Width);
                Require(artView.MarkersForSelfTest.SequenceEqual(known), $"a marker for each known area, the last first: {string.Join(",", artView.MarkersForSelfTest)} vs {string.Join(",", known)}");
                var descriptions = new List<string?>();
                void Collect(string? text) => descriptions.Add(text);
                _worldMapPanel.DescriptionRequested += Collect;
                var marked = artView.MarkersForSelfTest[0];
                var markedPixel = WorldTravelGeometry.Pixel(data.TravelAreas[marked].Location, data.Width);
                artView.SetOffsetForSelfTest(AreaWorldMapArtLayout.CentreOn(markedPixel.X, markedPixel.Y, max));
                var at = AreaWorldMapArtLayout.ToWindow(markedPixel.X, markedPixel.Y, artView.Offset);
                artView.PointerForSelfTest(new Vector2(at.X, at.Y));
                Require(descriptions.Count == 1 && descriptions[0] == data.Areas.Areas[marked].Description, $"the marker's description is asked for: {string.Join("|", descriptions)}");
                var (west, south) = artView.ReadoutForSelfTest!.Value;
                var expectedReadouts = AreaWorldMapArtLayout.Readouts(markedPixel.X, markedPixel.Y, data.Width, artLook!.Content.Offsets.Longitude, artLook.Content.Offsets.Latitude);
                Require((west, south) == expectedReadouts, $"the readouts name the sector: {west} / {south}");
                artView.PointerForSelfTest(new Vector2(at.X + 60, at.Y + 60));
                Require(descriptions.Count == 2 && descriptions[1] is null, "leaving the marker clears its description");
                _worldMapPanel.DescriptionRequested -= Collect;
                var mapOffset = artView.Offset;
                async Task Shot(string? path)
                {
                    if (path is null) return;
                    for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    image.SavePng(path);
                }
                var shot = System.Environment.GetEnvironmentVariable("ARC_WORLDMAP_SMOKE_SHOT") is { Length: > 0 } path ? path : null;
                await Shot(shot);
                _worldMapPanel.ToggleKind();
                Require(artView.Kind == AreaWorldMapKind.Continent && !artView.ScrollBy(2, AreaWorldMapArtLayout.KeyStep), "Z shows the continent view, which does not scroll");
                await Shot(shot is null ? null : System.IO.Path.ChangeExtension(shot, ".continent.png"));
                _worldMapPanel.ToggleKind();
                Require(artView.Kind == AreaWorldMapKind.Map && artView.Offset == mapOffset, "Z again shows the map view where it was left");
                GD.Print("ARC-UI-083 native-smoke worldmap-names PASS probe=" + probe + " markers=" + artView.MarkersForSelfTest.Count);
            }
            var (steps, hours) = map.Estimate();
            var before = session.QueryClockSnapshot().GameTimestampMs;
            var realBefore = session.QueryFrameClocks().RealMs;
            var wasKnown = session.IsAreaKnown(target);
            var started = System.Environment.TickCount64;
            InventoryKey(Key.Enter);
            Require(map.Travelling && session.QueryRealStep(SessionRealStep.WorldMapStep) is not null, "Enter started the journey and scheduled its first step");
            var behindPause = 0;
            // ARC-DEV-126 (MIG12 S7): Escape on the open map opens the in-play menu over it (kind 3), it does not end the journey; the travel steps run
            // behind the menu's pause and their time jumps still land, while ordinary game time stands still. The map itself is no pause.
            var menuState = 0; var pausedReal = 0L; var menuGameAtOpen = 0L; var menuJumpsAtOpen = 0L; var menuGameGained = 0L; var menuJumps = 0L; var menuTravelling = false;
            for (var i = 0; i < 36000 && WorldMapOpen; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var realBeforeTick = session.QueryFrameClocks().RealMs;
                TickAreaClock((long)Time.GetTicksMsec());
                if (MainMenuOpen) pausedReal += session.QueryFrameClocks().RealMs - realBeforeTick;
                else if (menuState == 0 && map.Travelling) Require(!session.QueryClockSnapshot().PauseDisposition.Paused, "the open map is no pause");
                if (map.Travelling && session.QueryClockSnapshot().PauseDisposition.Paused) behindPause++;
                if (menuState == 0 && i >= 1 && steps >= 8 && map.Travelling)
                {
                    InventoryKey(Key.Escape);
                    Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlayLimited && map.Travelling && WorldMapOpen,
                        "Escape opened the limited in-play menu over the map and the journey goes on: " + _mainMenuStatus);
                    menuGameAtOpen = session.QueryClockSnapshot().GameTimestampMs; menuJumpsAtOpen = map.GameMs; menuState = 1;
                }
                else if (menuState == 1 && pausedReal >= 250)
                {
                    menuGameGained = session.QueryClockSnapshot().GameTimestampMs - menuGameAtOpen; menuJumps = map.GameMs - menuJumpsAtOpen; menuTravelling = map.Travelling;
                    EndMainMenu("world map smoke"); menuState = 2;
                }
            }
            if (menuState == 1) { EndMainMenu("world map smoke"); throw new InvalidOperationException("ARC-UI-048 native-smoke worldmap: the journey ended behind the menu"); }
            if (steps >= 8)
                Require(menuState == 2 && menuTravelling && menuGameGained == menuJumps,
                    $"the journey's steps ran behind the Esc menu and only their jumps moved game time (menu {menuState}, travelling {menuTravelling}, game {menuGameGained} ms, jumps {menuJumps} ms)");
            var elapsed = System.Environment.TickCount64 - started;
            var realGained = session.QueryFrameClocks().RealMs - realBefore;
            // The clock as the journey left it (the frames of the load let ordinary time flow again).
            var gained = session.QueryClockSnapshot().GameTimestampMs - before;
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, session)); i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!WorldMapOpen && _mapHandoff is null, "the journey ended and the start map loaded");
            Require(ReferenceEquals(_areaSession, session), "the session is carried");
            Require(session.IsAreaKnown(target) && session.QueryLastDiscoveredArea() == 0, "the target is known, the last record cleared");
            // The jumps are the journey's hours; the game clock also flows at 8x real time behind the open map (it is no pause) except for the time the
            // menu held it (the 8x share of a real millisecond is the frame step's carry, so a few milliseconds of rounding are allowed).
            var ordinary = (realGained - pausedReal) * 8;
            Require(Math.Abs(gained - (hours * WorldJourney.MsPerSector + ordinary)) <= 16, $"clock gained {gained} ms for {hours} h and {ordinary} ms of ordinary time (menu held {pausedReal} real ms)");
            // The first step came 50 ms after Enter and each next one 62 ms of real time after it, behind the Esc menu when it is open.
            var paced = WorldJourney.FirstStepRealMs + (steps - 1) * WorldJourney.StepRealMs;
            Require(realGained >= paced, $"50 ms, then one step per 62 ms of real time: the real clock gained {realGained} ms for {steps} steps (at least {paced})");
            Require(session.QueryRealStep(SessionRealStep.WorldMapStep) is null, "no travel step is left pending");
            var landing = data.TravelAreas[target].Location;
            var actor = session.QueryActor();
            Require(actor.Map.RuntimeMap == data.StartRuntime, "on the start map");
            var off = Math.Max(Math.Abs(actor.Cell.X - landing.X), Math.Abs(actor.Cell.Y - landing.Y));
            Require(off <= 64, $"the actor stands {off} tiles from the landing");
            Require(_mapWindowCentre is { } centre && centre.X == landing.X >> 6 && centre.Y == landing.Y >> 6, "the window opened around the landing");
            GD.Print("ARC-UI-048 native-smoke worldmap PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Target = target, Far = far, Steps = steps, Hours = hours, ElapsedMs = elapsed, RealMs = realGained, BehindPause = behindPause, WasKnown = wasKnown,
                Known = session.QueryKnownAreas().Count, ActorOff = off, Rules = session.Rules.Travel.Name }));
        }
        finally
        {
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
