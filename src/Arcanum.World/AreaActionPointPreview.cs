using Arcanum.Simulation;
using Arcanum.Simulation.Combat;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-1114 (ledger CH4 and CH14): the action-point cost the combat interface previews for the action under the pointer. In a
/// turn-based round the original's hover handler (0x557370) runs the action-point check of the action the cursor would start,
/// which stores its cost as the pending one the lamps show: moving to a tile (0x4B7830), using an object (0x4B79A0), picking an
/// item up (0x4B7A20), attacking a critter (0x4B78D0), casting (0x4B7AA0). The viewer's hover actions stand for the original's
/// pointer, object-type and key tests. Distances are straight-line cells (the original asks its pathfinder for the steps of an
/// approach: the preview here does not search, the charge of each step stays exact).
/// </summary>
public static class AreaActionPointPreview
{
    /// <summary>
    /// The cost the action under the pointer previews, or null when it runs no action-point check (nothing under the pointer, no
    /// route, a talk).
    /// </summary>
    /// <param name="action">The viewer's hover action.</param>
    /// <param name="distance">The cells between the hero and the target (Chebyshev), at least 0.</param>
    /// <param name="attackCost">The cost of one attack with the weapon in hand (<see cref="TurnBasedRules.AttackCost"/>).</param>
    /// <param name="weaponRange">The weapon's reach in cells; an attacker that reaches one cell or less first walks to an adjacent cell.</param>
    /// <param name="alwaysRun">The PC always runs (a step costs 1 instead of 2).</param>
    [OriginalFunction(0x557370, OriginalFunctionScope.Partial)]
    public static int? Cost(AreaHoverAction action, int distance, int attackCost, int weaponRange, bool alwaysRun)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distance);
        ArgumentOutOfRangeException.ThrowIfNegative(attackCost);
        // A bigger map than the path estimate's bound counts as the bound.
        distance = Math.Min(distance, TurnBasedRules.MaxPathSteps);
        // The steps to a cell next to the target: none when already adjacent or on it.
        var adjacentSteps = Math.Max(0, distance - 1);
        return action switch
        {
            AreaHoverAction.Walk => TurnBasedRules.MoveCost(distance, alwaysRun),
            AreaHoverAction.Use or AreaHoverAction.WalkToUse or AreaHoverAction.Loot or AreaHoverAction.WalkToLoot =>
                TurnBasedRules.UseObjectActionCost(distance == 0, adjacentSteps, alwaysRun),
            AreaHoverAction.PickUp or AreaHoverAction.WalkToPickUp => TurnBasedRules.PickUpActionCost(distance == 0, distance, alwaysRun),
            AreaHoverAction.Attack or AreaHoverAction.WalkToAttack =>
                TurnBasedRules.AttackActionCost(attackCost, weaponRange <= 1, distance, adjacentSteps, alwaysRun),
            // The spell cursor's check (0x4B7AA0) runs for whatever is under it, whether the spell's area passes it or not.
            AreaHoverAction.Cast or AreaHoverAction.CastNoTarget => TurnBasedRules.CastSpellCost,
            _ => null,
        };
    }
}
