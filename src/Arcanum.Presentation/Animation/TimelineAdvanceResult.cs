namespace Arcanum.Presentation.Animation;

/// <summary>Describes the observable outcome of one elapsed-time playback update.</summary>
public enum TimelineAdvanceStatus
{
    NoChange,
    FrameChanged,
    Completed,
    InvalidDelta,
    BlockedRate,
}

/// <summary>The outcome and selected frame after an <see cref="AnimationTimeline.Advance" /> call.</summary>
public readonly record struct TimelineAdvanceResult(TimelineAdvanceStatus Status, int CurrentFrame);
