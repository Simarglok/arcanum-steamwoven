using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Route evidence over the whole-core T0 step snapshot, in <see cref="AreaGridSession"/> chart cells. It asks the same
/// <see cref="AreaStepSnapshot.Assess"/> the session admits steps with, so a planned Open step is the step the runtime
/// receives. Only Supported Open is Open; Unknown and Unsupported (core edge) stay distinct from Blocked.
/// </summary>
public sealed class AreaRouteSource(AreaStepSnapshot snapshot) : IRouteStepSource
{
    public AreaStepSnapshot Snapshot { get; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public bool Contains(CellCoord cell) => Snapshot.IsAdmitted(AreaGridSession.Cell(cell));

    public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction)
    {
        var to = RoutePlanner.Neighbor(from, direction);
        var assessment = Snapshot.Assess(Snapshot.Request(AreaGridSession.Cell(from), AreaGridSession.Cell(to), GridReplay.MapDirection(direction)));
        return assessment.Coverage switch
        {
            CardinalTraversalCoverage.Supported => assessment.Disposition == CardinalTraversalDisposition.Open ? RouteStepVerdict.Open : RouteStepVerdict.Blocked,
            CardinalTraversalCoverage.Unknown => RouteStepVerdict.Unknown,
            CardinalTraversalCoverage.Unsupported => RouteStepVerdict.Unsupported,
            _ => throw new InvalidOperationException("ROUTE_COVERAGE_INVALID")
        };
    }
}
