using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>ARC-DEV-222 (MIG15 S6): the two pure conversions the AI host and the companions read from the grid actor, out of the viewer's
/// <c>DiagnosticGridActor</c> (which draws): an exact scalar as seconds, and a facing as the drawn octant. <c>DiagnosticGridActor.Seconds</c> and
/// <c>Octant</c> call these.</summary>
public static class GridActorMath
{
    public static double Seconds(ExactScalar value) => (double)value.Numerator / (double)value.Denominator;
    public static int Octant(NeighborDirection facing) => facing switch
    {
        NeighborDirection.XMinusYMinus => 0, NeighborDirection.XMinus => 1,
        NeighborDirection.XMinusYPlus => 2, NeighborDirection.YPlus => 3,
        NeighborDirection.XPlusYPlus => 4, NeighborDirection.XPlus => 5,
        NeighborDirection.XPlusYMinus => 6, NeighborDirection.YMinus => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(facing))
    };
}
