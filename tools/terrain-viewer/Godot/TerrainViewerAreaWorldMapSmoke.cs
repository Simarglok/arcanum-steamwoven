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
        var savedInterfaceHd = _gameSettings.HdInterface;
        try
        {
            // ARC-UI-126: ARC_AREA_WORLDMAP_HD_VERIFY=1 runs the stage with the HD interface art of ARC_HD_PACK (the bar and sheet smokes' switch), for a native look.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_WORLDMAP_HD_VERIFY") == "1")
            {
                Require(_canvas?.HdSource?.Pack is not null, "HD verification needs ARC_HD_PACK");
                _gameSettings = _gameSettings with { HdInterface = true };
                SyncInterfaceHd();
                Require(InterfaceHd.Enabled, "HD interface switch enabled for this isolated smoke");
            }
            var session = _areaSession!;
            // ARC-UI-084: a PC in a sector with a town map opens the town map and has no walkable world (WM5, WM6); the journey part below needs the
            // walkable one, so after the town checks the gate is left off (a PC walked out of the town would have it).
            if (!DebugMode && artLook is not null && PcTownMapNumber() > 0)
            {
                await RunWorldMapTownSmokeAsync(data);
                _worldMapTownGateOffForSmoke = true;
            }
            else GD.Print($"ARC-UI-084 native-smoke worldmap-town SKIP (debugMode={DebugMode}, art {(artLook is null ? "not read" : "read")}, town map {PcTownMapNumber()})");
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
                // ARC-UI-134 (WM140): the lens shows the game view around the hero under the window, drawn over by art 198 when it read.
                for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var (holeCorner, holeEdge) = artView.LensOnScreen;
                Require(_worldMapPanel.RoundViewShown && !artView.LensFrozen && holeEdge == AreaWorldMapArtLayout.Lens.Side * artView.Scale.X
                    && holeCorner.DistanceTo(artView.WindowPointForSelfTest(AreaWorldMapArtLayout.Lens.X, AreaWorldMapArtLayout.Lens.Y)) < .01f,
                    $"the lens shows the round view: shown={_worldMapPanel.RoundViewShown} frozen={artView.LensFrozen} origin={holeCorner} side={holeEdge}");
                Require(artView.HasLensArt == (artLook.Art.ContainsKey(AreaWorldMapArtLayout.LensArt)), "the lens art is drawn when it read");
                // A native look (ARC_WORLDMAP_SMOKE_SHOT): the frame with the live lens; the shot is original-derived and stays under local/.
                if (System.Environment.GetEnvironmentVariable("ARC_WORLDMAP_SMOKE_SHOT") is { Length: > 0 } lensShot)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.ChangeExtension(lensShot, ".lens.png"));
                }
                GD.Print($"ARC-UI-073 native-smoke worldmap-art PASS offset={artView.Offset} max={max}");
                // ARC-UI-133 (WM36): through the real pointer route (so an input layer over the hole would be seen), a left release in the PC lens closes the
                // window and a press, or a release just outside the square, does not; the key W opens it again.
                async Task LensFrames()
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                async Task LensButton(Vector2 at, bool pressed)
                {
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0 });
                    await LensFrames();
                }
                var (lensX, lensY, lensSide) = AreaWorldMapArtLayout.Lens;
                await LensFrames();
                var lensCentre = artView.WindowPointForSelfTest(lensX + lensSide / 2.0, lensY + lensSide / 2.0);
                var lensCorner = artView.WindowPointForSelfTest(lensX + 2, lensY + 2);
                foreach (var outside in new[] { (lensX + lensSide, lensY + 40), (lensX + 40, lensY + lensSide), (lensX - 1, lensY + 40), (lensX + 40, lensY - 1) })
                {
                    var lensAt = artView.WindowPointForSelfTest(outside.Item1 + .5, outside.Item2 + .5);
                    await LensButton(lensAt, true); await LensButton(lensAt, false);
                    Require(WorldMapOpen, $"a click just outside the lens at window {outside} left the map open");
                }
                await LensButton(lensCentre, true);
                Require(WorldMapOpen, "a press in the lens does not close the map");
                await LensButton(lensCentre, false);
                Require(!WorldMapOpen && !WorldModal && !_worldMapPanel!.Visible, "a left release in the lens closed the map");
                InventoryKey(Key.W);
                Require(WorldMapOpen && WorldModal && _worldMap is not null, "W opened the map again: " + _worldMapStatus);
                map = _worldMap!;
                await LensFrames();
                await LensButton(lensCorner, true); await LensButton(lensCorner, false);
                Require(!WorldMapOpen, "a corner of the lens square, outside the round ring, closes the map as well (the test is a square)");
                InventoryKey(Key.W);
                Require(WorldMapOpen && WorldModal && _worldMap is not null, "W opened the map after the corner click: " + _worldMapStatus);
                map = _worldMap!;
                GD.Print("ARC-UI-133 native-smoke worldmap-lens PASS");
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
            // An area of negative radius is never discovered by travel (WT3): the target is one that can be. An unknown target is preferred (the journey then discovers it).
            // A click within 320 tiles of a KNOWN area snaps onto that area's location (WT4, 0x4CB2A0), and a segment whose end snaps back onto the start's own sector plans no route (601);
            // the map chain's talk stage learns areas 1 and 2 through a real dialogue, so after it the unknown areas near the start all snap there (ARC-DEV-320).
            // Then the nearest known area that plans is the target: its end is its own location, where the last step may take no hour (the clock check below allows for it).
            var refused = new List<string>();
            foreach (var area in byDistance.Where(a => data.TravelAreas[a].Radius > 0).OrderBy(a => session.IsAreaKnown(a)))
            {
                var refusal = map.AddArea(area);
                if (refusal != AreaWorldMapRefusal.None) { refused.Add($"{area}:{refusal}"); continue; }
                target = area; break;
            }
            Require(target > 0, $"no area plans from here {map.Position}; known {string.Join(",", session.QueryKnownAreas())}; refused {string.Join(" ", refused.Take(12))}");
            RenderWorldMap(null);
            if (!DebugMode && _worldMapPanel.ArtMode)
            {
            // ARC-UI-083: a marker for each known area; the pointer over one shows its description and the readouts; Z and the buttons switch the kind.
                // A new game knows no area: one that is neither the target nor the far one is found now, as a journey's step finds one (WM11).
                var probe = byDistance.First(a => a != target && a != far && !session.IsAreaKnown(a) && data.TravelAreas[a].Location != default);
                // WM11: the markers of the areas known at the open come from the last area down to 1; one found since is appended (the chain's talk stage may have made some known, ARC-DEV-320).
                var knownAtOpen = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(session.IsAreaKnown).OrderByDescending(a => a).ToArray();
                session.DiscoverArea(session.Version, probe); session.ClearLastDiscoveredArea(session.Version);
                RenderWorldMap(null);
                var known = knownAtOpen.Concat(Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => session.IsAreaKnown(a) && !knownAtOpen.Contains(a))).ToArray();
                var artView = _worldMapPanel.ArtView;
                var max = AreaWorldMapArtLayout.MaxOffset(data.Width, data.Width);
                Require(artView.MarkersForSelfTest.SequenceEqual(known), $"a marker for each known area, the last first (then the later ones): {string.Join(",", artView.MarkersForSelfTest)} vs {string.Join(",", known)}");
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

                // ARC-UI-085: the waypoint list as the original handles it (WM95–WM104): a press on a waypoint selects it, disarms and starts the drag; the drag scrolls
                // the view near its edge and never moves the waypoint; the left release re-arms; a right release removes the selected waypoint.
                Require(map.Waypoints.Count == 1 && map.Selected == 0 && artView.ArmedForSelfTest && !artView.DraggingForSelfTest, $"one waypoint, selected, armed: {map.Waypoints.Count} {map.Selected}");
                var clicked = map.Waypoints[0].Pixel;
                artView.SetOffsetForSelfTest(AreaWorldMapArtLayout.CentreOn(clicked.X, clicked.Y, max));
                var pressAt = AreaWorldMapArtLayout.ToWindow(clicked.X, clicked.Y, artView.Offset);
                artView.LeftForSelfTest(new Vector2(pressAt.X, pressAt.Y), true);
                Require(map.Waypoints.Count == 1 && !artView.ArmedForSelfTest && artView.DraggingForSelfTest, "a press on the waypoint selects it, disarms and starts the drag");
                artView.LeftForSelfTest(new Vector2(pressAt.X, pressAt.Y), true);
                Require(map.Waypoints.Count == 1, "a second press while disarmed adds nothing");
                var dragFrom = artView.Offset;
                var view = AreaWorldMapArtLayout.View;
                artView.DragForSelfTest(new Vector2(view.X + 5, view.Y + view.Height / 2));
                Require(artView.Offset.X == Math.Max(0, dragFrom.X - AreaWorldMapArtLayout.DragStep) && artView.Offset.Y == dragFrom.Y, $"the west edge scrolls the view 50 px: {dragFrom} -> {artView.Offset}");
                artView.DragForSelfTest(new Vector2(view.X + view.Width / 2, view.Y + view.Height / 2));
                Require(artView.Offset.X == Math.Max(0, dragFrom.X - AreaWorldMapArtLayout.DragStep), "the middle of the view does not scroll");
                Require(map.Waypoints[0].Pixel == clicked, "the drag does not move the waypoint");
                artView.LeftForSelfTest(new Vector2(pressAt.X, pressAt.Y), false);
                Require(artView.ArmedForSelfTest && !artView.DraggingForSelfTest && map.Waypoints.Count == 1, "the release re-arms and ends the drag");
                artView.RightReleaseForSelfTest();
                Require(map.Waypoints.Count == 0 && map.Selected == -1, "a right release removes the selected waypoint");
                RenderWorldMap(null);
                Require(map.AddArea(target) == AreaWorldMapRefusal.None && map.Selected == 0, "the target waypoint is back, selected");
                RenderWorldMap(null);
                // ARC-UI-105 (WM98): a right release in the continent view removes the last waypoint too (when the picture is there to show it).
                if (artView.ToggleKind())
                {
                    Require(artView.Kind == AreaWorldMapKind.Continent, "Z shows the continent view for the right release");
                    artView.RightReleaseForSelfTest();
                    Require(map.Waypoints.Count == 0, "a right release in the continent view removes the waypoint");
                    artView.ToggleKind();
                    Require(map.AddArea(target) == AreaWorldMapRefusal.None && map.Selected == 0, "the target waypoint is back again");
                    RenderWorldMap(null);
                }
                // ARC-UI-110 (Steamwoven rules): the note quill in the map view and the continent view, as the town map has it (ARC-UI-106): Q enters it (the waypoint selection is
                // cleared, the waypoints stay), a press on empty ground starts a note whose text Enter keeps, a press on a note edits it and an empty text deletes it; Escape
                // cancels; the right release only leaves the quill; the session keeps each view's list apart; with the quill off a hovered note shows its text.
                {
                    var panel = _worldMapPanel;
                    // A window point of the current view whose picture pixel has no area marker near it (a marker's hover would win over a note's).
                    Vector2 ClearPoint(AreaWorldMapKind kind)
                    {
                        var half = kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.HoverHalf : AreaWorldMapArtLayout.ZoomedHoverHalf;
                        foreach (var (dx, dy) in new[] { (0, 0), (90, 0), (-90, 0), (0, 80), (0, -80), (90, 80), (-90, -80) })
                        {
                            var point = kind == AreaWorldMapKind.Map ? new Vector2(view.X + view.Width / 2 + dx, view.Y + view.Height / 2 + dy)
                                : new Vector2(AreaWorldMapArtLayout.Zoomed.X + AreaWorldMapArtLayout.Zoomed.Width / 2 + dx, AreaWorldMapArtLayout.Zoomed.Y + AreaWorldMapArtLayout.Zoomed.Height / 2 + dy);
                            var pixel = kind == AreaWorldMapKind.Map ? AreaWorldMapArtLayout.ToPicture(point.X, point.Y, artView.Offset) : AreaWorldMapArtLayout.ZoomedToPicture(point.X, point.Y, data.Width);
                            if (pixel is not { } found) continue;
                            var near = artView.MarkersForSelfTest.Any(area => WorldTravelGeometry.Pixel(data.TravelAreas[area].Location, data.Width) is var m
                                && Math.Abs(m.X - found.X) <= half + 2 && Math.Abs(m.Y - found.Y) <= half + 2);
                            if (!near) return point;
                        }
                        throw new InvalidOperationException("ARC-UI-110 world-map quill smoke: no marker-free point in the view");
                    }
                    void QuillCycle(AreaWorldMapKind kind, int sessionView)
                    {
                        var notes = artView.NotesForSelfTest(kind)!;
                        var count = notes.Count;
                        Require(!panel.QuillActive && artView.Kind == kind && count == 0, $"{kind}: the quill starts off and the view holds no note");
                        var waypoints = map.Waypoints.Count;
                        InventoryKey(Key.Q);
                        Require(panel.QuillActive && map.Selected == -1 && map.Waypoints.Count == waypoints, $"{kind}: Q entered the quill, cleared the selection and kept the waypoints");
                        var spot = ClearPoint(kind);
                        artView.LeftForSelfTest(spot, true); artView.LeftForSelfTest(spot, false);
                        Require(panel.EditingNote && artView.NoteEditIndexForSelfTest == -1 && map.Waypoints.Count == waypoints && artView.Kind == kind, $"{kind}: a quill press on empty ground opens a new note's text entry (no waypoint added, no zoom)");
                        artView.NoteEditTextForSelfTest = "Smoke quill note";
                        InventoryKey(Key.Enter);
                        Require(!panel.EditingNote && notes.Count == 1 && notes.Items[0].Text == "Smoke quill note" && notes.Items[0].Type == AreaTownNotes.QuestionType, $"{kind}: Enter keeps the new note");
                        Require(session.QueryMapNotes(sessionView) is { } kept && kept.Notes.Count == 1 && kept.Notes[0].Text == "Smoke quill note"
                            && session.QueryMapNotes(sessionView == SessionStoryLimits.MapNoteView ? SessionStoryLimits.ContinentNoteView : SessionStoryLimits.MapNoteView) is null, $"{kind}: the session keeps this view's note alone");
                        artView.LeftForSelfTest(spot, true); artView.LeftForSelfTest(spot, false);
                        Require(panel.EditingNote && artView.NoteEditIndexForSelfTest == 0 && artView.NoteEditTextForSelfTest == "Smoke quill note", $"{kind}: a quill press on the note opens its text");
                        artView.NoteEditTextForSelfTest = "Edited";
                        InventoryKey(Key.Escape);
                        Require(!panel.EditingNote && notes.Items[0].Text == "Smoke quill note" && !MainMenuOpen, $"{kind}: Escape cancels the entry and opens no menu");
                        artView.LeftForSelfTest(spot, true); artView.LeftForSelfTest(spot, false);
                        artView.NoteEditTextForSelfTest = "Edited";
                        InventoryKey(Key.Enter);
                        Require(notes.Items[0].Text == "Edited" && session.QueryMapNotes(sessionView)!.Notes[0].Text == "Edited", $"{kind}: Enter keeps the edited text");
                        // The quill's press leaves the hover off; with the quill off the note shows its text unless a marker lies under the pointer.
                        artView.PointerForSelfTest(spot);
                        Require(artView.HoverWorldNoteForSelfTest == -1, $"{kind}: the hover is off with the quill");
                        artView.RightReleaseForSelfTest();
                        Require(!panel.QuillActive && map.Waypoints.Count == waypoints, $"{kind}: the right release only leaves the quill and removes no waypoint");
                        var texts = new List<string?>();
                        void Collect(string? text) => texts.Add(text);
                        panel.DescriptionRequested += Collect;
                        artView.PointerForSelfTest(spot + new Vector2(0, 1));
                        panel.DescriptionRequested -= Collect;
                        Require(artView.HoverWorldNoteForSelfTest == 0 && texts.Count > 0 && texts[^1] == "Edited", $"{kind}: the pointer over the note asks for its text: {string.Join("|", texts)}");
                        InventoryKey(Key.Q);
                        artView.LeftForSelfTest(spot, true); artView.LeftForSelfTest(spot, false);
                        artView.NoteEditTextForSelfTest = "  ";
                        InventoryKey(Key.Enter);
                        Require(notes.Count == 0 && session.QueryMapNotes(sessionView) is null, $"{kind}: an empty text deletes the note and the session drops the empty list");
                        // A note stays in the session across the window: put one, close, reopen.
                        artView.LeftForSelfTest(spot, true); artView.LeftForSelfTest(spot, false);
                        artView.NoteEditTextForSelfTest = "Kept";
                        InventoryKey(Key.Enter);
                        Require(notes.Count == 1, $"{kind}: a note is back for the reopen check");
                        InventoryKey(Key.Q);
                        Require(!panel.QuillActive, $"{kind}: Q left the quill");
                    }
                    QuillCycle(AreaWorldMapKind.Map, SessionStoryLimits.MapNoteView);
                    // The list a window opened later is given is read from the session (a reopen would also drop the journey's waypoints, so the read is made directly).
                    Require(MapNotesOf(session, SessionStoryLimits.MapNoteView) is { Count: 1 } reread && reread.Items[0].Text == "Kept" && MapNotesOf(session, SessionStoryLimits.ContinentNoteView).Count == 0,
                        "the list a reopened window reads comes from the session");
                    session.RecordMapNotes(session.Version, SessionStoryLimits.MapNoteView, AreaTownNotes.FirstId, []);
                    if (!artView.ToggleKind()) GD.Print("ARC-UI-110 native-smoke worldmap-quill continent SKIP (the continent picture is not there)");
                    else
                    {
                        QuillCycle(AreaWorldMapKind.Continent, SessionStoryLimits.ContinentNoteView);
                        // A kind switch ends the quill (WM103).
                        InventoryKey(Key.Q);
                        Require(panel.QuillActive, "Q entered the quill again in the continent view");
                        artView.ToggleKind();
                        Require(!panel.QuillActive && artView.Kind == AreaWorldMapKind.Map, "Z leaves the quill with the kind switch");
                        session.RecordMapNotes(session.Version, SessionStoryLimits.ContinentNoteView, AreaTownNotes.FirstId, []);
                    }
                    // The quill stays on for the journey's Enter below, which must leave it as the Travel button does.
                    Require(artView.Kind == AreaWorldMapKind.Map && !panel.QuillActive, "the map view is shown, quill off");
                    InventoryKey(Key.Q);
                    Require(panel.QuillActive, "Q entered the quill before Enter");
                    GD.Print("ARC-UI-110 native-smoke worldmap-quill PASS");
                }
                GD.Print("ARC-UI-085 native-smoke worldmap-waypoints PASS");
            }
            var (steps, hours) = map.Estimate();
            var before = session.QueryClockSnapshot().GameTimestampMs;
            var realBefore = session.QueryFrameClocks().RealMs;
            var wasKnown = session.IsAreaKnown(target);
            var started = System.Environment.TickCount64;
            InventoryKey(Key.Enter);
            Require(map.Travelling && session.QueryRealStep(SessionRealStep.WorldMapStep) is not null, "Enter started the journey and scheduled its first step");
            // ARC-UI-134 (WM141): the journey turns the lens black (mode 2: a black square with art 198), the live view is off until it ends.
            if (_worldMapPanel.ArtMode)
            {
                for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_worldMapPanel.ArtView.LensFrozen && !_worldMapPanel.RoundViewShown, $"the journey blackens the lens: frozen={_worldMapPanel.ArtView.LensFrozen} view={_worldMapPanel.RoundViewShown}");
                if (System.Environment.GetEnvironmentVariable("ARC_WORLDMAP_SMOKE_SHOT") is { Length: > 0 } travelShot)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.ChangeExtension(travelShot, ".travel.png"));
                }
            }
            if (!DebugMode && _worldMapPanel.ArtMode) Require(!_worldMapPanel.QuillActive, "Enter left the quill on as it started the journey (ARC-UI-110)");
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
            // A known target ends at the area's own location, not a sector centre: its last step may be shorter than a sector and then takes 1 ms instead of an hour, which the estimate does not model (ARC-DEV-320).
            var shortLast = wasKnown && !session.Rules.Travel.SegmentEndAddsNoTime ? WorldJourney.MsPerSector : 0;
            var expected = hours * WorldJourney.MsPerSector + ordinary;
            Require(gained <= expected + 16 && gained >= expected - shortLast - 16, $"clock gained {gained} ms for {hours} h and {ordinary} ms of ordinary time (menu held {pausedReal} real ms)");
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
            _worldMapTownGateOffForSmoke = false;
            if (_gameSettings.HdInterface != savedInterfaceHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd }; SyncInterfaceHd(); }
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }

    // ARC-UI-084 native observation (stage "worldmap", the PC standing in a sector with a town map): W opens the town map centred on the party pixel;
    // the scroll clamps at both corners; Home centres; Z cycles the continent view and back; tiles were read as drawn; a whole map and a fogged one
    // are drawn and the frame holds lit pixels (and, fogged, dark ones). Prints counts only.
    private async Task RunWorldMapTownSmokeAsync(AreaWorldMapData data)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-084 native-smoke worldmap-town FAIL " + why);
            throw new InvalidOperationException("ARC-UI-084 world-map town smoke: " + why);
        }
        var number = PcTownMapNumber();
        // ARC-WORLD-122 (WM78): the hero standing in the town has revealed the tile under it in the session (the frame hook ran while the map loaded).
        var session = _areaSession!;
        for (var i = 0; i < 1200 && _townMapTask is { IsCompleted: false }; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // An automated run has no live per-frame flow, so the hook the frame calls (SyncAreaWorldMap) is called here, twice: the second call changes nothing.
        RevealWalkedTownMapCell();
        var revision = session.Version.Revision;
        RevealWalkedTownMapCell();
        Require(session.Version.Revision == revision, "an unchanged cell reveals nothing more");
        var (px, py) = PcCell();
        var walked = AreaTownMapLayout.CellOf(AdoptTownMapSource()!.Prepare(number)!.Info, px, py);
        Require(walked is { } cell && session.IsTownMapCellRevealed(number, cell) && (session.QueryTownMapCellCount(number) >= 1 || session.IsTownMapRevealed(number)),
            $"the tile under the hero is revealed: cell {walked}, {session.QueryTownMapCellCount(number)} walked");
        InventoryKey(Key.W);
        Require(WorldMapOpen && _worldMap is not null, "W opened the world map in the town-map sector " + number + ": " + _worldMapStatus);
        var map = _worldMap!;
        var view = _worldMapPanel!.ArtView;
        Require(_worldMapPanel.ArtMode && view.HasTown && view.Kind == AreaWorldMapKind.Town, $"the town map {number} opened (kind {view.Kind}, town {view.HasTown})");
        Require(!map.OnStartMap, "a PC in a town-map sector is not on the walkable world");
        var max = view.TownMaxOffset; var party = view.TownParty;
        Require(max.X >= 0 && max.Y >= 0, "scroll maxima");
        Require(view.TownOffset == AreaWorldMapArtLayout.CentreOn(view.TownParty.X, view.TownParty.Y, max), $"opened centred on the party pixel {view.TownParty}: offset {view.TownOffset}");
        for (var i = 0; i < 400; i++) view.ScrollBy(7, AreaWorldMapArtLayout.CompassStep);
        Require(view.TownOffset == (0, 0), "scrolling clamps at the north-west corner");
        for (var i = 0; i < 400; i++) view.ScrollBy(3, AreaWorldMapArtLayout.CompassStep);
        Require(view.TownOffset == max, $"scrolling clamps at the south-east corner {view.TownOffset} vs {max}");
        _worldMapPanel.CentreOnParty();
        Require(view.TownOffset == AreaWorldMapArtLayout.CentreOn(view.TownParty.X, view.TownParty.Y, max), "Home centres on the party");
        // Z leaves for the continent view (when the picture is there) and comes back to the town map; the zoom button stays on it.
        if (view.ToggleKind())
        {
            Require(view.Kind == AreaWorldMapKind.Continent, "Z shows the continent view");
            Require(view.ToggleKind() && view.Kind == AreaWorldMapKind.Town, "Z again shows the town map");
        }
        Require(!view.ShowMapOrTown() && view.Kind == AreaWorldMapKind.Town, "the zoom button keeps the town map");
        var shot = System.Environment.GetEnvironmentVariable("ARC_WORLDMAP_SMOKE_SHOT") is { Length: > 0 } path ? path : null;
        async Task<(int Lit, int Dark)> Sample(string? file)
        {
            for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (file is not null) image.SavePng(file);
            var scale = view.Scale.X; var origin = view.GlobalPosition;
            var (vx, vy, vw, vh) = AreaWorldMapArtLayout.View;
            int lit = 0, dark = 0;
            for (var y = 0; y < vh - 40; y += 6)
                for (var x = 0; x < vw; x += 6)
                {
                    var px = (int)(origin.X + (vx + x + .5f) * scale); var py = (int)(origin.Y + (vy + y + .5f) * scale);
                    if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight()) continue;
                    var c = image.GetPixel(px, py);
                    if (c.R + c.G + c.B > .09f) lit++; else dark++;
                }
            return (lit, dark);
        }
        var plan = view.TownPlanForSelfTest!;
        // The view as it opened: a whole map shows every tile; else the session's reveal decides (a town opened by a script has all bits; otherwise none).
        var (wholeLit, wholeDark) = await Sample(shot is null ? null : System.IO.Path.ChangeExtension(shot, ".town.png"));
        Require(view.TownTexturesForSelfTest > 0 && view.TownTexturesForSelfTest <= 400, $"tiles read as drawn: {view.TownTexturesForSelfTest}");
        if (plan.Entry.Whole) Require(wholeLit > wholeDark / 2, $"a whole map shows its tiles: {wholeLit} lit, {wholeDark} dark");
        // A fogged town (the whole flag off) with the session's real reveal: the tile under the hero plain, its neighbours fading, the rest black.
        view.TownRevealForSelfTest(false, index => session.IsTownMapCellRevealed(number, index));
        var (fogLit, fogDark) = await Sample(shot is null ? null : System.IO.Path.ChangeExtension(shot, ".fog.png"));
        Require(fogLit > 0 && fogDark > 0, $"the fogged town shows lit and dark areas: {fogLit} lit, {fogDark} dark");
        view.TownRevealForSelfTest(false, _ => false);
        var (blackLit, _) = await Sample(null);
        Require(blackLit < fogLit || fogLit == 0, $"an unrevealed town without a whole flag is black: {blackLit} lit");
        // ARC-UI-103 (WM79, WM127, WM59): a note added as a marked scenery adds it (a second one near it is refused) is drawn on the open map and shows its text when hovered.
        var notes = view.TownNotesForSelfTest!;
        var noteCount = notes.Count;
        Require(notes.AddForObject(plan.Info, px, py, "Smoke Sign") && notes.Count == noteCount + 1, "a marked scenery adds a note at its tile");
        Require(!notes.AddForObject(plan.Info, px, py, "Smoke Sign Again") && notes.Count == noteCount + 1, "a note near an existing one is not added again");
        // ARC-UI-104: the list goes to the session, which keeps it (and a window opened later reads it back).
        Require(RecordTownNotes(session, number, notes) && session.QueryTownNotes(number) is { } kept && kept.Notes.Count == notes.Count && kept.Notes[^1].Text == "Smoke Sign",
            "the session keeps the town's notes");
        var noteAt = new Vector2(AreaWorldMapArtLayout.View.X + view.TownParty.X - view.TownOffset.X, AreaWorldMapArtLayout.View.Y + view.TownParty.Y - view.TownOffset.Y);
        view.PointerForSelfTest(noteAt);
        Require(view.HoverNoteForSelfTest == notes.Count - 1, $"the pointer over the note hits it: {view.HoverNoteForSelfTest}");
        view.PointerForSelfTest(noteAt + new Vector2(60, 0));
        Require(view.HoverNoteForSelfTest == -1, "the pointer away from the note leaves it");
        // ARC-UI-106 (WM30, WM92; Steamwoven rules): Q enters the quill (cursor art 21), a press on empty ground starts a note whose text Enter keeps, a press on a
        // note edits it and an empty text deletes it; Escape cancels the entry; the right button leaves the quill; the session keeps every change.
        var panel = _worldMapPanel!;
        Require(!panel.QuillActive, "the quill starts off");
        InventoryKey(Key.Q);
        Require(panel.QuillActive, "Q entered the quill");
        view.PointerForSelfTest(noteAt);
        Require(view.HoverNoteForSelfTest == -1, "the marker hover is off with the quill (WM59)");
        InventoryKey(Key.Q);
        Require(!panel.QuillActive, "Q left the quill");
        InventoryKey(Key.Q);
        var before = notes.Count;
        var placed = false;
        foreach (var away in new[] { new Vector2(60, 0), new Vector2(-60, 0), new Vector2(0, 60), new Vector2(0, -60) })
        {
            view.LeftForSelfTest(noteAt + away, true); view.LeftForSelfTest(noteAt + away, false);
            if (panel.EditingNote) { placed = true; break; }
        }
        Require(placed && view.NoteEditIndexForSelfTest == -1, "a quill press on empty ground opens a new note's text entry");
        view.NoteEditTextForSelfTest = "Quill note";
        InventoryKey(Key.Enter);
        Require(!panel.EditingNote && notes.Count == before + 1 && notes.Items[^1].Text == "Quill note" && notes.Items[^1].Type == AreaTownNotes.QuestionType, "Enter keeps the new note");
        Require(session.QueryTownNotes(number) is { } kept2 && kept2.Notes.Count == notes.Count && kept2.Notes[^1].Text == "Quill note", "the session keeps the quill's note");
        var madeAt = new Vector2(AreaWorldMapArtLayout.View.X + notes.Items[^1].PixelX - view.TownOffset.X, AreaWorldMapArtLayout.View.Y + notes.Items[^1].PixelY - view.TownOffset.Y);
        view.LeftForSelfTest(madeAt, true); view.LeftForSelfTest(madeAt, false);
        Require(panel.EditingNote && view.NoteEditIndexForSelfTest == notes.Count - 1 && view.NoteEditTextForSelfTest == "Quill note", "a quill press on a note opens its text");
        view.NoteEditTextForSelfTest = "Edited";
        InventoryKey(Key.Escape);
        Require(!panel.EditingNote && notes.Items[^1].Text == "Quill note" && !MainMenuOpen, "Escape cancels the entry and opens no menu");
        view.LeftForSelfTest(madeAt, true); view.LeftForSelfTest(madeAt, false);
        view.NoteEditTextForSelfTest = "Edited";
        InventoryKey(Key.Enter);
        Require(notes.Count == before + 1 && notes.Items[^1].Text == "Edited" && session.QueryTownNotes(number)!.Notes[^1].Text == "Edited", "Enter keeps the edited text");
        view.LeftForSelfTest(madeAt, true); view.LeftForSelfTest(madeAt, false);
        view.NoteEditTextForSelfTest = "  ";
        InventoryKey(Key.Enter);
        Require(notes.Count == before && session.QueryTownNotes(number)!.Notes.Count == before, "an empty text deletes the note");
        view.RightReleaseForSelfTest();
        Require(!panel.QuillActive, "the right button leaves the quill (WM98)");
        session.RecordTownNotes(session.Version, number, AreaTownNotes.FirstId, []);
        InventoryKey(Key.W);
        Require(!WorldMapOpen, "W closed the town map");
        await RunTownWaypointSmokeAsync(number);
        GD.Print($"ARC-UI-084 native-smoke worldmap-town PASS number={number} name={plan.Entry.Name} whole={plan.Entry.Whole} tiles={plan.Info.Columns}x{plan.Info.Rows} party={party} max={max} wholeLit={wholeLit} fogLit={fogLit} fogDark={fogDark}");
    }

    // ARC-UI-092: the town map's waypoints through the view's own mouse path (the list outlives the window, WM89), then Travel.
    private async Task RunTownWaypointSmokeAsync(int number)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-092 native-smoke worldmap-town-waypoints FAIL " + why);
            throw new InvalidOperationException("ARC-UI-092 town waypoint smoke: " + why);
        }
        InventoryKey(Key.W);
        Require(WorldMapOpen && _worldMapPanel!.ArtView is { Kind: AreaWorldMapKind.Town, HasTown: true }, "W reopened the town map");
        var view = _worldMapPanel!.ArtView;
        var info = view.TownPlanForSelfTest!.Info;
        var pc = PcCell();
        Require(_townWaypoints.Count == 0, "no town waypoints yet");
        // The path test's own limits (WM118, WM119): the PC's own tile, and a tile 32 tiles away, have no path.
        Require(!TownPathExists(pc, pc) && !TownPathExists(pc, (pc.X + 32, pc.Y)) && !TownPathExists(pc, (pc.X, pc.Y - 32)), "the PC's tile and 32 tiles away are refused");
        var refusals = new List<AreaTownWaypointRefusal>();
        void Refused(AreaTownWaypointRefusal refusal) => refusals.Add(refusal);
        _worldMapPanel.TownWaypointRefused += Refused;
        view.Centre();
        // Tiles four apart in each direction (their diamonds lie 40 px or more apart on screen, beyond a waypoint's hit box).
        (int X, int Y)[] steps = [(4, 0), (8, 0), (0, 4), (0, 8), (-4, 0), (-8, 0), (0, -4), (0, -8)];
        var added = 0;
        foreach (var (dx, dy) in steps)
        {
            var (tx, ty) = AreaTownMapLayout.ToPixel(info, pc.X + dx, pc.Y + dy);
            var window = AreaWorldMapArtLayout.ToWindow(tx + 10, ty + 5, view.TownOffset);
            var at = new Vector2(window.X, window.Y);
            if (AreaWorldMapArtLayout.ToPicture(at.X, at.Y, view.TownOffset) is null) continue;
            var before = _townWaypoints.Count;
            view.LeftForSelfTest(at, true); view.LeftForSelfTest(at, false);
            if (_townWaypoints.Count > before) { added++; Require(_townWaypoints.Selected == _townWaypoints.Count - 1, "an added waypoint is selected"); }
            if (added == AreaTownWaypoints.TownCap) break;
        }
        Require(added >= 1, $"at least one town waypoint was added (refusals {string.Join(",", refusals)})");
        Require(added >= 2 && TownPathExists(pc, _townWaypoints.Items[0].Location), $"at least two waypoints were added and the path test finds the first's path again ({added})");
        // A press on the first waypoint selects it and adds nothing.
        var firstPixel = _townWaypoints.Items[0].Pixel;
        var firstWindow = AreaWorldMapArtLayout.ToWindow(firstPixel.X, firstPixel.Y, view.TownOffset);
        var count = _townWaypoints.Count;
        view.LeftForSelfTest(new Vector2(firstWindow.X, firstWindow.Y), true); view.LeftForSelfTest(new Vector2(firstWindow.X, firstWindow.Y), false);
        Require(_townWaypoints.Count == count && _townWaypoints.Selected == 0, "a press on a waypoint selects it");
        // The first press of a window is not dead (WM86's flaw is fixed): the adds above prove it. The sixth add is refused with 611 when there is a seventh place to ask.
        var capNote = "6 not reached";
        if (_townWaypoints.Count == AreaTownWaypoints.TownCap)
        {
            var seventh = steps.Select(step => AreaTownMapLayout.ToPixel(info, pc.X + step.X + 2, pc.Y + step.Y + 2)).Select(p => AreaWorldMapArtLayout.ToWindow(p.X + 10, p.Y + 5, view.TownOffset))
                .FirstOrDefault(w => AreaWorldMapArtLayout.ToPicture(w.X, w.Y, view.TownOffset) is not null);
            if (seventh != default) { view.LeftForSelfTest(new Vector2(seventh.X, seventh.Y), true); view.LeftForSelfTest(new Vector2(seventh.X, seventh.Y), false); }
            Require(_townWaypoints.Count == AreaTownWaypoints.TownCap && refusals.Contains(AreaTownWaypointRefusal.TownCap), $"a seventh waypoint is refused with 611: {string.Join(",", refusals)}");
            capNote = "6 reached, 7th refused";
        }
        _worldMapPanel.TownWaypointRefused -= Refused;
        // Delete and the right release remove the selected waypoint, or the last, and keep the selection in range.
        count = _townWaypoints.Count;
        _worldMapPanel.RemoveWaypoint();
        Require(_townWaypoints.Count == count - 1 && _townWaypoints.Selected <= _townWaypoints.Count - 1, "Delete removes the selected waypoint");
        if (_townWaypoints.Count == 0)
        {
            // Put one back for Travel.
            var (tx, ty) = AreaTownMapLayout.ToPixel(info, pc.X + 4, pc.Y);
            var window = AreaWorldMapArtLayout.ToWindow(tx + 10, ty + 5, view.TownOffset);
            view.LeftForSelfTest(new Vector2(window.X, window.Y), true); view.LeftForSelfTest(new Vector2(window.X, window.Y), false);
            Require(_townWaypoints.Count == 1, "a waypoint was put back");
        }
        var beforeRight = _townWaypoints.Count;
        view.RightReleaseForSelfTest();
        Require(_townWaypoints.Count == beforeRight - 1, "a right release removes a waypoint");
        var left = _townWaypoints.Count;
        if (left == 0)
        {
            // Put it back once more: Travel needs one.
            var (tx, ty) = AreaTownMapLayout.ToPixel(info, pc.X + 4, pc.Y);
            var window = AreaWorldMapArtLayout.ToWindow(tx + 10, ty + 5, view.TownOffset);
            view.LeftForSelfTest(new Vector2(window.X, window.Y), true); view.LeftForSelfTest(new Vector2(window.X, window.Y), false);
        }
        Require(_townWaypoints.Count >= 1, "a waypoint is left for Travel");
        // Travel: the window closes, the list stays, and the PC walks the route as one run of its table, one goal-3 walk per waypoint (ARC-DEV-311, ARC-DEV-322: the same in both settings, no switch).
        var waypoints = _townWaypoints.Count;
        var routeTiles = _townWaypoints.Items.Select(w => w.Location).ToList();
        Require(routeTiles.Count >= 2, $"a route of several waypoints: {routeTiles.Count}");
        StartTownTravel();
        Require(!WorldMapOpen && _townWaypoints.Count == waypoints, "Travel closed the window and kept the list");
        var routeRun = HeroRunsHost()?.WalkRun;
        Require(routeRun is not null, "the PC walks to the first waypoint");
        var legs = routeRun!.Frames.Count(f => f.Goal == Arcanum.Simulation.Goals.GoalType.MoveToTile);
        Require(legs == routeTiles.Count, $"one run holds every waypoint as a walk: {legs} of {routeTiles.Count}");
        // The walk the check started must not outlive it (the journey checks that follow expect a standing hero).
        HeroRunsHost()?.EndHeldWalk("ARC-DEV-322 smoke");
        Require(HeroRunsHost()?.WalkRun is null, "the interrupt ended the whole route run");
        GD.Print($"ARC-UI-092 native-smoke worldmap-town-waypoints PASS number={number} added={added} {capNote} left={waypoints}");
        await Task.CompletedTask;
    }
}
