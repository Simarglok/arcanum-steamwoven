using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>ARC-DEV-141 (MIG13 S8c): the shown map's NPCs as pushed bodies for the knockback host: the AI host's movers, step test and damage step. Since ARC-DEV-285 a companion the host runs is pushed through the
/// followers' jump, and since ARC-DEV-287 the hero through the scene's hero body (<see cref="AreaAiScene.HeroPush"/>); since ARC-MAGIC-1230 an item lying on the ground is pushed through <paramref name="items"/> (UO5); scenery, portals and containers
/// are not pushed (the spell's target test never names them), so a start on one is refused.</summary>
public sealed class AreaAiKnockbackBodies(Func<AreaAiHost?> host, AreaKnockbackItems? items = null) : IAreaKnockbackViewer
{
    // An id the AI host runs as an NPC, a companion or the hero is theirs; any other that lies on the ground is an item.
    private bool Item(string target) => items is not null && !(host()?.CanBePushed(target) ?? false) && items.IsItem(target);

    /// <inheritdoc />
    public CellCoord? Tile(string target) => host() is { } ai && ai.CanBePushed(target) ? ai.Tile(target) : items?.Tile(target);

    /// <inheritdoc />
    public void IdleRestart(string target, GoalHandle ending)
    {
        if (!Item(target)) host()?.IdleRestart(target, ending);
    }

    /// <inheritdoc />
    public int Rotation(string target) => Item(target) ? 0 : host()?.PushRotation(target) ?? 0;

    /// <inheritdoc />
    public bool Contains(CellCoord tile) => host()?.OnMap(tile) == true;

    /// <inheritdoc />
    public KnockbackBlock Block(string target, CellCoord left, CellCoord tested) =>
        Item(target) ? items!.Block(target, left, tested) : host()?.PushBlock(target, left, tested) ?? KnockbackBlock.Terrain;

    /// <inheritdoc />
    public bool Slide(string target, CellCoord tile, int ms, long nowMs) => Item(target) ? items!.Slide(target, tile) : host()?.PushSlide(target, tile, ms, nowMs) == true;

    /// <inheritdoc />
    public void Bump(string target, int maximum)
    {
        if (Item(target)) items!.Bump(target, maximum);
        else host()?.PushBump(target, maximum);
    }
}
