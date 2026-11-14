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

    /// <summary>The most a literal frame time may lag behind what the queue or the session has already seen and still be lifted to it (seconds).</summary>
    public static readonly ExactScalar MaxLift = ExactScalar.Integer(5);

    /// <summary>
    /// ARC-DEV-2005: the time of a frame whose smoke gave a literal time. A smoke steps its stage by literal times, but a click, a key press or
    /// a helper that runs between its frames observes at the test time it finds (a wait loop that moved the test time on, a click that stamps
    /// the frame count at the wrong scale), so the queue can hold an observation a little later than the next literal frame. The frame is then
    /// lifted to that observation (a frame never goes back, and a smoke's clock only stands still until its own steps catch up) instead of being
    /// refused as GRID_OBSERVATION_NONMONOTONIC or GRID_OBSERVATION_LATE_OR_INVALID. A lag over <see cref="MaxLift"/> is a real mismatch of two
    /// clocks (a frame count read at the wrong scale is one), not a smoke's slack: the literal time stays and the queue refuses it. An invalid
    /// literal is returned as it is, so the queue names it. (5 s is 5 frames of an unscaled stage, 100 of a stage that counts twentieths.)
    /// </summary>
    public static SimulationTime Frame(SimulationTime literal, SimulationTime? newestObserved, SimulationTime? sessionTime)
    {
        if (!literal.IsValid) return literal;
        var floor = literal;
        if (newestObserved is { } newest && newest.Value > floor.Value) floor = newest;
        if (sessionTime is { } session && session.Value > floor.Value) floor = session;
        return floor.Value - literal.Value <= MaxLift ? floor : literal;
    }
}
