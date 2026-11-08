using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

// ARC-DEV-287 (MIG13 tail T6): the hero pushed by a knockback (goal 43, goal-table.md Tail T6). The original moves a player character through the ordinary move 0x43E770 and the location change 0x444270: the
// location is written tile by tile, the tile's jump point and tile script run (a trap does not: it is inside the walk step), the facing and the sneak are untouched, the walk ends (the table's level-5 interrupt
// ended it before the first tile) and the view is not centred on the hero. Here the grid session relocates the actor onto each tile of the line (AreaGridSession.Relocate), the session learns the place at once,
// the tile's entry is queued for the frame's map-transfer step (the same queue as a walked step's), and the camera stays where it is until the hero's next step (TerrainCameraController.HoldWorld).
public partial class TerrainViewerApp
{
    /// <summary>The hero's body for the knockback host (the AI scene's <see cref="AreaAiScene.HeroPush"/>).</summary>
    private sealed class ViewerHeroPush(TerrainViewerApp app) : IAreaHeroPush
    {
        public int Rotation => app._worldActor.State.Facing;

        public bool Slide(CellCoord tile) => app.PushHeroOnto(tile);
    }

    /// <summary>Relocates the hero onto the world tile <paramref name="tile"/> (a pushed step): the grid actor, the session's place, the tile's entry events and the camera. False when the hero is not on a grid here or the grid refuses the tile.</summary>
    private bool PushHeroOnto(CellCoord tile)
    {
        if (_worldActor.Session is not AreaGridSession grid) return false;
        var arrival = new AreaStepCell(tile.X, tile.Y);
        var chart = grid.Chart.ToChart(arrival);
        var left = grid.Chart.ToWorld(grid.State.CommittedCell);
        // A change of place that waits holds the hero's place (WriteHeroLocation): the push ends where the hero stands.
        if (AreaTransferPending || !_worldActor.Relocate(chart)) return false;
        WriteHeroLocation(grid, chart);
        // 0x444270 runs the jump point and the tile script of every tile the move enters (a map change among them); the walk step's trap run (0x430CA4) is not part of a push.
        if (_mapTransfers is not null) _mapEntered.Enqueue((left, arrival));
        _camera?.HoldWorld();
        return true;
    }
}
