namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-COMBAT-021: where the drawn hero is in its knockdown.</summary>
public enum AreaKnockdownStage
{
    /// <summary>Up: the movement draws the hero.</summary>
    Standing,
    /// <summary>The fall-down animation (7) plays once.</summary>
    Falling,
    /// <summary>The fall's last frame held while the hero is unconscious or dead.</summary>
    Down,
    /// <summary>The get-up animation (8) plays once, then the hero stands.</summary>
    GettingUp,
}

/// <summary>One animation played once: its frames and its ART rate (frames per second).</summary>
public readonly record struct AreaKnockdownClip(int Frames, int FramesPerSecond)
{
    /// <summary>The clip's length: the last frame is reached at <c>(Frames − 1) / rate</c> and shown for one frame's time.</summary>
    public double DurationMs => Frames * 1000.0 / FramesPerSecond;

    /// <summary>The frame shown <paramref name="elapsedMs"/> after the start, held on the last.</summary>
    public int FrameAt(double elapsedMs) => (int)Math.Clamp(Math.Floor(elapsedMs * FramesPerSecond / 1000.0), 0, Frames - 1);
}

/// <summary>
/// ARC-COMBAT-021: the AREA hero's fall when it goes unconscious and its getting up when it comes round, as the original draws a critter
/// (ledger UC1–UC4). The fatigue setter knocks a critter down when the write leaves it unconscious (0x45D5A0 → 0x435A90: the knockdown
/// goal 62 plays the fall-down animation 7 and keeps its last frame); the fatigue recovery event, when it leaves a critter that was
/// unconscious conscious again, starts the get-up goal 63 (animation 8) for a critter whose art shows animation 7 (0x45E610 →
/// 0x434DE0). A dead hero lies on the same fall (the dying animation's choice is not drawn for the hero). The fall starts once the hero
/// stands settled (a step under way ends first); a hero that falls again while getting up falls from the start. Without a clip (a body
/// whose ART has no fall) the stages still run, with no frame to draw. Godot-free; the host feeds the life state and the time.
/// </summary>
public sealed class AreaHeroKnockdown
{
    private double _elapsedMs;

    public AreaKnockdownStage Stage { get; private set; }

    /// <summary>The frame of the playing or held clip (0 standing).</summary>
    public int Frame { get; private set; }

    /// <summary>Whether the hero is off its feet (falling, down or getting up): it takes no world command.</summary>
    public bool Lying => Stage != AreaKnockdownStage.Standing;

    /// <summary>A new session or map: no fall plays, a hero already down lies on the fall's last frame.</summary>
    public void Rebase(bool down, AreaKnockdownClip? fall)
    {
        Stage = down ? AreaKnockdownStage.Down : AreaKnockdownStage.Standing;
        Frame = down && fall is { } clip ? clip.Frames - 1 : 0;
        _elapsedMs = 0;
    }

    /// <summary>
    /// Moves the knockdown on by <paramref name="milliseconds"/>; true when the stage or the frame changed.
    /// <paramref name="mayGetUp"/> is asked once per try when a conscious hero lies down (in a turn-based round only on its own subturn,
    /// paying the get-up's action points); a refusal keeps it down until a later tick.
    /// </summary>
    /// <param name="down">The hero is unconscious or dead.</param>
    /// <param name="settled">The hero stands on its cell (no step under way).</param>
    public bool Tick(double milliseconds, bool down, bool settled, Func<bool> mayGetUp, AreaKnockdownClip? fall, AreaKnockdownClip? getUp)
    {
        ArgumentNullException.ThrowIfNull(mayGetUp);
        if (!double.IsFinite(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var before = (Stage, Frame);
        switch (Stage)
        {
            case AreaKnockdownStage.Standing:
                if (down && settled) Start(fall is null ? AreaKnockdownStage.Down : AreaKnockdownStage.Falling, 0);
                break;
            case AreaKnockdownStage.Falling:
                Play(milliseconds, fall, AreaKnockdownStage.Down);
                break;
            case AreaKnockdownStage.Down:
                if (!down && mayGetUp()) Start(getUp is null ? AreaKnockdownStage.Standing : AreaKnockdownStage.GettingUp, 0);
                break;
            case AreaKnockdownStage.GettingUp:
                if (down) Start(fall is null ? AreaKnockdownStage.Down : AreaKnockdownStage.Falling, 0);
                else Play(milliseconds, getUp, AreaKnockdownStage.Standing);
                break;
        }
        return before != (Stage, Frame);
    }

    private void Start(AreaKnockdownStage stage, int frame) { Stage = stage; Frame = frame; _elapsedMs = 0; }

    private void Play(double milliseconds, AreaKnockdownClip? clip, AreaKnockdownStage next)
    {
        if (clip is not { } playing) { Start(next, 0); return; }
        _elapsedMs += milliseconds;
        if (_elapsedMs >= playing.DurationMs) { Start(next, next == AreaKnockdownStage.Down ? playing.Frames - 1 : 0); return; }
        Frame = playing.FrameAt(_elapsedMs);
    }
}
