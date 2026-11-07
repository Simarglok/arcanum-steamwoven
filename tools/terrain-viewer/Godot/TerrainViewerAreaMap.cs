using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-017: native observation of one whole interior (--area-map). The map is drawn at the actor's
// map-list arrival, a left click through the production ground-click seam sets a route goal some cells away
// over the whole-map step evidence, and frames walk the actor there on the map's own chart. The Crash Site
// smoke phases (pixel classes, cave, quick save, companions) belong to the exterior and do not run here.
public partial class TerrainViewerApp
{
    private const int AreaMapSmokeReach = 8;

    private async Task RunAreaMapSmokeAsync(TerrainLoadResult result)
    {
        static void Require(bool value, string code) { if (!value) throw new InvalidOperationException("AREA_MAP_SMOKE_" + code); }
        // The frontier line prints only a bare code; the detail goes to the log first.
        void Detail(string text) => GD.Print("ARC-RENDER-017 smoke detail " + text);
        var area = result.Area!;
        if (_areaAmbient is not null) { _areaAmbient.Enabled = false; PushAreaAmbient(); }
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Borderless = true; GetWindow().Unresizable = true;
        GetWindow().Position = Vector2I.Zero; GetWindow().Size = new(1280, 800);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (var image = GetViewport().GetTexture().GetImage())
            Require(image.GetWidth() == 1280 && image.GetHeight() == 800, "VIEWPORT");
        Require(_canvas!.AreaVisibleCount > 0, "NOTHING_DRAWN");
        var session = _worldActor.Session as AreaGridSession ?? throw new InvalidOperationException("AREA_MAP_SMOKE_NO_SESSION");
        var chart = area.Plan.Chart;
        Require(session.Chart == chart && session.State.CommittedCell == chart.Spawn, "SPAWN");
        var goal = AreaMapSmokeGoal(session) ?? throw new InvalidOperationException("AREA_MAP_SMOKE_NO_GOAL");

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var frames = 0;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // A fresh session on the spawn at test time 0, then neutral frames until the safety gate opens.
            AreaSmokePlace(new AreaMapRuntime(area.Snapshot, result.AreaStepSnapshot!, chart), chart.Spawn);
            session = (AreaGridSession)_worldActor.Session!;
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var center = AreaCellPicking.Center(goal.Cell);
            AreaSmokeClick(_canvas.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y));
            // ARC-DEV-149: the click starts the hero's walk run on the table (the route gets its steps from it), and the table steps while the area clock is ticked 0.1 s a frame.
            if (HeroWalkGoal != goal.Cell) Detail($"click {_worldRouteClick} route {_worldRouteDiagnostic} goal {goal.Cell}");
            Require(HeroWalkGoal == goal.Cell, "ROUTE_NOT_SET");
            var state = session.State;
            var walkHostMs = 35_000_000L;
            while (frames < 400 && !(state.Phase == MovementPhase.Settled && state.CommittedCell == goal.Cell))
            {
                if (ShownHeroRunsHost?.WalkRun is not null) TickAreaClock(walkHostMs += 100);
                AreaSmokeFrame(); frames++;
                state = ((AreaGridSession)_worldActor.Session!).State;
            }
            if (state.CommittedCell != goal.Cell) Detail($"at {state.CommittedCell} {state.Phase} route {_worldRouteDiagnostic} goal {goal.Cell}");
            Require(state.Phase == MovementPhase.Settled && state.CommittedCell == goal.Cell, "NOT_ARRIVED");
            // The session exists when the map's lighting gives it a clock (an unlit map has none, as in crash-site-area).
            Require(area.Lighting is null || _areaSession?.QueryActor().Map.RuntimeMap == area.Snapshot.RuntimeMap, "SESSION_MAP");
            _camera.ResetWorld(WorldAnchor, WorldViewport, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            // On the restored clock (the viewer's, or the walk's test time when it ran ahead): a fresh session where the actor
            // stands, so no later observation is late.
            if (_worldActor.Session is AreaGridSession walked)
                ConfigureAreaActor(new AreaMapRuntime(area.Snapshot, result.AreaStepSnapshot!, chart), walked.State.CommittedCell, walked.State.Facing);
        }
        RefreshAreaStatus();
        if (_nativeSmokeScreenshot is not null)
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_nativeSmokeScreenshot) != Error.Ok) throw new InvalidOperationException("AREA_CAPTURE_FAILED");
        }
        await RunAreaBlendSmokeAsync(area);
        _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("ARC-RENDER-017 native-smoke map PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, area.Snapshot.CatalogueMap, area.Snapshot.RuntimeMap, area.Snapshot.Profile,
            Sectors = area.Snapshot.Reconciliation.CoreSectors, area.Snapshot.Reconciliation.CoreTerrain, Bodies = area.Plan.DrawableBodies,
            Frames = area.Frames.Count, area.CpuRgbaBytes, area.ElapsedMilliseconds, Chart = new { chart.OriginX, chart.OriginY, chart.MinimumCell, chart.MaximumCell },
            Spawn = chart.ToWorld(chart.Spawn), Goal = chart.ToWorld(goal.Cell), goal.Steps, WalkFrames = frames,
            result.AreaStepSnapshot!.AdmittedCells, Lit = area.Lighting is not null, Walls = _areaWallCheck?.WallCount ?? 0,
            WallFadesAtGoal = _areaWallCheck?.State.Fades.Count ?? 0, Parity = "NOT_VERIFIED" }));
    }

    /// <summary>The cell farthest (by open steps, at most <see cref="AreaMapSmokeReach"/>) from the spawn over the
    /// session's own assessments, ties broken by row then column; null when the spawn has no open step.</summary>
    private static (CellCoord Cell, int Steps)? AreaMapSmokeGoal(AreaGridSession session)
    {
        var start = session.State.CommittedCell;
        var distance = new Dictionary<CellCoord, int> { [start] = 0 };
        var queue = new Queue<CellCoord>(); queue.Enqueue(start);
        (NeighborDirection Direction, int X, int Y)[] moves =
            [(NeighborDirection.XPlus, 1, 0), (NeighborDirection.XMinus, -1, 0), (NeighborDirection.YPlus, 0, 1), (NeighborDirection.YMinus, 0, -1)];
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (distance[cell] == AreaMapSmokeReach) continue;
            foreach (var (direction, dx, dy) in moves)
            {
                var next = new CellCoord(cell.X + dx, cell.Y + dy);
                if (distance.ContainsKey(next) || !session.Chart.Contains(next) || !session.Snapshot.IsAdmitted(session.Chart.ToWorld(next))) continue;
                var step = session.Assess(cell, next, direction);
                if (step.Coverage != Arcanum.Data.Traversal.CardinalTraversalCoverage.Supported
                    || step.Disposition != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
                distance[next] = distance[cell] + 1; queue.Enqueue(next);
            }
        }
        var best = distance.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X).ToArray();
        return best.Length == 0 ? null : (best[0].Key, best[0].Value);
    }
}
