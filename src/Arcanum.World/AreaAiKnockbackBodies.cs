using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>ARC-DEV-141 (MIG13 S8c): the shown map's NPCs as pushed bodies for the knockback host: the AI host's movers, step test and damage step. Since ARC-DEV-285 a companion the host runs is pushed through the
/// followers' jump, and since ARC-DEV-287 the hero through the scene's hero body (<see cref="AreaAiScene.HeroPush"/>); the map's objects cannot be pushed yet, so a start on them is refused.</summary>
public sealed class AreaAiKnockbackBodies(Func<AreaAiHost?> host) : IAreaKnockbackViewer
{
    /// <inheritdoc />
    public CellCoord? Tile(string target) => host() is { } ai && ai.CanBePushed(target) ? ai.Tile(target) : null;

    /// <inheritdoc />
    public void IdleRestart(string target, GoalHandle ending) => host()?.IdleRestart(target, ending);

    /// <inheritdoc />
    public int Rotation(string target) => host()?.PushRotation(target) ?? 0;

    /// <inheritdoc />
    public bool Contains(CellCoord tile) => host()?.OnMap(tile) == true;

    /// <inheritdoc />
    public KnockbackBlock Block(string target, CellCoord left, CellCoord tested) =>
        host()?.PushBlock(target, left, tested) ?? KnockbackBlock.Terrain;

    /// <inheritdoc />
    public bool Slide(string target, CellCoord tile, int ms, long nowMs) => host()?.PushSlide(target, tile, ms, nowMs) == true;

    /// <inheritdoc />
    public void Bump(string target, int maximum) => host()?.PushBump(target, maximum);
}
