using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Route evidence over the whole-core T0 step snapshot, in <see cref="AreaGridSession"/> chart cells. It asks the same
/// <see cref="AreaStepSnapshot.Assess"/> the session admits steps with, so a planned Open step is the step the runtime
/// receives. Only Supported Open is Open; Unknown and Unsupported (core edge) stay distinct from Blocked. With an
/// <paramref name="occupancy"/> provider (ARC-WORLD-016) every step gets the same live overlay the session admits it with.
/// </summary>
public sealed class AreaRouteSource(AreaStepSnapshot snapshot, AreaChart? chart = null, Func<AreaOccupancy?>? occupancy = null) : IRouteStepSource
{
    public AreaStepSnapshot Snapshot { get; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public AreaChart Chart { get; } = chart ?? AreaChart.Exterior;

    public bool Contains(CellCoord cell) => Snapshot.IsAdmitted(Chart.ToWorld(cell));

    public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction)
    {
        var to = RoutePlanner.Neighbor(from, direction);
        var assessment = Snapshot.Assess(Snapshot.Request(Chart.ToWorld(from), Chart.ToWorld(to), GridReplay.MapDirection(direction)));
        return AreaOccupancyRules.Route(AreaOccupancyRules.Apply(assessment, from, direction, occupancy?.Invoke()));
    }
}
