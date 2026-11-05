using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-051 native observation (map mode, after the talk smoke): the PC stands on a closed, unlocked door's tile and
// walks one step across its edge: the step opens the door in the session and the door is drawn on its open frame once the
// animation has run; a left press on the door then closes it (frame 0, drawn on its T0 command). When the map has a door
// the player cannot open (locked, jammed or held), a press next to it is refused and the door stays closed. NONE on a map
// without a door that opens. An open frame that a body drawn in front hides at every pixel cannot be pressed (ashbury
// haunted castle: a tall wall two tiles south of a door); such a door is closed again by a use and the next door is tried (the first eight).
public partial class TerrainViewerApp
{
    private async Task RunAreaPortalSmokeAsync()
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-WORLD-051 native-smoke portal NONE map={_readyMapName} (no session portals)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-051 portal smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PORTAL_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var passages = portals.Passages(session);
        (AreaPortal Portal, CellCoord From, CellCoord To)? Crossing(Func<AreaPortal, bool> accept, bool open)
            => Crossings(accept, open).Select(c => ((AreaPortal Portal, CellCoord From, CellCoord To)?)c).FirstOrDefault();
        IEnumerable<(AreaPortal Portal, CellCoord From, CellCoord To)> Crossings(Func<AreaPortal, bool> accept, bool open)
        {
            foreach (var portal in portals.All.Where(p => p.Art.Door && p.Frames.Count > 1 && !p.UnlockScript && accept(p)).OrderBy(p => p.Entity, StringComparer.Ordinal))
            {
                var from = map.Chart.ToChart(portal.Cell);
                if (!map.Chart.Contains(from) || !map.Steps.IsAdmitted(portal.Cell)) continue;
                foreach (var direction in new[] { NeighborDirection.XMinus, NeighborDirection.YPlus, NeighborDirection.XPlus, NeighborDirection.YMinus })
                {
                    var to = RoutePlanner.Neighbor(from, direction);
                    if (!map.Chart.Contains(to)) continue;
                    var step = grid.Assess(from, to, direction);
                    if (step.Portals.Contains(portal.Key) && step.Disposition == (open ? CardinalTraversalDisposition.Open : CardinalTraversalDisposition.Blocked)
                        && map.Steps.IsAdmitted(map.Chart.ToWorld(to))) { yield return (portal, from, to); break; }
                }
            }
        }
        var targets = Crossings(p => portals.Frame(session, p) == 0 && passages[p.Key] == AreaPortalPassage.Open, open: true).Take(MaxPortalSmokeDoors).ToArray();
        if (targets.Length == 0) { GD.Print($"ARC-WORLD-051 native-smoke portal NONE map={_readyMapName} portals={portals.All.Count}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var door = targets[0]; var frames = 0; var openFrame = 0; Vector2? openPoint = null; var hidden = new List<object>();
            foreach (var candidate in targets)
            {
                door = candidate;
                // A step through the closed door opens it.
                AreaSmokePlace(map, door.From);
                var route = WorldRoute((AreaGridSession)_worldActor.Session!) ?? throw new InvalidOperationException("AREA_ROUTE_REQUIRED");
                route.SetTarget(door.To, WorldTime);
                frames = 0;
                for (; frames < 200 && ((AreaGridSession)_worldActor.Session!).State is var state
                    && (state.Phase != MovementPhase.Settled || state.CommittedCell != door.To); frames++) AreaSmokeFrame();
                var walked = ((AreaGridSession)_worldActor.Session!).State.CommittedCell == door.To;
                Require(walked, $"the PC stepped through the door {door.Portal.Entity} to {map.Chart.ToWorld(door.To)} in {frames} frames: {_worldRouteDiagnostic}");
                openFrame = door.Portal.OpenFrame;
                Require(portals.Frame(session, door.Portal) == openFrame, $"the step opened the door: frame {portals.Frame(session, door.Portal)}, want {openFrame}");
                // The animation runs on host time: (frames in the range) × 1000 / fps ms.
                await ToSignal(GetTree().CreateTimer((3 * door.Portal.StepMs + 200) / 1000.0), SceneTreeTimer.SignalName.Timeout);
                var openCommand = await PortalDrawnAsync(door.Portal, openFrame);
                Require(openCommand is not null, $"the open door is drawn on frame {openFrame}: drawn {DrawnFrames(door.Portal)}");
                if ((openPoint = await TryPortalPointAsync(openCommand!)) is not null) break;
                // Hidden at every pixel by bodies drawn in front: close it again by a use and try the next door.
                var by = HidingBodies(openCommand!);
                hidden.Add(new { door.Portal.Entity, By = by });
                GD.Print($"ARC-WORLD-051 portal hidden entity={door.Portal.Entity} pc={map.Chart.ToWorld(door.To)} by={by}");
                UsePortal(door.Portal);
                Require(portals.Frame(session, door.Portal) == 0, $"a use closed the hidden door {door.Portal.Entity}: {_portalNotice}");
                await ToSignal(GetTree().CreateTimer((3 * door.Portal.StepMs + 200) / 1000.0), SceneTreeTimer.SignalName.Timeout);
            }
            Require(openPoint is not null, $"one of {targets.Length} doors shows a pixel of its open frame: hidden {System.Text.Json.JsonSerializer.Serialize(hidden)}");
            // A press on the open door closes it.
            _portalNotice = "";
            AreaSmokeClick(openPoint!.Value);
            Require(portals.Frame(session, door.Portal) == 0, $"a press closed the door {door.Portal.Entity}: {_portalNotice}");
            await ToSignal(GetTree().CreateTimer((3 * door.Portal.StepMs + 200) / 1000.0), SceneTreeTimer.SignalName.Timeout);
            var closed = await PortalDrawnAsync(door.Portal, door.Portal.Baseline.Frame);
            Require(closed is not null, $"the closed door is drawn on its T0 frame: drawn {DrawnFrames(door.Portal)}");
            // A door the player cannot open is refused and stays closed.
            object? refused = null;
            if (Crossing(p => portals.Frame(session, p) == 0 && passages[p.Key] is AreaPortalPassage.LeavingOnly or AreaPortalPassage.Blocked
                && session.AttemptOpen(portals.Openable(p), p.Baseline, false) != Arcanum.Simulation.Locks.OpenAttempt.Opens, open: false) is { } locked)
            {
                AreaSmokePlace(map, locked.To);
                var command = _readyArea!.Plan.Commands.Single(c => c.Identity == locked.Portal.Key);
                AreaSmokeClick(await PortalPointAsync(command));
                Require(portals.Frame(session, locked.Portal) == 0 && (_portalNotice.EndsWith(": Locked", StringComparison.Ordinal)
                    || _portalNotice.EndsWith(": Jammed", StringComparison.Ordinal) || _portalNotice.EndsWith(": MagicallyHeld", StringComparison.Ordinal)),
                    $"the locked door {locked.Portal.Entity} was refused: {_portalNotice}");
                refused = new { locked.Portal.Entity, Notice = _portalNotice, Flags = locked.Portal.Baseline.Flags };
                await RunAreaPickLockSmokeAsync(locked.Portal, command);
            }
            else GD.Print($"ARC-SKILLS-006 native-smoke pick NONE map={_readyMapName} (no door the player cannot open)");
            GD.Print("ARC-WORLD-051 native-smoke portal PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, door.Portal.Entity, From = map.Chart.ToWorld(door.From), To = map.Chart.ToWorld(door.To), door.Portal.Art.Rotation,
                OpenFrame = openFrame, door.Portal.StepMs, Frames = frames, Portals = portals.All.Count, Hidden = hidden, Refused = refused, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // ARC-SKILLS-006 native observation on the refused door when it is locked (the PC stands next to it): with lockpicks in the
    // PC's list (the halfling-male hero's kit), a press of the inventory's Pick a lock button arms them and the next press on the
    // door picks its lock through the production click route; the session's lock state follows the outcome. NONE without
    // lockpicks or when the refused door is jammed or held but not locked.
    private async Task RunAreaPickLockSmokeAsync(AreaPortal door, AreaDrawCommand command)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SKILLS-006 pick smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PICK_SMOKE_FAILED");
        }
        var session = _areaSession!;
        if (!session.QueryOpenable(_areaPortals!.Openable(door), door.Baseline).Locked)
        { GD.Print($"ARC-SKILLS-006 native-smoke pick NONE map={_readyMapName} (the refused door is not locked)"); return; }
        if (_inventoryHost is null || !InventoryView(null).Pc.Any(line => line.Lockpick))
        { GD.Print($"ARC-SKILLS-006 native-smoke pick NONE map={_readyMapName} (no lockpicks in the PC's list)"); return; }
        InventoryKey(Key.I);
        Require(InventoryOpen && _inventoryPanel!.PickButtonsForSelfTest.Count > 0, "the window lists a Pick a lock button");
        var listed = _inventoryPanel!.ViewForSelfTest!.Pc.First(line => line.Lockpick).Entity;
        _inventoryPanel.PickButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!InventoryOpen && _lockpickArmed == listed, "the button closed the window and armed the listed lockpicks: " + _portalNotice);
        // ARC-DEV-110 (MIG16 S3): the armed lockpicks are interface mode 16 (item use) over the base: the window closed through the stack
        // first, the spell cursor shows (SA41), and the stack agrees with every panel and target state.
        Require(TargetModeOpen(InterfaceMode.ItemTarget) && _interfaceWindows.Stack.Depth == 2 && TargetsMatchStack && WindowPanelsMatchStack
            && CursorMode == AreaCursorMode.SpellTarget, $"the lockpicks are mode 16 over the base: {StackText()}");
        // The closed window quarantined the world: neutral frames reopen the gate, as after any window.
        for (var i = 0; i < 3; i++) AreaSmokeFrame();
        Require(_worldGate.IsOpen && _lockpickArmed is not null, "the gate reopened with the lockpicks armed");
        var revision = session.Version.Revision;
        AreaSmokeClick(PortalCenterPoint(command));
        for (var i = 0; i < 300 && (_portalNotice.StartsWith("walking", StringComparison.Ordinal) || _portalNotice.EndsWith("after this step", StringComparison.Ordinal)); i++)
            AreaSmokeFrame();
        var state = session.QueryOpenable(_areaPortals.Openable(door), door.Baseline);
        var notice = _portalNotice;
        Require(notice.Contains("the lock opens") || notice.Contains("the pick failed") || notice.Contains("the lock jams"), "the press picked the lock: " + notice);
        Require(session.Version.Revision > revision && state.Locked == !notice.Contains("the lock opens") && _lockpickArmed is null,
            $"the session's lock state follows the pick: locked {state.Locked}, {notice}");
        Require(!TargetModeOpen(InterfaceMode.ItemTarget) && _interfaceWindows.Stack.Depth == 1 && TargetsMatchStack, $"the press left mode 16: {StackText()}");
        GD.Print("ARC-SKILLS-006 native-smoke pick PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, door.Entity, door.LockDifficulty, Notice = notice, Locked = state.Locked, Parity = "NOT_VERIFIED" }));
    }

    // A point of the command's destination rectangle the shared pick resolves to it: its centre, else a scan.
    private Vector2 PortalCenterPoint(AreaDrawCommand command)
    {
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size; var hud = _hud!.Rect;
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
            if (!hud.HasPoint(new(x + .5f, y + .5f)) && AreaAlphaAt(command, AreaFrames(), transform, x, y) != 0 && AreaObjectAtScreen(new(x + .5f, y + .5f)) == command.Identity)
                return new Vector2(x + .5f, y + .5f);
        throw new InvalidOperationException("AREA_PORTAL_NOT_VISIBLE");
    }

    // The visible command of the portal once it draws the frame, after a few published frames.
    private async Task<AreaDrawCommand?> PortalDrawnAsync(AreaPortal portal, int frame)
    {
        if (portal.Visual(frame) is not { } visual) return null;
        var key = AreaFrameKey.From(visual);
        var row = _readyArea!.Plan.Commands.Single(c => c.Identity == portal.Key);
        PublishWorldSelection();
        _camera!.ResetWorld(new GamepadLab.Core.Vec2(row.Destination.X + row.Destination.Width / 2d, row.Destination.Y + row.Destination.Height / 2d), WorldViewport, 1);
        for (var i = 0; i < 4; i++)
        {
            AreaSmokeFrame(); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (_canvas!.AreaVisibleCommands.FirstOrDefault(c => c.Identity == portal.Key && c.Frame == key) is { } drawn) return drawn;
        }
        return null;
    }

    // The doors the portal smoke opens at most before it gives up on finding one whose open frame shows.
    private const int MaxPortalSmokeDoors = 8;

    private async Task<Vector2> PortalPointAsync(AreaDrawCommand command)
        => await TryPortalPointAsync(command) ?? throw new InvalidOperationException("AREA_PORTAL_NOT_VISIBLE");

    // The bodies the shared pick resolves to on the command's opaque pixels outside the HUD, with their pixel counts.
    private string HidingBodies(AreaDrawCommand command)
    {
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size; var hud = _hud!.Rect;
        var hits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
            if (!hud.HasPoint(new(x + .5f, y + .5f)) && AreaAlphaAt(command, AreaFrames(), transform, x, y) != 0)
            {
                var picked = AreaObjectAtScreen(new(x + .5f, y + .5f)) is { } key
                    ? $"{key.Sector}:{key.SourceIndex}/class {_readyArea!.Plan.Commands.FirstOrDefault(c => c.Identity == key)?.ArtClass}" : "nothing";
                hits[picked] = hits.GetValueOrDefault(picked) + 1;
            }
        return string.Join(",", hits.Select(hit => $"{hit.Key}×{hit.Value}"));
    }

    // Frames the portal, then returns an opaque pixel of its drawn frame that the shared pick resolves to it, outside the HUD,
    // or null when none does.
    private async Task<Vector2?> TryPortalPointAsync(AreaDrawCommand command)
    {
        PublishWorldSelection();
        _camera!.ResetWorld(new GamepadLab.Core.Vec2(command.Destination.X + command.Destination.Width / 2d,
            command.Destination.Y + command.Destination.Height / 2d), WorldViewport, 1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size; var hud = _hud!.Rect;
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
            if (!hud.HasPoint(new(x + .5f, y + .5f)) && AreaAlphaAt(command, AreaFrames(), transform, x, y) != 0 && AreaObjectAtScreen(new(x + .5f, y + .5f)) == command.Identity)
                return new Vector2(x + .5f, y + .5f);
        return null;
    }

    private string DrawnFrames(AreaPortal portal) => string.Join(",", _canvas!.AreaVisibleCommands.Where(c => c.Identity == portal.Key)
        .Select(c => portal.Frames.FirstOrDefault(v => AreaFrameKey.From(v) == c.Frame) is { } v ? ((v.RawArt >> 14) & 31).ToString() : "?"));
}
