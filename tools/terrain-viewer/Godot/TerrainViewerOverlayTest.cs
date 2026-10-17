using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>Fills the overlay synchronously for a test (the ordinary route is one bounded batch per frame).</summary>
    internal void PassabilityOverlayFillForSelfTest()
    {
        RequireExplicitTest();
        for (var i = 0; i < 200; i++)
        {
            SyncPassabilityOverlay();
            if (_overlayLayer?.Overlay is not { } overlay || overlay.Failure is not null || _overlayLayer.Capped
                || _overlayLayer.VisibleCells.All(c => overlay.TryGet(c, out _))) break;
        }
    }

    /// <summary>ARC-RENDER-008 AC1/AC4/AC5 in the actual app: OFF at launch; palette checkbox by native click,
    /// controller and keyboard, one toggle per press; plain F3 inert; display-only invariants; projected
    /// geometry shared with the actor; red fill/edge/gray hatch rasters at zoom 0.5/1/2 and 1280x800/2560x1440;
    /// equal movement traces ON/OFF; no assessment while OFF.</summary>
    internal async Task WorldPassabilityOverlayMatrixForSelfTestAsync(CellCoord blocked, CellCoord open, CellCoord outside, string mode)
    {
        RequireExplicitTest();
        // The matrix sets world focus itself (WorldFocusForSelfTest) and owns it like the other GUI
        // self-tests: a native window focus-out during its awaits (a test window the OS never keeps
        // in front) must not drop the world focus and quarantine the controller frames it sends next.
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true;
        try { await WorldPassabilityOverlayMatrixScriptAsync(blocked, open, outside, mode); }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; }
    }
    private async Task WorldPassabilityOverlayMatrixScriptAsync(CellCoord blocked, CellCoord open, CellCoord outside, string mode)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-RENDER-008 overlay: " + why); }
        var none = new RawInputFrame(-1, default);
        void Frame(params RawInputFrame[] pads) => WorldFrameForSelfTest(none, pads.Length == 0 ? new[] { SimulatedPadLevel() } : pads, .1);
        var viewport = GetViewport(); var originalSize = new Vector2I((int)WorldViewport.X, (int)WorldViewport.Y);
        var graphical = DisplayServer.GetName() != "headless";
        if (WorldModal) ToggleWorldSettings();
        GetViewport().GuiReleaseFocus(); WorldFocusForSelfTest(true); SelectSimulatedPadForSelfTest(); ResetWorldScene(); WorldSettleForSelfTest(SimulatedPadLevel()); Frame(); Frame(); Frame();
        // 1. OFF at launch: no overlay instance, no assessment, nothing drawn.
        Require(!_passabilityOverlayOn && _overlayLayer?.Overlay is null, "overlay must start OFF");
        for (var i = 0; i < 5; i++) { Frame(); SyncPassabilityOverlay(); }
        Require(_overlayLayer?.Overlay is null && (_overlayLayer?.DrawnCells ?? 0) == 0, "OFF overlay assessed or drew");
        Require(_hud!.Text.Contains("Passability overlay OFF", StringComparison.Ordinal), "HUD OFF state");
        // Plain F3 (an original Followers key) never toggles the overlay.
        _Input(new InputEventKey { Keycode = Key.F3, Pressed = true }); _Input(new InputEventKey { Keycode = Key.F3, Pressed = false });
        Require(!_passabilityOverlayOn, "plain F3 toggled the overlay");

        // 2. Palette checkbox: native click, controller A and keyboard Space each toggle exactly once.
        // Opening the palette applies the existing modal policy; the toggles themselves must change nothing else.
        SendPaletteChordForSelfTest(); Require(WorldModal, "palette open");
        var actor = _worldActor.State; var camera = _camera!.State; var follow = _camera.FollowState; var zoom = _worldPreferredZoom;
        var settings = _worldSettings; var session = _worldActor.Session; var gate = _worldGate.IsOpen;
        var check = (CheckButton)_worldPanel!.FindChild("PassabilityOverlay", true, false)!;
        Require(!check.Disabled && !check.ButtonPressed && check.Text == "Show passability (read-only)", "checkbox available and OFF");
        var scroll = _worldPanel.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>().Single();
        scroll.EnsureControlVisible(check); await NextFrameAsync(); scroll.EnsureControlVisible(check); await NextFrameAsync();
        var point = check.GetGlobalRect().GetCenter();
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point });
        await NextFrameAsync();
        Require(_passabilityOverlayOn && check.ButtonPressed, "native click turned the overlay ON once");
        check.GrabFocus(); Frame(); Frame();
        viewport.PushInput(new InputEventJoypadButton { Device = SimulatedPadId, ButtonIndex = JoyButton.A, Pressed = true });
        Frame(SimulatedPadLevel() with { Confirm = true }); Frame(SimulatedPadLevel() with { Confirm = true });
        viewport.PushInput(new InputEventJoypadButton { Device = SimulatedPadId, ButtonIndex = JoyButton.A, Pressed = false });
        Frame();
        Require(!_passabilityOverlayOn && !check.ButtonPressed, "controller A toggled once (held A did not repeat)");
        check.GrabFocus();
        viewport.PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
        viewport.PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = true });
        Frame(); viewport.PushInput(new InputEventKey { Keycode = Key.Space, Pressed = false }); await NextFrameAsync();
        Require(_passabilityOverlayOn && check.ButtonPressed, "keyboard Space toggled once (echo did not repeat)");
        Require(_worldActor.State == actor && ReferenceEquals(_worldActor.Session, session) && _worldPreferredZoom == zoom && _worldGate.IsOpen == gate
            && _worldSettings == settings && _camera.State == camera && _camera.FollowState == follow,
            $"toggling changed actor/session/camera/Follow/zoom/settings camera={_camera.State}/{camera} follow={_camera.FollowState}/{follow}");
        SendPaletteChordForSelfTest(); Require(!WorldModal, "palette closed");
        Frame(); Frame();

        // 3. Geometry and classification at the required zooms and viewports.
        var layer = _overlayLayer!;
        Require(layer.GetParent() == _canvas && layer.GetGlobalTransformWithCanvas() == _canvas!.GetGlobalTransformWithCanvas(), "overlay shares the canvas transform (no double scaling)");
        int OriginX() => layer.Overlay?.OriginX ?? throw new InvalidOperationException("ARC-RENDER-008 overlay: not bound");
        Vector2 Center(CellCoord cell) => PassabilityOverlayLayer.Center(cell, OriginX());
        Require(OriginX() == (mode == "crash-site-area" ? 40 : 0), "AREA diamonds share the terrain tile origin (ARC-RENDER-011)");
        Require(Center(_worldActor.Session!.State.CommittedCell) == new Vector2((float)WorldAnchor.X, (float)WorldAnchor.Y),
            "overlay cell center is the actor's projected anchor");
        var samples = new List<string>();
        // Raster samples need a settled camera: no smoothing between the readback and the transform used to sample it.
        var smoothing = _camera.Camera.PositionSmoothingEnabled; _camera.Camera.PositionSmoothingEnabled = false;
        var kinds = new Dictionary<CellCoord, OverlayCellKind>();
        string? edgeSample = null;
        foreach (var size in new[] { new Vector2I(1280, 800), new(2560, 1440) })
        {
            if (viewport is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
            await NextFrameAsync(); await NextFrameAsync();
            Require(WorldViewport == new Vector2(size.X, size.Y), $"viewport {size}");
            foreach (var z in new[] { .5, 1, 2 })
            {
                SetPreferredWorldZoom(z); _camera.ResetWorld(WorldAnchor, WorldViewport, z);
                var calls = layer.Overlay!.AssessCalls;
                PassabilityOverlayFillForSelfTest(); await NextFrameAsync(); await NextFrameAsync();
                var overlay = layer.Overlay!;
                Require(!layer.Capped && overlay.Failure is null, $"z{z} overlay drawable");
                foreach (var cell in new[] { blocked, open, outside })
                {
                    Require(overlay.TryGet(cell, out var result), $"z{z} {cell} assessed");
                    if (kinds.TryGetValue(cell, out var kind)) Require(kind == result.Kind, $"{cell} classification drifted with view");
                    kinds[cell] = result.Kind;
                }
                Require(kinds[blocked] == OverlayCellKind.Blocked && kinds[open] == OverlayCellKind.Open && kinds[outside] == OverlayCellKind.Outside,
                    $"literal classes blocked={kinds[blocked]} open={kinds[open]} outside={kinds[outside]}");
                Require(_hud.Text.Contains("Passability overlay ON", StringComparison.Ordinal) && _hud.Text.Contains("legend:", StringComparison.Ordinal), "HUD legend/counts");
                if (!graphical) continue;
                // Raster: the blocked cell center is red-tinted against the OFF frame; the Open center is
                // untouched; the outside center lies on its middle gray hatch line (zoom >= 1).
                Color Pixel(Image image, CellCoord cell)
                {
                    var p = _canvas!.GetGlobalTransformWithCanvas() * Center(cell);
                    return image.GetPixel(Math.Clamp((int)p.X, 0, image.GetWidth() - 1), Math.Clamp((int)p.Y, 0, image.GetHeight() - 1));
                }
                bool OnScreen(CellCoord cell) => viewport.GetVisibleRect().HasPoint(_canvas!.GetGlobalTransformWithCanvas() * Center(cell));
                // The HUD is its own layer above the world: with it shown the overlay must not change HUD pixels;
                // world samples are taken with it hidden so cells under the HUD panel stay observable.
                using var hudOn = viewport.GetTexture().GetImage();
                layer.Visible = false; await NextFrameAsync(); await NextFrameAsync();
                using var hudOff = viewport.GetTexture().GetImage();
                var hudPoint = _hud.Rect.Position + new Vector2(6, 6);
                Require(hudOn.GetPixelv((Vector2I)hudPoint) == hudOff.GetPixelv((Vector2I)hudPoint), $"z{z} {size} overlay changed HUD pixels");
                _hud.Visible = false; await NextFrameAsync(); await NextFrameAsync();
                using var off = viewport.GetTexture().GetImage();
                layer.Visible = true; await NextFrameAsync(); await NextFrameAsync();
                using var on = viewport.GetTexture().GetImage();
                _hud.Visible = true; await NextFrameAsync();
                if (OnScreen(blocked))
                {
                    // Literal translucent red over whatever lies beneath (terrain or a scenery body): the OFF pixel
                    // blended with the fill colour at its alpha, so the scene stays readable under the overlay.
                    var a = Pixel(off, blocked); var b = Pixel(on, blocked); var f = PassabilityOverlayLayer.BlockedFill;
                    var expected = a.Lerp(new Color(f.R, f.G, f.B), f.A);
                    Require(Math.Abs(b.R - expected.R) < .03 && Math.Abs(b.G - expected.G) < .03 && Math.Abs(b.B - expected.B) < .03,
                        $"z{z} {size} blocked fill off={a} on={b} expected={expected}");
                }
                if (OnScreen(open)) Require(Pixel(off, open) == Pixel(on, open), $"z{z} {size} Open cell was filled");
                if (z >= 1 && OnScreen(outside))
                {
                    // The middle hatch line passes through the center; allow its 1px rasterization offset.
                    var c = _canvas!.GetGlobalTransformWithCanvas() * Center(outside);
                    var drawn = false;
                    for (var dy = -2; dy <= 2 && !drawn; dy++) for (var dx = -2; dx <= 2 && !drawn; dx++)
                    {
                        var x = Math.Clamp((int)c.X + dx, 0, on.GetWidth() - 1); var y = Math.Clamp((int)c.Y + dy, 0, on.GetHeight() - 1);
                        var p = on.GetPixel(x, y); var q = off.GetPixel(x, y);
                        drawn = p != q && Math.Abs(p.R - p.G) < .08 && Math.Abs(p.G - p.B) < .08;
                    }
                    Require(drawn, $"z{z} {size} outside gray hatch not drawn");
                }
                if (z >= 1)
                {
                    // A directed red mark: the arrow body just inside the cell, on the from->to line.
                    var edgeCell = layer.VisibleCells.Select(cell => overlay.TryGet(cell, out var r) ? r : null)
                        .FirstOrDefault(r => r is not null && r.BlockedEdges.Any() && OnScreen(r.Cell));
                    Require(edgeCell is not null, $"z{z} {size} no on-screen edge-only blocker to raster-check");
                    var e = edgeCell!.BlockedEdges.First();
                    var to = Center(edgeCell.Cell); var from = Center(e.From);
                    var mark = _canvas!.GetGlobalTransformWithCanvas() * ((to + from) / 2 + (to - from).Normalized() * 7);
                    var red = false;
                    for (var dy = -1; dy <= 1 && !red; dy++) for (var dx = -1; dx <= 1 && !red; dx++)
                    {
                        var p = on.GetPixel(Math.Clamp((int)mark.X + dx, 0, on.GetWidth() - 1), Math.Clamp((int)mark.Y + dy, 0, on.GetHeight() - 1));
                        red = p.R > .8 && p.G < .3 && p.B < .3;
                    }
                    Require(red, $"z{z} {size} directed red mark missing for {e.From}->{edgeCell.Cell}");
                    edgeSample ??= $"{e.From}->{edgeCell.Cell} {e.Direction} {e.Step.Reason}";
                }
                samples.Add($"{size.X}x{size.Y}@{z}");
            }
        }
        _camera.Camera.PositionSmoothingEnabled = smoothing;
        if (viewport is SubViewport restore) restore.Size = originalSize; else DisplayServer.WindowSetSize(originalSize);
        await NextFrameAsync();
        // Fit/pan never change classification or reassess cached cells.
        var cached = _overlayLayer!.Overlay!; var before = cached.AssessCalls;
        FitWorldView(); PassabilityOverlayFillForSelfTest();
        foreach (var (cell, kind) in kinds) { Require(_overlayLayer.Overlay == cached && cached.TryGet(cell, out var r) && r.Kind == kind, "Fit changed classification"); }
        Require(cached.AssessCalls >= before, "assessment counter regressed");

        // 4. Read-only: the same stick timeline gives identical movement events with the overlay ON and OFF.
        string Trace(bool overlayOn)
        {
            _passabilityOverlayOn = overlayOn; SyncPassabilityOverlay();
            SelectSimulatedPadForSelfTest(); RetireWorldLifecycle();
            var snapshotSession = _worldActor.Session!;
            if (snapshotSession is AreaGridSession area) _worldActor.Configure(area.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
            else _worldActor.Configure(((DiagnosticGridSession)snapshotSession).Snapshot, SimulationTime.At(0), StepDuration.Of(8));
            _worldGridInput = new(); _worldTestTime = SimulationTime.At(0); PublishWorldSelection();
            var events = new List<MovementEvent>();
            void At(int t)
            {
                WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1, true, SimulationTime.At(t));
                if (overlayOn) PassabilityOverlayFillForSelfTest();
                if (_worldActor.Session!.LastAdvance is { } advance) events.AddRange(advance.Movement.Events.Where(e => !events.Any(o => o.Ordinal == e.Ordinal)));
            }
            At(1); At(2); At(3);
            _worldTestTime = SimulationTime.At(10); SimulatedSectorKey(Key.S, true); SimulatedSectorKey(Key.A, true);
            for (var t = 11; t <= 17; t++) At(t);
            _worldTestTime = SimulationTime.At(18); SimulatedSectorKey(Key.S, false); SimulatedSectorKey(Key.A, false);
            for (var t = 19; t <= 30; t++) At(t);
            return string.Join("|", events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}"))
                + $" cell={_worldActor.Session!.State.CommittedCell} phase={_worldActor.Session.State.Phase} frame={_worldActor.CurrentFrame}";
        }
        var offTrace = Trace(false);
        Require(_overlayLayer.Overlay is null, "OFF trace had an overlay");
        var onTrace = Trace(true);
        Require(_overlayLayer.Overlay is { AssessCalls: > 0 }, "ON trace never assessed");
        Require(offTrace == onTrace && offTrace.Contains("StepStarted", StringComparison.Ordinal), $"overlay changed movement: off={offTrace} on={onTrace}");

        // Restore a live-duration session, OFF overlay and the default zoom for later matrices.
        _passabilityOverlayOn = false; SyncPassabilityOverlay();
        var live = _worldActor.Session!;
        if (live is AreaGridSession liveArea) _worldActor.Configure(liveArea.Snapshot, _worldTestTime!.Value);
        else _worldActor.Configure(((DiagnosticGridSession)live).Snapshot, _worldTestTime!.Value);
        _worldGridInput = new(); RetireWorldLifecycle(); ReleaseSimulatedPadForSelfTest(); PublishWorldSelection();
        SetPreferredWorldZoom(2); _overlayPanelState = null;
        GD.Print($"ARC-RENDER-008 overlay matrix PASS mode={mode} off-at-launch/zero-off-assessment/F3-inert/click-controller-keyboard single toggles/display-only/geometry/literal blocked-open-outside/fit/paired traces; rasters={(graphical ? string.Join(",", samples) + " edge " + edgeSample : "NOT_VERIFIED headless")}");
    }

    internal void SetPassabilityOverlayForSelfTest(bool on) { RequireExplicitTest(); SetPassabilityOverlay(on); }
}

public partial class TerrainViewerApp
{
    /// <summary>ARC-RENDER-008 AC3 on authenticated original data, through the live session's own production
    /// assessor: a known matched Open/Blocked pair with its reverse, at least one positive directional Blocked,
    /// and a cell outside the admitted coverage. Read-only: no I/O, no movement. Report is numeric and path-free.</summary>
    private void OriginalPassabilityOverlayCheck(string mode, CellCoord outside, string blockerReason)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-RENDER-008 original overlay: " + why); }
        var session = _worldActor.Session ?? throw new InvalidOperationException("ARC-RENDER-008 original overlay: no session");
        IPassabilityAuthority authority = session switch
        {
            AreaGridSession area => new AreaPassabilityAuthority(area),
            DiagnosticGridSession bounded => new CardinalPassabilityAuthority(bounded),
            _ => throw new InvalidOperationException("ARC-RENDER-008 original overlay: unsupported session"),
        };
        var state = session.State; var spawn = state.CommittedCell;
        var overlay = new PassabilityOverlay(authority);
        var window = new List<CellCoord>();
        for (var y = spawn.Y - 3; y <= spawn.Y + 3; y++) for (var x = spawn.X - 3; x <= spawn.X + 3; x++) window.Add(new(x, y));
        overlay.Fill(window.Append(outside), 1000);
        Require(overlay.Failure is null, "authority failed");
        OverlayStep Step(CellCoord from, CellCoord to) { overlay.TryGet(to, out var cell); return cell.Incoming.Single(i => i.From == from).Step; }
        var east = new CellCoord(spawn.X + 1, spawn.Y); var west = new CellCoord(spawn.X - 1, spawn.Y);
        var open = Step(spawn, east); var blocked = Step(spawn, west); var reverse = Step(west, spawn);
        Require(open.Verdict == OverlayStepVerdict.Open, $"spawn->east {open}");
        // The known original scenery blocker west of the spawn, and its asymmetric Open reverse.
        Require(blocked == new OverlayStep(OverlayStepVerdict.Blocked, blockerReason), $"spawn->west {blocked}");
        Require(reverse.Verdict == OverlayStepVerdict.Open, $"west->spawn reverse {reverse}");
        // Edge-only: the map diagonal spawn->(x-1,y-1) passes the blocker as a shoulder; that cell is not filled.
        var corner = new CellCoord(spawn.X - 1, spawn.Y - 1); var diagonal = Step(spawn, corner);
        overlay.TryGet(corner, out var cornerCell);
        Require(diagonal.Verdict == OverlayStepVerdict.Blocked && cornerCell.Kind != OverlayCellKind.Blocked
            && cornerCell.BlockedEdges.Any(e => e.From == spawn), $"spawn->corner edge-only {diagonal} {cornerCell.Kind}");
        var anyBlocked = window.Where(c => overlay.TryGet(c, out _)).Select(c => { overlay.TryGet(c, out var r); return r; })
            .SelectMany(r => r.Incoming.Where(i => i.Step.Verdict == OverlayStepVerdict.Blocked).Select(i => (r.Cell, i))).ToArray();
        Require(anyBlocked.Length > 0, "no positive directional Blocked near spawn");
        overlay.TryGet(outside, out var far);
        Require(far.Kind == OverlayCellKind.Outside && far.Incoming.All(i => i.Step.Verdict == OverlayStepVerdict.Unsupported), $"outside cell {outside} is {far.Kind}");
        Require(ReferenceEquals(state, session.State) && ReferenceEquals(session, _worldActor.Session), "overlay changed movement state");
        var counts = overlay.Count(window);
        GD.Print("ARC-RENDER-008 original overlay PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            mode, profile = authority.ProfileId, actor = authority.ActorId, identity = authority.Identity,
            open = new { from = new[] { spawn.X, spawn.Y }, to = new[] { east.X, east.Y }, open.Verdict, open.Reason },
            blocked = new { from = new[] { spawn.X, spawn.Y }, to = new[] { west.X, west.Y }, blocked.Verdict, blocked.Reason },
            reverse = new { from = new[] { west.X, west.Y }, to = new[] { spawn.X, spawn.Y }, reverse.Verdict, reverse.Reason },
            firstBlocked = new { from = new[] { anyBlocked[0].i.From.X, anyBlocked[0].i.From.Y }, to = new[] { anyBlocked[0].Cell.X, anyBlocked[0].Cell.Y }, direction = anyBlocked[0].i.Direction.ToString(), anyBlocked[0].i.Step.Reason },
            outside = new { cell = new[] { outside.X, outside.Y }, kind = far.Kind.ToString(), reasons = far.Incoming.Select(i => i.Step.Reason).Distinct().ToArray() },
            window = new { counts.Open, counts.Blocked, counts.Unknown, counts.Outside, counts.BlockedEdges, counts.UnknownEdges },
            assessments = overlay.AssessCalls, parity = "NOT_VERIFIED"
        }));
    }
}
