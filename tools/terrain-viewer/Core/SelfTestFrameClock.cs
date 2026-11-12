using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-1251: the clock of a synthetic self-test frame. A self-test drives the world on test time, one tenth of a second a frame, from the actor
/// session's time. Before its first frame the world still reads the wall clock (a quarantine at a pad selection, a route click), so the queue
/// can already hold an observation newer than the session's time plus a tenth: a slow machine moves the wall clock on by more than that between the
/// session's creation and the stage's setup. A frame must never be stamped before what the queue has seen, or it is refused as
/// GRID_OBSERVATION_NONMONOTONIC.
/// </summary>
public static class SelfTestFrameClock
{
    /// <summary>The test time one synthetic frame advances.</summary>
    public static readonly ExactScalar Step = ExactScalar.Create(1, 10);

    /// <summary>The time of the next synthetic frame: a step after <paramref name="basis"/>, or after the newest observation when that is later.</summary>
    public static SimulationTime Next(SimulationTime basis, SimulationTime? newestObserved)
    {
        var from = newestObserved is { } newest && newest.Value > basis.Value ? newest : basis;
        return new SimulationTime(from.Value + Step);
    }
}
