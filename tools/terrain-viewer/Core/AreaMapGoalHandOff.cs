using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What a walk follows in a new window and the goal it hands on (null when the window holds the goal).</summary>
public sealed record AreaMapGoalResume(CellCoord Target, AreaStepCell? FarGoal);

/// <summary>
/// ARC-WORLD-042: a route goal carried across a window move of a streamed map. A goal the new window holds is followed as
/// it is; a goal outside it is approached through the end of a partial <see cref="RouteCorridor"/> over the new window
/// (the cell it reaches nearest the goal), and the goal is handed on to the next move. Null: no cell of the new window is
/// nearer the goal (the walk ends). Project policy, parity NOT_VERIFIED.
/// </summary>
public static class AreaMapGoalHandOff
{
    public static AreaMapGoalResume? Resume(IRouteStepSource routes, AreaChart chart, CellCoord from, AreaStepCell goal)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(chart);
        var target = chart.ToChart(goal);
        if (chart.Contains(target)) return new(target, null);
        return RouteCorridor.Plan(routes, from, target) is { Outcome: RouteCorridorOutcome.Partial, End: { } end } ? new(end, goal) : null;
    }
}
