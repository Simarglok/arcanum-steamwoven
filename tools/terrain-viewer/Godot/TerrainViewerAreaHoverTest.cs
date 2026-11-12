using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-005 over the strict synthetic AREA owner in the production app: tooltip, action cursor, guards.</summary>
    private static async Task AssertAreaHoverAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = await app.AreaHoverForSelfTestAsync(host, steps);
            app.ClearSyntheticForSelfTest();
            Require(app.HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow && app.HoverTextForSelfTest is null, "clearing the scene gives the default cursor back");
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-005 self-test PASS synthetic hover tooltip and action cursor ({summary}); original names and talk/use covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<string> AreaHoverForSelfTestAsync(Node host, Arcanum.Data.Traversal.AreaStepSnapshot steps)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA hover: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _readyMode != TerrainViewerMode.Area) throw new InvalidOperationException("AREA_HOVER_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // The synthetic module path is not a game folder: names fall back to the object type.
            for (var i = 0; i < 200 && _hoverNames is null; i++) { await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
            Require(_hoverNames?.Status == AreaNamesStatus.ModuleOutsideGameRoot, "synthetic names status " + _hoverNames?.Status);
            OpenHoverGate(steps);
            var index = HoverIndex()!;
            Require(index.Bodies.Count > 0, "the synthetic owner has pickable bodies");

            // A body: frame it, find a pixel where it is the frontmost pick, hover there.
            var body = index.Bodies[^1];
            var point = await HoverPointAsync(host, body) ?? throw new InvalidOperationException("AREA hover: synthetic body not visible");
            HoverAtForSelfTest(point);
            var name = index.Name(body.Identity!.Value)!;
            var chart = _canvas!.GetGlobalTransformWithCanvas().AffineInverse() * point;
            var cell = AreaCellPicking.Pick(new(chart.X, chart.Y))!.Value;
            var expected = AreaHover.Ground(Routes(), ((AreaGridSession)_worldActor.Session!).State.CommittedCell, cell);
            Require(HoverKeyForSelfTest == body.Identity && HoverActionForSelfTest == expected, $"body hover {HoverKeyForSelfTest} {HoverActionForSelfTest}, expected {expected}");
            Require(name.StartsWith("[", StringComparison.Ordinal) && name.EndsWith("name unavailable]", StringComparison.Ordinal), "type fallback name " + name);
            Require(HoverTextForSelfTest == AreaHover.Tooltip(name, expected), "tooltip names the body and its click action: " + HoverTextForSelfTest);
            Require(HoverCursorForSelfTest == (expected == AreaHoverAction.Walk ? Godot.Input.CursorShape.Cross : Godot.Input.CursorShape.Forbidden), "action cursor");
            // ARC-DEV-164: the panel sits 16 px right of the pointer (or 8 px left when flipped) and its position is rounded to whole pixels (crisp font art, ARC-DEV-156), while the pointer
            // found by the scan may sit on a half pixel (338.5): the rounding moves the panel by up to half a pixel, so the check allows one pixel, which still keeps the pointed spot uncovered.
            var panelLeft = _hoverPanel!.Position.X; var panelRight = panelLeft + _hoverPanel.GetCombinedMinimumSize().X;
            Require(panelLeft >= point.X + 15 || panelRight <= point.X - 7, $"the tooltip does not cover the pointer: panel {panelLeft}..{panelRight}, pointer {point.X}");
            Require(GetSelfTestSnapshot().Hud.Contains($" | hover {name} -> {AreaHover.Label(expected)} (names ModuleOutsideGameRoot)", StringComparison.Ordinal), "HUD hover status");
            // What the cursor promised is what the click does: a body that is neither speaker nor trigger is a ground goal.
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(HeroWalkGoal == cell, "the left click set the hovered cell as goal");
            HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "ARC-UI-005 self-test"); StopWorldRoute("ARC-UI-005 self-test");

            // Bare ground near the actor: an enterable cell is Walk, a cell no neighbour step enters is NoRoute; no tooltip.
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var actor = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var near = Enumerable.Range(1, 6).SelectMany(r => Enumerable.Range(-r, 2 * r + 1).SelectMany(d => new CellCoord[]
                { new(actor.X + d, actor.Y - r), new(actor.X + d, actor.Y + r), new(actor.X - r, actor.Y + d), new(actor.X + r, actor.Y + d) }))
                .Distinct().Select(c => (Cell: c, Point: GroundPoint(c))).Where(c => c.Point is not null).ToArray();
            var outcomes = new HashSet<AreaHoverAction>();
            foreach (var wanted in new[] { AreaHoverAction.Walk, AreaHoverAction.NoRoute })
            {
                var (groundCell, ground) = near.FirstOrDefault(c => AreaHover.Ground(Routes(), actor, c.Cell) == wanted);
                if (ground is null) continue;
                HoverAtForSelfTest(ground);
                Require(HoverActionForSelfTest == wanted && HoverTextForSelfTest is null
                    && HoverCursorForSelfTest == (wanted == AreaHoverAction.Walk ? Godot.Input.CursorShape.Cross : Godot.Input.CursorShape.Forbidden),
                    $"bare ground {groundCell}: {HoverActionForSelfTest}, expected {wanted}");
                outcomes.Add(wanted);
            }
            Require(outcomes.Contains(AreaHoverAction.Walk) && outcomes.Contains(AreaHoverAction.NoRoute), "both ground outcomes observed near the actor");

            // ARC-UI-056: with the original's cursor art read (here injected) the art replaces the action shapes and does not follow
            // the target; without it the shapes come back.
            Require(CursorShownForSelfTest is null, "no cursor art without a game folder");
            _cursorContentForSelfTest = SyntheticCursorContent(); _cursorClockForSelfTest = 0;
            SyncCursorArt();
            Require(CursorShownForSelfTest == (AreaCursorArt.Gauntlet, 0) && CursorShapeSetForSelfTest == Godot.Input.CursorShape.Arrow,
                $"the gauntlet out of combat mode: {CursorShownForSelfTest}");
            foreach (var (_, ground) in near.Where(c => AreaHover.Ground(Routes(), actor, c.Cell) == AreaHoverAction.Walk).Take(1))
            {
                HoverAtForSelfTest(ground); SyncCursorArt();
                Require(HoverCursorForSelfTest == Godot.Input.CursorShape.Cross && CursorShownForSelfTest == (AreaCursorArt.Gauntlet, 0)
                    && CursorShapeSetForSelfTest == Godot.Input.CursorShape.Arrow, "a walk target keeps the gauntlet");
            }
            ToggleWorldSettings(); SyncCursorArt();
            Require(WorldModal && CursorArtForSelfTest == AreaCursorArt.Gauntlet, "a window shows the gauntlet");
            ToggleWorldSettings(); OpenHoverGate(steps);
            _cursorContentForSelfTest = null; _cursorClockForSelfTest = null;
            SyncCursorArt();
            Require(CursorShownForSelfTest is null && CursorShapeSetForSelfTest == HoverCursorForSelfTest, "the system shapes come back without the art");

            // A native control (HUD, palette) under the pointer takes it: no tooltip, default cursor.
            point = await HoverPointAsync(host, body) ?? throw new InvalidOperationException("AREA hover: body not visible again");
            HoverAtForSelfTest(point, overGui: true);
            Require(HoverActionForSelfTest == AreaHoverAction.None && HoverTextForSelfTest is null && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "GUI under the pointer");

            // The palette is modal: no tooltip, default cursor. Closing it quarantines the click guards: name only.
            ToggleWorldSettings();
            Require(WorldModal, "palette open");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.None && HoverTextForSelfTest is null && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "modal palette hides the hover");
            ToggleWorldSettings();
            Require(!WorldModal && !_worldGate.IsOpen, "closed palette quarantines");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.None && HoverTextForSelfTest == name && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "closed gate: name, no action");
            OpenHoverGate(steps);
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == expected, "the open gate brings the action back");

            // Focus loss and the pointer leaving the window hide it.
            _worldFocused = false; HoverAtForSelfTest(point);
            Require(HoverTextForSelfTest is null && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "unfocused window");
            _worldFocused = true; HoverAtForSelfTest(null);
            Require(HoverTextForSelfTest is null && HoverActionForSelfTest == AreaHoverAction.None && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "pointer outside the window");

            // An injected catalog replaces the fallback: the row's own description number gets an invented text.
            var row = index.Row(body.Identity!.Value);
            var probe = Arcanum.Data.Messages.ObjectDisplayName.Resolve(row, Arcanum.Data.Messages.ObjectDescriptionCatalog.Create(
                Arcanum.Data.Messages.MesFile.Parse("{1}{x}"u8).File!), Arcanum.Data.Messages.CritterRecognition.NotRecognized);
            Require(probe.Number is >= 0, $"synthetic row has a description number ({probe.Status} {probe.Number})");
            HoverNamesForSelfTest(Arcanum.Data.Messages.ObjectDescriptionCatalog.Create(
                Arcanum.Data.Messages.MesFile.Parse(System.Text.Encoding.ASCII.GetBytes($"{{{probe.Number}}}{{Synthetic Hover Name}}")).File!));
            HoverAtForSelfTest(point);
            Require(HoverTextForSelfTest == AreaHover.Tooltip("Synthetic Hover Name", expected), "catalog name shown: " + HoverTextForSelfTest);
            return $"bodies {index.Bodies.Count}, body {expected}, ground Walk and NoRoute, modal/gate/focus/window guards, cursor art";
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (WorldModal) ToggleWorldSettings();
            ReleaseSimulatedPadForSelfTest();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    private AreaRouteSource Routes() => _worldActor.Session is AreaGridSession session
        ? new(session.Snapshot, session.Chart) : throw new InvalidOperationException("AREA hover: no area session");

    private void OpenHoverGate(Arcanum.Data.Traversal.AreaStepSnapshot steps)
    {
        // ARC-DEV-1251: the setup's wall-clock time moves on after the actor session is made (a slow or busy machine takes over a tenth of a second);
        // the pad selection's quarantine reads that clock, so the frames below must not be stamped before it. A pause here makes the self-test show it every run.
        if (SelfTestBootstrap) OS.DelayMsec(150);
        SelectSimulatedPadForSelfTest();
        var level = StickLevel(default);
        for (var i = 0; i < 3; i++) WorldFrameForSelfTest(new(-1, default), new[] { level }, .1);
        if (!_worldGate.IsOpen) throw new InvalidOperationException("AREA hover: neutral frames open the gate");
    }

    /// <summary>Frames the body, then returns a pixel (outside the HUD) where it is the frontmost pick.</summary>
    private async Task<Vector2?> HoverPointAsync(Node host, AreaDrawCommand body)
    {
        PublishWorldSelection();
        _camera!.ResetWorld(new Vec2(body.Destination.X + body.Destination.Width / 2d, body.Destination.Y + body.Destination.Height / 2d), WorldViewport, 1);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var size = GetViewport().GetVisibleRect().Size; var hud = _hud!.Rect;
        // ARC-DEV-089: only the body's own pixels can be its pick (the pick tests the body's alpha), so the scan covers the screen rectangle
        // of its destination, on the same two-pixel grid and in the same order as the whole viewport would, which gives the same first
        // point. A whole-viewport scan of a body that is not visible took over a minute.
        // The pick tests the body where it is drawn now (a walked, fallen or moved body is its moving command, not its T0 one), so the
        // rectangle is the union of the command given and the moving command of the same identity.
        var transform = AreaDrawItem.RasterTransform(GetViewport());
        var rects = new List<AreaDrawCommand> { body };
        if (AreaMovingCommands()?.FirstOrDefault(c => c.Identity == body.Identity) is { } drawn) rects.Add(drawn);
        var left = Math.Max(0, (int)Math.Floor(rects.Min(c => transform.Origin.X + transform.X.X * c.Destination.X)) - 2) & ~1;
        var top = Math.Max(0, (int)Math.Floor(rects.Min(c => transform.Origin.Y + transform.Y.Y * c.Destination.Y)) - 2) & ~1;
        var right = Math.Min((int)size.X, (int)Math.Ceiling(rects.Max(c => transform.Origin.X + transform.X.X * (c.Destination.X + c.Destination.Width))) + 2);
        var bottom = Math.Min((int)size.Y, (int)Math.Ceiling(rects.Max(c => transform.Origin.Y + transform.Y.Y * (c.Destination.Y + c.Destination.Height))) + 2);
        for (var y = top; y < bottom; y += 2)
        for (var x = left; x < right; x += 2)
        {
            var p = new Vector2(x + .5f, y + .5f);
            if (!hud.HasPoint(p) && AreaObjectAtScreen(p) == body.Identity) return p;
        }
        return null;
    }

    /// <summary>
    /// ARC-DEV-089: a standable cell next to <paramref name="targetCell"/> (orthogonally <paramref name="range"/> cells off, standable within
    /// one more) that is at least <paramref name="minDistance"/> and at most <paramref name="maxDistance"/> cells from it and is not its own
    /// cell; null when none is. The standable cell may lie one cell farther than <paramref name="range"/>, so a caller that needs a cell
    /// really next to the target (ARC-UI-1333: the attack preview at one cell has no approach step) passes <c>maxDistance: 1</c>.
    /// </summary>
    private CellCoord? AreaSmokeNearTarget(AreaMapRuntime map, CellCoord targetCell, int range, int minDistance = 1, int maxDistance = int.MaxValue)
    {
        foreach (var (dx, dy) in new[] { (range, 0), (-range, 0), (0, range), (0, -range), (range, range), (-range, -range) })
        {
            var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
            if (!map.Chart.Contains(near)) continue;
            CellCoord cell;
            try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
            var distance = Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y));
            if (distance >= minDistance && distance <= maxDistance) return cell;
        }
        return null;
    }

    /// <summary>
    /// ARC-DEV-089: whether a combat smoke can fight the body in this window. The stages first took the weakest NPC of the map, which
    /// on the Crash Site stands in the open; in a town it may be under a roof, walled in or off the screen. A body is usable when
    /// the hero can stand <paramref name="range"/> cells from it (and next to it when <paramref name="alsoAdjacent"/>), and, with the
    /// hero placed there, a pixel of it is the frontmost pick. The hero is left on that cell, the camera framed on it.
    /// </summary>
    private async Task<CellCoord?> AreaSmokeFightableStartAsync(AreaMapRuntime map, AreaDrawCommand body, CellCoord targetCell, int range, int minDistance, bool alsoAdjacent)
    {
        if (alsoAdjacent && AreaSmokeNearTarget(map, targetCell, 1, maxDistance: 1) is null) return null;
        var start = AreaSmokeNearTarget(map, targetCell, range, minDistance);
        if (start is null) return null;
        AreaSmokePlace(map, start.Value);
        _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
        PublishWorldSelection();
        return await HoverPointAsync(this, body) is null ? null : start;
    }

    /// <summary>A point of the cell's diamond with no pickable object over it, outside the HUD.</summary>
    private Vector2? GroundPoint(CellCoord cell)
    {
        var center = AreaCellPicking.Center(cell);
        foreach (var (dx, dy) in new[] { (0, 0), (-20, 0), (20, 0), (0, -10), (0, 10), (-10, -5), (10, 5), (-10, 5), (10, -5) })
        {
            var p = _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)(center.X + dx), (float)(center.Y + dy));
            if (!_hud!.Rect.HasPoint(p) && GetViewport().GetVisibleRect().HasPoint(p) && AreaObjectAtScreen(p) is null) return p;
        }
        return null;
    }
}

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-005 on the original exterior (native smoke): Virgil names his not-yet-met description and shows talk, the
    /// cave entrance shows use, click-through scenery is never picked, and a press does what the cursor promised.
    /// Prints counts and actions only; ARC_AREA_HOVER_CAPTURE saves in-app readbacks to the caller's ignored path.
    /// </summary>
    private async Task RunAreaHoverSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_HOVER_SMOKE " + why); }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
            Require(_hoverNames?.Status == AreaNamesStatus.Ready, "names " + _hoverNames?.Status + " " + _hoverNames?.Detail);
            var map = _areaTransitions!.Current;
            AreaSmokePlace(map, map.Chart.Spawn);
            var index = HoverIndex()!;
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_HOVER_CAPTURE");
            async Task Capture(string name)
            {
                if (string.IsNullOrWhiteSpace(capture)) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_HOVER_CAPTURE_FAILED");
            }

            // Virgil: the one dialog NPC.
            var speaker = _conversationHost!.Speakers.Single();
            var virgil = _readyArea!.Plan.Commands.Single(c => c.Identity == speaker.Row.Key);
            var point = await HoverPointAsync(this, virgil) ?? throw new InvalidOperationException("AREA_HOVER_SMOKE Virgil not visible");
            HoverAtForSelfTest(point);
            var actor = map.Chart.ToWorld(map.Chart.Spawn);
            var talk = AreaConversationHost.Distance(speaker, actor.X, actor.Y) < Arcanum.Simulation.Ai.AiExecutorRules.HeroTalkRange ? AreaHoverAction.Talk : AreaHoverAction.WalkToTalk;
            var unmet = Arcanum.Data.Messages.ObjectDisplayName.Resolve(speaker.Row, _hoverNames!.Catalog!, Arcanum.Data.Messages.CritterRecognition.NotRecognized);
            var unmetText = Arcanum.Data.Messages.MesText.Decode(unmet.Message!, AreaHoverIndex.TextProfile).Text;
            Require(HoverKeyForSelfTest == speaker.Row.Key && HoverActionForSelfTest == talk && HoverCursorForSelfTest == Godot.Input.CursorShape.Help, $"Virgil {HoverActionForSelfTest}");
            Require(HoverTextForSelfTest == AreaHover.Tooltip(unmetText, talk), "Virgil's tooltip is his not-yet-met description and the talk action");
            await Capture("virgil");
            AreaSmokeClick(point);
            if (talk == AreaHoverAction.Talk) AreaSmokeFramesUntilConversation();
            Require(talk == AreaHoverAction.Talk ? ConversationOpen : _heroRunsHost?.TalkTarget == TalkTargetOf(speaker), "the press did what the cursor showed");
            if (ConversationOpen) EndConversation();
            CancelTalkApproach("ARC-UI-005 smoke"); StopWorldRoute("ARC-UI-005 smoke");

            // The cave entrance: use within range 2, else walk to use.
            AreaSmokePlace(map, map.Chart.Spawn);
            var (_, trigger) = _areaTransitions.Triggers().Single();
            var entrance = _readyArea.Plan.Commands.Single(c => c.Identity == trigger.Key);
            point = await HoverPointAsync(this, entrance) ?? throw new InvalidOperationException("AREA_HOVER_SMOKE entrance not visible");
            HoverAtForSelfTest(point);
            var use = Math.Max(Math.Abs(trigger.WorldX!.Value - actor.X), Math.Abs(trigger.WorldY!.Value - actor.Y)) <= AreaTransitionCoordinator.UseRange
                ? AreaHoverAction.Use : AreaHoverAction.WalkToUse;
            Require(HoverKeyForSelfTest == trigger.Key && HoverActionForSelfTest == use && HoverCursorForSelfTest == Godot.Input.CursorShape.PointingHand, $"entrance {HoverActionForSelfTest}");
            var entranceName = Arcanum.Data.Messages.ObjectDisplayName.Resolve(trigger, _hoverNames.Catalog!, Arcanum.Data.Messages.CritterRecognition.NotRecognized);
            Require(entranceName.Message is { } entranceMessage && HoverTextForSelfTest == AreaHover.Tooltip(
                Arcanum.Data.Messages.MesText.Decode(entranceMessage, AreaHoverIndex.TextProfile).Text, use), "entrance tooltip names its description");
            await Capture("entrance");

            // Click-through scenery (trees, plants) is passed through: where its sprite is opaque, the pick is the frontmost
            // body behind it that is not click-through (scanned over the whole plan here, not the index), or nothing.
            var rows = _readyArea.Snapshot.Objects.ToDictionary(o => o.Key);
            var passed = 0; var behind = 0; var transform = AreaDrawItem.RasterTransform(GetViewport());
            AreaObjectKey? Expected(int x, int y)
            {
                for (var i = _readyArea.Plan.Commands.Count - 1; i >= 0; i--)
                {
                    var c = _readyArea.Plan.Commands[i];
                    if (c.Identity is { } k && !AreaHoverIndex.ClickThrough(rows[k]) && AreaAlphaAt(c, _readyArea.Frames, transform, x, y) != 0) return k;
                }
                return null;
            }
            foreach (var tree in _readyArea.Plan.Commands.Where(c => c.Identity is { } k && AreaHoverIndex.ClickThrough(rows[k])).Take(12))
            {
                PublishWorldSelection();
                _camera!.ResetWorld(new GamepadLab.Core.Vec2(tree.Destination.X + tree.Destination.Width / 2d, tree.Destination.Y + tree.Destination.Height / 2d), WorldViewport, 1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                transform = AreaDrawItem.RasterTransform(GetViewport());
                var size = GetViewport().GetVisibleRect().Size;
                var hits = 0;
                for (var y = 0; y < (int)size.Y && hits < 8; y += 3)
                for (var x = 0; x < (int)size.X && hits < 8; x += 3)
                {
                    if (AreaAlphaAt(tree, _readyArea.Frames, transform, x, y) == 0) continue;
                    hits++;
                    var picked = AreaObjectAtScreen(new(x + .5f, y + .5f));
                    Require(picked != tree.Identity && picked == Expected(x, y), "a click-through body is passed through");
                    if (picked is not null) behind++;
                }
                if (hits > 0) passed++;
            }
            Require(passed > 0 && behind > 0, $"click-through sprites observed {passed}, picks behind them {behind}");
            AreaSmokePlace(map, map.Chart.Spawn); PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            GD.Print("ARC-UI-005 native-smoke hover PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Names = _hoverNames.Status.ToString(), Pickable = index.Bodies.Count, Virgil = talk.ToString(), Entrance = use.ToString(),
                ClickThroughChecked = passed, PickedBehindClickThrough = behind, Parity = "NOT_VERIFIED (original cursor does not change per target)" }));
        }
        finally
        {
            HoverReleaseForSelfTest();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
