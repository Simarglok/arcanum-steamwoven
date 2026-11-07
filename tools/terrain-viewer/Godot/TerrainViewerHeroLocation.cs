using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-150 (MIG13 S10b-2c): the hero's location is the session's. The original writes the hero's location while a step is under way, when
// the step's pixel offset enters the next tile (0x4305D0); here the movement runtime commits the cell at the same moment (its CellCommitted
// event), and the viewer writes the committed cell and the facing into the session at the end of every advance of the hero's grid, before the
// cell's entry events (traps, map entries, portals) look at the hero. The former commit points (a drop, an attack, a throw, a projectile's strike,
// combat mode, the round's calls) are retired: every reader of the hero's place asks the session. A map change is never a step's write (it is the
// change of place's: ApplyChanges), and a map transfer that waits holds the write.
public partial class TerrainViewerApp
{
    /// <summary>Whether the session has a hero place to read: a hero on a grid and a session to hold it.</summary>
    private bool HeroLocationKnown() => _worldActor.Session is AreaGridSession && _areaSession is not null;

    /// <summary>Writes the hero's committed cell and facing after an advance of its grid. False when nothing could be written (the hero is not the
    /// shown grid's, a transfer waits, the session's actor stands on another map, or the session refused the placement).</summary>
    private bool WriteHeroLocation(IGridSession session)
    {
        if (session is not AreaGridSession grid || !ReferenceEquals(grid, _worldActor.Session) || AreaTransferPending) return false;
        return WriteHeroLocation(grid, grid.State.CommittedCell);
    }

    /// <summary>Writes <paramref name="cell"/> (a chart cell of <paramref name="grid"/>) and the grid's facing as the hero's location.</summary>
    private bool WriteHeroLocation(AreaGridSession grid, CellCoord cell)
    {
        if (_areaSession is not { } owner) return false;
        var world = grid.Chart.ToWorld(cell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)world.X), checked((int)world.Y)), grid.State.Facing);
        var held = owner.QueryActor();
        if (held == placement) return true;
        if (held.Map != placement.Map) return false;
        var placed = owner.PlaceActor(owner.Version, placement);
        if (placed.Disposition == GameSessionDisposition.Rejected) { GD.Print($"ARC-DEV-150 hero-location refused failure={placed.Failure}"); return false; }
        return true;
    }
}
