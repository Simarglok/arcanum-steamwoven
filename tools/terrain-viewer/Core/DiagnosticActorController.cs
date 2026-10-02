using Arcanum.Presentation.Animation;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer.Core;

public sealed class DiagnosticActorController
{
    private readonly DiagnosticActorMotion _motion = new();
    private AnimationTimeline _timeline = null!;
    public DiagnosticActorController() => Reset();
    public DiagnosticActorState State { get; private set; } = null!;
    public int CurrentFrame => _timeline.CurrentFrame;
    public bool IsPlaying => _timeline.IsPlaying;
    public void Reset()
    {
        State = _motion.Reset();
        _timeline = Timeline(State.Action);
    }
    public void SafetyStop()
    {
        State = State with { Action = ActorDiagnosticAction.Idle };
        _timeline = Timeline(State.Action); _timeline.Stop();
    }

    public DiagnosticActorStep Update(Vec2 intent, double delta) => Update(intent, delta, null);

    /// <summary>Selection callback executes on a trial state/clock; a failure commits neither.</summary>
    public DiagnosticActorStep Update(Vec2 intent, double delta, Action<ActorDiagnosticPlacementInput>? validateSelection)
    {
        var step = _motion.Update(State, intent, delta);
        if (step.Status != DiagnosticMotionStatus.Accepted) return step;
        var trial = _timeline.Snapshot(); var action = State.Action;
        foreach (var segment in step.Segments)
        {
            if (segment.Action != action) { action = segment.Action; trial = Timeline(action); }
            // Safety-paused clocks restart only when a host admits a genuine frame.
            if (delta > 0) trial.Play();
            trial.Advance(segment.Seconds);
        }
        validateSelection?.Invoke(DiagnosticActorMotion.ToPlacement(step.State, trial.CurrentFrame));
        State = step.State; _timeline = trial;
        return step;
    }

    private static AnimationTimeline Timeline(ActorDiagnosticAction action)
    {
        var idle = action == ActorDiagnosticAction.Idle;
        var timeline = new AnimationTimeline(idle ? ActorDiagnosticSource.IdleFrames : ActorDiagnosticSource.WalkFrames,
            idle ? ActorDiagnosticSource.IdleFps : ActorDiagnosticSource.WalkFps);
        timeline.SetManualFramesPerSecond(idle ? DiagnosticWorldPolicy.IdleFramesPerSecond : DiagnosticWorldPolicy.WalkFramesPerSecond);
        timeline.SetMode(PlaybackMode.Loop); timeline.Play();
        return timeline;
    }
}
