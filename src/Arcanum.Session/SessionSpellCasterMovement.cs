using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-036 (magic audit MA-5, owner decision D3b of 2026-11-11: tile movement first; the executable read MV1–MV3, ledger spell-runs): the Movement component (type 15, 0x4524C0) of Spatial Distortion (3), type 0
/// of its four <c>Move_Location:</c> values. The component needs a current target object (the object the per-target stage handed it: Spatial Distortion's area is <c>Tgt_Self</c>, so the caster) and a run
/// whose resistance margin (+0x144) is 0; its location is the run's **target tile** (+0xE0), and a tile of (0, 0) moves nothing. The move is the immediate path relocation 0x455350: a route from the object's tile
/// to the target tile asked from the path request, at most eight tiles of it walked, the tile scripts of the tiles walked run, the object's runs interrupted and the object placed on the last tile
/// (<see cref="Arcanum.Simulation.Navigation.ImmediatePathWalk"/>, the host's: it needs the map's paths and the tile scripts). This host logs the request (<see cref="SpellCastEventKind.Movement"/>), as the
/// knockback and the hover do.
/// <para>Left: the types 1 (a random tile in a radius of the object, 0x4F4E40), 2 (the teleport tile, with the anti-teleport zones, the change of place and the followers: MA-5's second half) and 3 (the area
/// portal, record 218 of the technologies) stay not ready; so does a target that is no critter the host knows.</para>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The Movement component on its target object (<c>0x4524C0</c> case 0): a critter the host knows (the hero, or a critter of the map) and the run's target tile.</summary>
    [OriginalFunction(0x4524C0, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? MovementComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Movement is not { Location: SpellComponentTables.TargetTile }) return null;
        // No current target object (0x5E6D48 and 0x5E6D4C both 0): the component does nothing, and it is not "not ready".
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        // A resisted run (the margin, +0x144, written by the per-target stage) moves nothing, and a target tile of (0, 0) is no tile (the original tests the pair for zero).
        if (run.Margin != 0 || run.TargetTile is { X: 0, Y: 0 }) return SpellComponentOutcome.None;
        if (obj.Id != Hero && !CritterKnown(obj.Id)) return null;
        _log.Add(new(SpellCastEventKind.Movement, run.Id, unchecked((int)run.TargetTile.X), obj.Id, unchecked((int)run.TargetTile.Y)));
        return SpellComponentOutcome.None;
    }
}
