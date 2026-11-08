using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>ARC-DEV-141 (MIG13 S8c): the shown map's NPCs as pushed bodies for the knockback host: the AI host's movers, step test and damage step. The hero, a companion and the map's objects cannot be pushed yet
/// (the hero's place is the world actor's, S10; a companion's is the followers'), so a start on them is refused.</summary>
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
    public void Slide(string target, CellCoord tile, int ms, long nowMs) => host()?.PushSlide(target, tile, ms, nowMs);

    /// <inheritdoc />
    public void Bump(string target, int maximum) => host()?.PushBump(target, maximum);
}
