using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-2792 native observation (stage "npc-roof", only when named, map mode with --hero, AI on, the main map at --sector 1450,1323): the roof covers a body by the tile it stands on now (RF10), not by its T0 tile.
    // Farkhus (the shack's NPC, stored off in the map data and switched on by his heartbeat's toggle state, script 2791) stands inside the roofed shack at T0. The stage switches him on, lets the AI host walk him out of the
    // roof's reach and checks his body is drawn where he stands. The way back in is the unit test's (AreaRoofCoverTests).
    private async Task RunAreaNpcRoofSmokeAsync()
    {
        const string Farkhus = "oid:9cdb53294615901004b4cd2b68858ee292cd433c340d56622776bd8786b5765c";
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-2792 npc-roof smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_ROOF_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null || _inventoryHost is null || _canvas is null)
        { GD.Print($"ARC-WORLD-2792 native-smoke npc-roof NONE map={_readyMapName} (needs a hero, combat values and the map)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var grid = (AreaGridSession)_worldActor.Session!;
            var map = new AreaMapRuntime(_readyArea.Snapshot, grid.Snapshot, grid.Chart);
            if (!_inventoryHost.Maps.First(m => m.RuntimeMap == CurrentAreaMap).TryGetRow(Farkhus, out var row) || row.WorldX is not { } dataX || row.WorldY is not { } dataY)
            { GD.Print($"ARC-WORLD-2792 native-smoke npc-roof NONE map={_readyMapName} (Farkhus is not on this map's window)"); return; }
            var chartCell = grid.Chart.ToChart(new AreaStepCell(dataX + 5, dataY + 6));
            AreaSmokePlace(map, new CellCoord(checked((int)chartCell.X), checked((int)chartCell.Y)));
            var host = AreaAiHostNow();
            if (host is null) { GD.Print("ARC-WORLD-2792 native-smoke npc-roof NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
            TickAreaClock(hostMs);
            void Frames(int count)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
            }
            Frames(40);
            host = AreaAiHostNow()!;
            var data = new CellCoord(checked((int)dataX), checked((int)dataY));
            var key = row.Key;
            Require(host.Scene.Npcs.Any(n => n.Entity == Farkhus), "Farkhus is an NPC of the AI host");
            // The premise: his T0 tile is under the shack's roof.
            Require(_canvas.AreaRoofCovers(key), $"the roof covers Farkhus at his data tile {data.X},{data.Y}");
            Require(SwitchOnStoredOffNpcs(session, host, e => e == Farkhus) == 1, "Farkhus was switched on as his heartbeat's toggle does");
            Frames(5); host = AreaAiHostNow()!;
            async Task<bool> DrawnAt(CellCoord cell)
            {
                for (var f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                return _canvas.AreaVisibleCommands.Any(c => c.Identity == key && c.MapX == cell.X && c.MapY == cell.Y);
            }
            async Task<(bool Arrived, bool Covered, bool Drawn)> Walk(CellCoord to)
            {
                var order = host.OrderWalk(Farkhus, to);
                Require(order is "started" or "retargeted" or "already walking there", $"the walk order to {to.X},{to.Y}: {order}");
                for (var frame = 0; frame < 900; frame++)
                {
                    Frames(1);
                    if (host.Tile(Farkhus) == to && host.RunOf(Farkhus) is null) break;
                }
                Frames(2);
                var arrived = host.Tile(Farkhus) == to;
                return (arrived, _canvas.AreaRoofCovers(key), await DrawnAt(to));
            }
            // Out: the cell beside the hero, outdoors. The AI host's own routine may have started him walking already (his heartbeat sets it up); the order retargets it.
            var outside = new CellCoord(checked((int)dataX) + 6, checked((int)dataY) + 4);
            var outResult = await Walk(outside);
            GD.Print($"ARC-WORLD-2792 npc-roof out arrived={outResult.Arrived} covered={outResult.Covered} drawn={outResult.Drawn}");
            Require(outResult.Arrived, $"Farkhus walked out to {outside.X},{outside.Y}");
            Require(!outResult.Covered, "the roof does not cover Farkhus once he stands outdoors");
            Require(outResult.Drawn, "Farkhus's body is drawn where he stands outdoors");
            GD.Print("ARC-WORLD-2792 native-smoke npc-roof PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Npc = Farkhus }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
        await Task.CompletedTask;
    }
}
