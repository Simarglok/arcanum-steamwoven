using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-021: native observation of a window jump in map mode. When the map has an Open window step reachable
// from its spawn over the whole-map step evidence, the actor stands on its near side and the stick steps across:
// the step takes the vault time, the vault ART is drawn mid-jump, and the actor ends on the far side. A map without
// such a window reports that and passes nothing.
public partial class TerrainViewerApp
{
    private const int AreaMapVaultSearchLimit = 200_000;

    private async Task RunAreaMapVaultSmokeAsync(TerrainLoadResult result)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-021 vault smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_VAULT_SMOKE_FAILED");
        }
        var area = result.Area!; var steps = result.AreaStepSnapshot!; var chart = area.Plan.Chart;
        var map = new AreaMapRuntime(area.Snapshot, steps, chart);
        var probe = new AreaGridSession(steps, SimulationTime.At(0), chart: chart, start: chart.Spawn);
        // Breadth-first over open axis steps from the spawn: the first Open window step found.
        (NeighborDirection Direction, int X, int Y)[] moves =
            [(NeighborDirection.XPlus, 1, 0), (NeighborDirection.XMinus, -1, 0), (NeighborDirection.YPlus, 0, 1), (NeighborDirection.YMinus, 0, -1)];
        var seen = new HashSet<CellCoord> { chart.Spawn }; var queue = new Queue<CellCoord>([chart.Spawn]);
        (CellCoord From, NeighborDirection Direction, CellCoord To)? window = null;
        while (window is null && queue.TryDequeue(out var cell) && seen.Count < AreaMapVaultSearchLimit)
            foreach (var (direction, dx, dy) in moves)
            {
                var next = new CellCoord(cell.X + dx, cell.Y + dy);
                if (next.X < chart.MinimumCell || next.X > chart.MaximumCell || next.Y < chart.MinimumCell || next.Y > chart.MaximumCell) continue;
                var assessment = probe.Assess(cell, next, direction);
                if (assessment.Disposition != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
                if (assessment.WindowJump is not null) { window = (cell, direction, next); break; }
                if (seen.Add(next)) queue.Enqueue(next);
            }
        if (window is not { } jump)
        {
            GD.Print($"ARC-WORLD-021 native-smoke vault NO-WINDOW map={_readyMapName} searched={seen.Count}");
            return;
        }

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var events = new List<MovementEvent>();
        var vaultDrawn = false; var frames = 0;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            AreaSmokePlace(map, jump.From);
            _worldAdvanceObserverForSelfTest = advance => events.AddRange(advance.Movement.Events);
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Screen sectors of the four axis steps (ARC-WORLD-003 stick table): S+A = +X, W+D = −X, S+D = +Y, W+A = −Y.
            Key[] keys = jump.Direction switch
            {
                NeighborDirection.XPlus => [Key.S, Key.A], NeighborDirection.XMinus => [Key.W, Key.D],
                NeighborDirection.YPlus => [Key.S, Key.D], _ => [Key.W, Key.A],
            };
            _areaSmokeTime *= 10;
            // ARC-DEV-147: the stick is a held walk of the goal table, which the area clock steps (0.1 s a frame here).
            var vaultHostMs = 34_000_000L;
            TickAreaClock(vaultHostMs);
            void Frame()
            {
                _areaSmokeTime++; frames++;
                TickAreaClock(vaultHostMs += 100);
                WorldFrameForSelfTest(new(-1, default), new[] { SimulatedPadLevel() }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            string? capture = System.Environment.GetEnvironmentVariable("ARC_AREA_VAULT_CAPTURE");
            // The first frame that draws the vault ART: optionally saved from the real viewport.
            async Task Sample()
            {
                if (vaultDrawn || _worldActor.State.Action != ActorDiagnosticAction.Vault || _readyActorPlan?.Current.TextureKey.Selector != _worldActor.Profile.Vault!.Selector) return;
                vaultDrawn = true;
                if (string.IsNullOrWhiteSpace(capture)) return;
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-021 vault smoke: capture failed");
            }
            // Hold the stick past the step's entry (half of 1.125 s), then release before it ends: one step, no return. The table gives the step one frame after the press (the first frame starts the run).
            foreach (var key in keys) SimulatedSectorKey(key, true);
            for (var i = 0; i < 9; i++) { Frame(); await Sample(); }
            foreach (var key in keys) SimulatedSectorKey(key, false);
            for (var i = 0; i < 60 && !(((AreaGridSession)_worldActor.Session!).State.Phase == MovementPhase.Settled && events.Any(e => e.Kind == MovementEventKind.CenterArrived)); i++)
            { Frame(); await Sample(); }
            var permit = events.FirstOrDefault(e => e.Kind == MovementEventKind.StepStarted)?.Assessment;
            Require(permit is not null && permit.Key.From == jump.From && permit.Key.To == jump.To, "the stick started the window step: " + permit?.Key);
            Require(permit!.Provenance.Contains(GaitTiming.VaultProvenance) && permit.Duration == _worldActor.AreaGaitTiming!.Vault, "the window step is a vault: " + string.Join(",", permit.Provenance));
            Require(vaultDrawn, "the vault ART was drawn mid-jump");
            var state = ((AreaGridSession)_worldActor.Session!).State;
            Require(state.CommittedCell == jump.To && state.Phase == MovementPhase.Settled, $"landed on the far side: {state.CommittedCell} {state.Phase}");
        }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            if (_worldActor.Session is AreaGridSession walked)
                ConfigureAreaActor(map, walked.State.CommittedCell, walked.State.Facing);
        }
        GD.Print("ARC-WORLD-021 native-smoke vault PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, From = chart.ToWorld(jump.From), To = chart.ToWorld(jump.To), Direction = jump.Direction.ToString(),
            Searched = seen.Count, Seconds = 1.125, Frames = frames, Parity = "NOT_VERIFIED (linear motion, research W4)" }));
    }
}
