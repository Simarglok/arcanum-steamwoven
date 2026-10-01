namespace Arcanum.Presentation.Animation;

/// <summary>
/// A pure presentation timeline for an already-selected sequence of animation frames.
/// Call <see cref="Advance" /> with elapsed monotonic time from the presentation host.
/// </summary>
public sealed class AnimationTimeline
{
    // Deltas come from binary floating-point presentation clocks. This only absorbs arithmetic noise at an exact frame boundary;
    // it is far smaller than the shortest accepted interval (1 / 240 seconds).
    private const double BoundaryToleranceFrames = 1e-10d;

    private double _intervalElapsedSeconds;
    private bool _isPlaying;
    private bool _isCompleted;

    /// <summary>Independent snapshot for a host's transactional selection preflight.</summary>
    public AnimationTimeline Snapshot() => (AnimationTimeline)MemberwiseClone();

    /// <summary>Creates a paused timeline positioned at frame zero.</summary>
    public AnimationTimeline(int frameCount, int sourceFramesPerSecond)
    {
        ValidateFrameCount(frameCount);
        FrameCount = frameCount;
        SourceFramesPerSecond = sourceFramesPerSecond;
        Speed = PlaybackSpeed.Normal;
        Mode = PlaybackMode.Loop;
    }

    /// <summary>Gets the number of frames in the currently selected sequence.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Gets the currently selected zero-based frame.</summary>
    public int CurrentFrame { get; private set; }

    /// <summary>Gets the raw signed FPS value reported by the selected source asset.</summary>
    public int SourceFramesPerSecond { get; private set; }

    /// <summary>Gets the explicit inspector FPS override, if one is selected.</summary>
    public int? ManualFramesPerSecond { get; private set; }

    /// <summary>Gets the selected closed-set playback multiplier.</summary>
    public PlaybackSpeed Speed { get; private set; }

    /// <summary>Gets the multiplier represented by <see cref="Speed" />.</summary>
    public double SpeedMultiplier => Speed.ToMultiplier();

    /// <summary>Gets the source or override FPS after applying <see cref="Speed" />, or null when playback is blocked.</summary>
    public double? EffectiveFramesPerSecond => TryGetPlaybackFramesPerSecond(out var framesPerSecond) ? framesPerSecond : null;

    /// <summary>Gets whether the current sequence has a playable rate and more than one frame.</summary>
    public bool CanPlay => FrameCount > 1 && EffectiveFramesPerSecond is not null;

    /// <summary>Gets whether elapsed time may currently advance the timeline.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Gets whether a play-once run reached its completion boundary.</summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>Gets the selected completion behavior.</summary>
    public PlaybackMode Mode { get; private set; }

    /// <summary>
    /// Replaces the asset metadata, preserving inspector-selected mode, override, and speed.
    /// The new asset is paused at frame zero.
    /// </summary>
    public void ResetAsset(int frameCount, int sourceFramesPerSecond)
    {
        ValidateFrameCount(frameCount);
        FrameCount = frameCount;
        SourceFramesPerSecond = sourceFramesPerSecond;
        CurrentFrame = 0;
        _isPlaying = false;
        _isCompleted = false;
        ResetInterval();
    }

    /// <summary>
    /// Changes the selected group or palette sequence while retaining the current play/pause state and a valid frame.
    /// The old interval and completion state never carry into the new sequence.
    /// </summary>
    public void ReconfigureSequence(int frameCount)
    {
        ValidateFrameCount(frameCount);
        FrameCount = frameCount;
        CurrentFrame = Math.Min(CurrentFrame, frameCount - 1);
        _isCompleted = false;
        ResetInterval();
        if (!CanPlay)
        {
            _isPlaying = false;
        }
    }

    /// <summary>
    /// Sets or clears the explicit FPS override. Non-null overrides must be within 1 through 120 inclusive.
    /// A rate change preserves the frame but starts a new interval.
    /// </summary>
    public void SetManualFramesPerSecond(int? framesPerSecond)
    {
        if (framesPerSecond is int value && !IsValidFramesPerSecond(value))
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), framesPerSecond, "Manual FPS must be within 1 through 120.");
        }

        if (ManualFramesPerSecond == framesPerSecond)
        {
            return;
        }

        ManualFramesPerSecond = framesPerSecond;
        ResetInterval();
        if (!CanPlay)
        {
            _isPlaying = false;
        }
    }

    /// <summary>Sets a closed-set speed choice while retaining the frame and restarting its interval.</summary>
    public void SetSpeed(PlaybackSpeed speed)
    {
        ValidateSpeed(speed);
        if (Speed == speed)
        {
            return;
        }

        Speed = speed;
        ResetInterval();
    }

    /// <summary>Sets looping or play-once behavior and starts a fresh interval when the mode changes.</summary>
    public void SetMode(PlaybackMode mode)
    {
        ValidateMode(mode);
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        _isCompleted = false;
        ResetInterval();
    }

    /// <summary>
    /// Starts playback from the current frame. A completed play-once run restarts at frame zero.
    /// Returns false for blocked rates and single-frame sequences.
    /// </summary>
    public bool Play()
    {
        if (!CanPlay)
        {
            _isPlaying = false;
            return false;
        }

        if (_isCompleted && Mode == PlaybackMode.Once)
        {
            CurrentFrame = 0;
            _isCompleted = false;
            ResetInterval();
        }

        _isPlaying = true;
        return true;
    }

    /// <summary>Pauses playback without changing the current frame or partial interval.</summary>
    public void Pause() => _isPlaying = false;

    /// <summary>Pauses at frame zero, clears completion, and starts a new interval.</summary>
    public void Stop()
    {
        CurrentFrame = 0;
        _isPlaying = false;
        _isCompleted = false;
        ResetInterval();
    }

    /// <summary>Moves to an in-range frame and pauses. The caller must provide a valid zero-based frame.</summary>
    public void Seek(int frame)
    {
        if (frame < 0 || frame >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "Frame must be within the active sequence.");
        }

        CurrentFrame = frame;
        _isPlaying = false;
        _isCompleted = false;
        ResetInterval();
    }

    /// <summary>Moves by a signed frame count, clamped to the sequence bounds, and pauses without wrapping.</summary>
    public void Step(int delta)
    {
        var target = Math.Clamp((long)CurrentFrame + delta, 0L, FrameCount - 1L);
        CurrentFrame = (int)target;
        _isPlaying = false;
        _isCompleted = false;
        ResetInterval();
    }

    /// <summary>
    /// Applies a finite, non-negative elapsed-time delta. Invalid deltas do not mutate state.
    /// Long loop stalls are reduced modulo one cycle; play-once stalls directly target the bounded final frame.
    /// </summary>
    public TimelineAdvanceResult Advance(double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
        {
            return Result(TimelineAdvanceStatus.InvalidDelta);
        }

        if (elapsedSeconds == 0d || !_isPlaying)
        {
            return Result(TimelineAdvanceStatus.NoChange);
        }

        if (!TryGetPlaybackFramesPerSecond(out var framesPerSecond))
        {
            _isPlaying = false;
            return Result(TimelineAdvanceStatus.BlockedRate);
        }

        if (FrameCount == 1)
        {
            _isPlaying = false;
            return Result(TimelineAdvanceStatus.NoChange);
        }

        return Mode == PlaybackMode.Loop
            ? AdvanceLoop(elapsedSeconds, framesPerSecond)
            : AdvanceOnce(elapsedSeconds, framesPerSecond);
    }

    private TimelineAdvanceResult AdvanceLoop(double elapsedSeconds, double framesPerSecond)
    {
        var cycleDuration = FrameCount / framesPerSecond;
        var elapsedWithinCycle = elapsedSeconds % cycleDuration;
        var elapsedFrames = (_intervalElapsedSeconds + elapsedWithinCycle) * framesPerSecond;
        var passedFrames = WholeFrames(elapsedFrames);
        var fractionalFrames = FractionalFrames(elapsedFrames, passedFrames);
        _intervalElapsedSeconds = fractionalFrames / framesPerSecond;

        if (passedFrames == 0)
        {
            return Result(TimelineAdvanceStatus.NoChange);
        }

        var target = (int)(((long)CurrentFrame + (passedFrames % FrameCount)) % FrameCount);
        if (target == CurrentFrame)
        {
            return Result(TimelineAdvanceStatus.NoChange);
        }

        CurrentFrame = target;
        return Result(TimelineAdvanceStatus.FrameChanged);
    }

    private TimelineAdvanceResult AdvanceOnce(double elapsedSeconds, double framesPerSecond)
    {
        var framesUntilCompletion = FrameCount - CurrentFrame;
        var elapsedFrames = (_intervalElapsedSeconds + elapsedSeconds) * framesPerSecond;
        if (elapsedFrames + BoundaryToleranceFrames >= framesUntilCompletion)
        {
            CurrentFrame = FrameCount - 1;
            _isPlaying = false;
            _isCompleted = true;
            ResetInterval();
            return Result(TimelineAdvanceStatus.Completed);
        }

        var passedFrames = (int)WholeFrames(elapsedFrames);
        _intervalElapsedSeconds = FractionalFrames(elapsedFrames, passedFrames) / framesPerSecond;
        if (passedFrames == 0)
        {
            return Result(TimelineAdvanceStatus.NoChange);
        }

        CurrentFrame += passedFrames;
        return Result(TimelineAdvanceStatus.FrameChanged);
    }

    private TimelineAdvanceResult Result(TimelineAdvanceStatus status) => new(status, CurrentFrame);

    private static long WholeFrames(double elapsedFrames) => (long)Math.Floor(elapsedFrames + BoundaryToleranceFrames);

    private static double FractionalFrames(double elapsedFrames, long wholeFrames) => Math.Max(0d, elapsedFrames - wholeFrames);

    private bool TryGetPlaybackFramesPerSecond(out double framesPerSecond)
    {
        var source = ManualFramesPerSecond ?? SourceFramesPerSecond;
        if (!IsValidFramesPerSecond(source))
        {
            framesPerSecond = default;
            return false;
        }

        framesPerSecond = source * Speed.ToMultiplier();
        return true;
    }

    private void ResetInterval() => _intervalElapsedSeconds = 0d;

    private static bool IsValidFramesPerSecond(int framesPerSecond) => framesPerSecond is >= 1 and <= 120;

    private static void ValidateFrameCount(int frameCount)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count must be positive.");
        }
    }

    private static void ValidateSpeed(PlaybackSpeed speed)
    {
        if (!Enum.IsDefined(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unsupported playback speed.");
        }
    }

    private static void ValidateMode(PlaybackMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported playback mode.");
        }
    }
}
