using Arcanum.Session;
using Arcanum.World;

namespace Arcanum.World;

/// <summary>
/// MIG12 S8 (ARC-DEV-127): the viewer's host of the real-time steps the session's frame step hands out after a pass
/// (<see cref="IRealStepHost"/>): the world-map journey's travel step (the viewer's <c>AreaWorldMap</c>) and the turn-based event (the round
/// host). Godot-free; the viewer supplies the two actions, each returning the real milliseconds until the step is due again or null.
/// </summary>
public sealed class AreaRealStepHost(Func<long?> worldMapStep, Func<long?> turnBasedStep) : IRealStepHost
{
    public long? RunRealStep(SessionRealStep step, Arcanum.Simulation.Clock.FrameClocks clocks) => step switch
    {
        SessionRealStep.WorldMapStep => worldMapStep(),
        SessionRealStep.TurnBasedStep => turnBasedStep(),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };
}
