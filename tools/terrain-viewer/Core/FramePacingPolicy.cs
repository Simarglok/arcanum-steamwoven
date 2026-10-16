namespace Arcanum.TerrainViewer.Core;

/// <summary>Interactive frame-rate caps: the active display's refresh rate while the
/// user is acting (input held or recent, camera moving), and at most
/// <see cref="IdleFps"/> once nothing has changed for <see cref="IdleAfterSeconds"/>.
/// Automated tests keep the engine's uncapped pacing. The engine limiter alone
/// undershoots on macOS (cap 60 → about 50 FPS, cap 180 → about 120 FPS on a 180 Hz
/// display), so the cap is corrected from the measured rate; see <see cref="CorrectCap"/>.</summary>
public static class FramePacingPolicy
{
    public const int IdleFps = 60;
    public const double IdleAfterSeconds = 0.5;
    public const int UnknownDisplayFps = 60;
    public const int MaxDisplayFps = 480;
    /// <summary>The status line is rebuilt at most this often per second in an
    /// interactive frame loop; event-driven refreshes stay immediate.</summary>
    public const double StatusRefreshSeconds = 0.1;
    /// <summary>The engine cap never exceeds this multiple of the target.</summary>
    public const int MaxCapFactor = 4;
    /// <summary>Measured rates within this many FPS of the target keep the cap.</summary>
    public const int ToleranceFps = 1;

    /// <summary>A refresh rate the display reports, or <see cref="UnknownDisplayFps"/>
    /// when it reports none (Godot returns -1).</summary>
    public static int DisplayFps(double refreshRate)
        => double.IsFinite(refreshRate) && refreshRate >= 1 ? (int)Math.Clamp(Math.Round(refreshRate), 1, MaxDisplayFps) : UnknownDisplayFps;

    public static int Target(double secondsSinceActivity, double refreshRate)
    {
        var display = DisplayFps(refreshRate);
        return double.IsFinite(secondsSinceActivity) && secondsSinceActivity >= 0 && secondsSinceActivity < IdleAfterSeconds
            ? display : Math.Min(IdleFps, display);
    }

    /// <summary>The next engine cap for <paramref name="target"/>, given the cap in force
    /// and the frame rate measured under it: raised or lowered by the error, within
    /// [target, <see cref="MaxCapFactor"/> × target]. A frame-bound loop (measured below
    /// target at any cap) ends at the upper bound, which is then the effective limit.</summary>
    public static int CorrectCap(int target, int cap, double measuredFps)
    {
        if (target < 1) throw new ArgumentOutOfRangeException(nameof(target));
        var max = checked(target * MaxCapFactor);
        cap = Math.Clamp(cap, target, max);
        if (!double.IsFinite(measuredFps) || measuredFps <= 0 || Math.Abs(target - measuredFps) <= ToleranceFps) return cap;
        return (int)Math.Clamp(Math.Round(cap + (target - measuredFps)), target, max);
    }
}
