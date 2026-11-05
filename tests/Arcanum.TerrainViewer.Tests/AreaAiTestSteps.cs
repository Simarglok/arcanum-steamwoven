using System.Runtime.CompilerServices;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-134 (MIG13 S4): the AI host's goal runs are session runs stepped by the frame step, so a test steps them as the viewer binds them: the frame step's
/// goal-step host (<see cref="AreaAiGoalSteps"/>) and the host's frame participant (<see cref="AreaAiFrameParticipant"/>).</summary>
internal static class AreaAiTestSteps
{
    /// <summary>Binds the host's goal steps and frame to <paramref name="step"/>; <paramref name="host"/> is asked at every step (a test may rebuild it).</summary>
    public static FrameStep WithAi(this FrameStep step, Func<AreaAiHost?> host)
    {
        step.GoalSteps = new AreaAiGoalSteps(host);
        step.Register(new AreaAiFrameParticipant(host));
        return step;
    }

    /// <summary>Binds <paramref name="host"/>'s goal steps and frame to <paramref name="step"/>.</summary>
    public static FrameStep WithAi(this FrameStep step, AreaAiHost host) => step.WithAi(() => host);

    private static readonly ConditionalWeakTable<AreaAiHost, FrameStep> Steps = new();

    /// <summary>The frame step of <paramref name="host"/> (one per host): its passes ask the host (its AI ticks run, as in the viewer) and it steps the host's goal runs.</summary>
    public static FrameStep Frames(this AreaAiHost host) => Steps.GetValue(host, h => new FrameStep(h.Session, h).WithAi(h));

    /// <summary>An event world that answers no fact and runs no AI pass: a pass consumes the AI ticks unrun, so a test steps only the goal runs.</summary>
    public static ISessionEventWorld NoAi { get; } = new NoAiWorld();

    private sealed class NoAiWorld : ISessionEventWorld
    {
        public Arcanum.Simulation.Combat.LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    /// <summary>Host milliseconds one at a time until a frame steps a goal run (at most 64): the steps due by then fire once each (a run's next step is at least 8 ms of
    /// animation time later), as one pump of the former host stepped every run that was due. The frame clock admits a host interval only once it has seen the stamps
    /// before it, so the first frames of a session may admit nothing.</summary>
    public static FrameStepReport StepDue(this FrameStep step)
    {
        var report = step.AdvanceHost(1);
        for (var i = 0; i < 64 && report.GoalSteps.Count == 0; i++) report = step.AdvanceHost(1);
        return report;
    }
}
