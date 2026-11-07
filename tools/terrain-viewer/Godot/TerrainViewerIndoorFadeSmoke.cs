using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-203 (B45, SM18) native observation in map mode: the hero vaults out of a window from an indoor tile onto an outdoor one, and his shade
// (the tile class under his placement, FR-LIGHT-006) must follow the committed cell frame by frame. The original places an object on the tile its anchor
// point lies in (location_normalize 0x4B9420: half way along a step), so the hero takes the outdoor shade in the frame that commits the far cell, not
// when the vault ends (towards +X/+Y) or when it starts (towards -X/-Y), which the placement's floor of the position did before. One window towards
// +X/+Y and one towards -X/-Y are tried (each when the map has an open indoor-to-outdoor window step reachable from the spawn; a missing kind is reported, not failed).
public partial class TerrainViewerApp
{
    private const int IndoorFadeSearchLimit = 400_000;

    private async Task RunIndoorFadeSmokeAsync(TerrainLoadResult result)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-203 indoor-fade smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_INDOOR_FADE_SMOKE_FAILED");
        }
        var area = result.Area!; var steps = result.AreaStepSnapshot!; var chart = area.Plan.Chart;
        var map = new AreaMapRuntime(area.Snapshot, steps, chart);
        var tileArt = area.Snapshot.Terrain.Where(tile => tile.Scope == AreaScope.Core).ToDictionary(tile => (tile.WorldX, tile.WorldY), tile => tile.RawArt);
        AreaAmbientChannel Channel(CellCoord cell)
        {
            var world = chart.ToWorld(cell);
            return tileArt.TryGetValue((world.X, world.Y), out var art) ? AreaAmbientPolicy.Channel(art) : AreaAmbientChannel.Unlit;
        }
        var probe = new AreaGridSession(steps, SimulationTime.At(0), chart: chart, start: chart.Spawn);
        (NeighborDirection Direction, int X, int Y)[] moves =
            [(NeighborDirection.XPlus, 1, 0), (NeighborDirection.XMinus, -1, 0), (NeighborDirection.YPlus, 0, 1), (NeighborDirection.YMinus, 0, -1)];
        var seen = new HashSet<CellCoord> { chart.Spawn }; var queue = new Queue<CellCoord>([chart.Spawn]);
        (CellCoord From, NeighborDirection Direction, CellCoord To)? towardsPlus = null, towardsMinus = null;
        while (queue.TryDequeue(out var cell) && seen.Count < IndoorFadeSearchLimit && (towardsPlus is null || towardsMinus is null))
            foreach (var (direction, dx, dy) in moves)
            {
                var next = new CellCoord(cell.X + dx, cell.Y + dy);
                if (next.X < chart.MinimumCell || next.X > chart.MaximumCell || next.Y < chart.MinimumCell || next.Y > chart.MaximumCell) continue;
                var assessment = probe.Assess(cell, next, direction);
                if (assessment.Disposition != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
                if (assessment.WindowJump is not null && Channel(cell) == AreaAmbientChannel.Indoor && Channel(next) == AreaAmbientChannel.Outdoor)
                {
                    if (dx + dy > 0) towardsPlus ??= (cell, direction, next); else towardsMinus ??= (cell, direction, next);
                    continue;
                }
                if (seen.Add(next)) queue.Enqueue(next);
            }
        var windows = new[] { towardsPlus, towardsMinus }.Where(w => w is not null).Select(w => w!.Value).ToArray();
        if (windows.Length == 0)
        {
            GD.Print($"ARC-DEV-203 native-smoke indoor-fade NO-WINDOW map={_readyMapName} searched={seen.Count}");
            return;
        }
        var report = new List<object>();
        foreach (var jump in windows)
        {
            var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
            _worldFocusSimulationForSelfTest = true;
            var events = new List<MovementEvent>();
            var frames = new List<(int Frame, CellCoord Committed, (long X, long Y) Placed, AreaAmbientChannel Shade)>();
            try
            {
                _worldFocused = true; SelectSimulatedPadForSelfTest();
                AreaSmokePlace(map, jump.From);
                _worldAdvanceObserverForSelfTest = advance => events.AddRange(advance.Movement.Events);
                PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Key[] keys = jump.Direction switch
                {
                    NeighborDirection.XPlus => [Key.S, Key.A], NeighborDirection.XMinus => [Key.W, Key.D],
                    NeighborDirection.YPlus => [Key.S, Key.D], _ => [Key.W, Key.A],
                };
                _areaSmokeTime *= 10;
                var hostMs = 34_000_000L;
                TickAreaClock(hostMs);
                var count = 0;
                void Frame()
                {
                    _areaSmokeTime++; count++;
                    TickAreaClock(hostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { SimulatedPadLevel() }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                    var state = ((AreaGridSession)_worldActor.Session!).State; var placement = _worldActor.Placement();
                    frames.Add((count, state.CommittedCell, (placement.MapX!.Value, placement.MapY!.Value), Channel(new CellCoord(checked((int)placement.MapX.Value), checked((int)placement.MapY.Value)))));
                }
                _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
                foreach (var key in keys) SimulatedSectorKey(key, true);
                for (var i = 0; i < 9; i++) Frame();
                foreach (var key in keys) SimulatedSectorKey(key, false);
                for (var i = 0; i < 60 && !(((AreaGridSession)_worldActor.Session!).State.Phase == MovementPhase.Settled && events.Any(e => e.Kind == MovementEventKind.CenterArrived)); i++) Frame();
                var permit = events.FirstOrDefault(e => e.Kind == MovementEventKind.StepStarted)?.Assessment;
                Require(permit is not null && permit.Key.From == jump.From && permit.Key.To == jump.To, "the stick started the window step: " + permit?.Key);
                Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == jump.To, "landed on the far side");
                // The placement tile is the committed cell in every frame (the tile the original's location is on).
                foreach (var f in frames)
                    Require((f.Committed.X, f.Committed.Y) == f.Placed, $"frame {f.Frame}: placement {f.Placed} is the committed cell {f.Committed}");
                var committedFrame = frames.First(f => f.Committed == jump.To).Frame;
                var outdoorFrame = frames.First(f => f.Shade == AreaAmbientChannel.Outdoor).Frame;
                var indoorBefore = frames.TakeWhile(f => f.Committed != jump.To).All(f => f.Shade == AreaAmbientChannel.Indoor);
                Require(outdoorFrame == committedFrame, $"the outdoor shade starts in the frame that commits the far cell: outdoor {outdoorFrame}, commit {committedFrame}");
                Require(indoorBefore, "the shade stays indoor until the far cell commits");
                // The vault takes 1.125 s; the far cell commits half way (0.5625 s), the table starts the step one frame after the press: by the ninth 0.1 s frame (the whole vault is 13).
                Require(committedFrame <= 9, $"the far cell commits half way along the vault: frame {committedFrame}");
                report.Add(new { jump.Direction, From = chart.ToWorld(jump.From), To = chart.ToWorld(jump.To), CommitFrame = committedFrame, Frames = frames.Count });
            }
            finally
            {
                _worldAdvanceObserverForSelfTest = null;
                _areaSmokeTime = (_areaSmokeTime + 9) / 10;
                ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
                _worldFocused = focused; RestoreWorldTestTime(testTime);
                if (_worldActor.Session is AreaGridSession walked) ConfigureAreaActor(map, walked.State.CommittedCell, walked.State.Facing);
            }
        }
        GD.Print("ARC-DEV-203 native-smoke indoor-fade PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Windows = report }));
    }
}
