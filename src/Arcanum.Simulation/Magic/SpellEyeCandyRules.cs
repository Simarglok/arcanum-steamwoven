namespace Arcanum.Simulation.Magic;

/// <summary>ARC-MAGIC-017: where a spell's eye candy sits in the eye-candy table (ledger EC rows): six entries per spell; the cast
/// overlay (0x456D20), the missile (0x458B70), the release's caster eye candy (0x456E00) and an EyeCandy component's number
/// (0x452017) all index from 6 · spell.</summary>
public static class SpellEyeCandyRules
{
    public const int PerSpell = 6, Cast = 0, Projectile = 1, Destination = 2, SecondaryDestination = 3, SecondaryCast = 4, Hit = 5;

    public static int EntryIndex(int spell, int number) => checked(spell * PerSpell + number);

    /// <summary>Whether an entry plays as its own goal: it animates and has a foreground or an underlay part (0x4CD49D: the slot the goal is given is the foreground's or the underlay's). ARC-DEV-143: the goal itself is
    /// an overlay run now (<c>AiExecutorRules.EyeCandyFrame</c>); the former timer class <c>EyeCandyAnimation</c> is gone.</summary>
    public static bool Plays(bool animates, EyeCandyParts parts) => animates && (parts.Foreground || parts.Underlay);
}

/// <summary>The ART facts an eye candy's animation reads (0x5030B0): the frames of its group, its action frame and its frame rate.</summary>
public readonly record struct EyeCandyClip(int Frames, int ActionFrame, int FramesPerSecond)
{
    /// <summary>The pace of one frame (0x431725): 1000 / the frame rate, truncated; null for a rate the original divides by zero
    /// with (0 or less: the original crashes).</summary>
    public int? PaceMs => FramesPerSecond > 0 ? 1000 / FramesPerSecond : null;
}

/// <summary>The parts of an eye candy that a play places and the goal animates (ledger EC rows): every flagged part is placed; with
/// an underlay only the underlay is animated (and removed at the end), else the foreground and the background; the timing part is the
/// underlay, else the foreground, else the background, and only a foreground or underlay sets the action flag.</summary>
public readonly record struct EyeCandyParts(bool Foreground, bool Background, bool Underlay)
{
    public bool Any => Foreground || Background || Underlay;
    public bool AnimatesForeground => Foreground && !Underlay;
    public bool AnimatesBackground => Background && !Underlay;
    /// <summary>Whether the timing part's action frame sets the goal's action flag (0x431434, 0x43150F).</summary>
    public bool TimesRelease => Underlay || Foreground;
}

public enum EyeCandyStepKind
{
    /// <summary>The animated parts moved to <see cref="EyeCandyStep.Frame"/> (the timing part's frame).</summary>
    Frame,
    /// <summary>The cast's release frame ran (0x429C80): <see cref="EyeCandyStep.Passed"/> is the release's answer.</summary>
    Release,
    /// <summary>The goal ended and removed the animated parts (0x431A40); a kept (forever) eye candy stays at its last frame.</summary>
    Ended,
}

public readonly record struct EyeCandyStep(EyeCandyStepKind Kind, long AtMs, int Frame = 0, bool Passed = false);

/// <summary>
/// ARC-MAGIC-017: the casting goal 27 (and 83) as it plays the caster's eye candy (goal table 0x5A8BD0, ledger EC rows; no missile).
/// The start (0x429F00, 0x4315B0) places the cast entry's parts at frame 0 and takes the pace of the last part the play built (the
/// underlay, else the background, else the foreground; <paramref name="pace"/> when it is not the timing part); without a cast eye
/// candy (no art, no part, or a rate of 0) the release runs at once and the goal ends. Each step after it (the first at once) moves
/// the animated parts one frame (0x431340): the timing part reaching its action frame sets the action flag, and the gate (0x42CAA0)
/// then fires the release once; after a release that passes the goal steps again at once, after one that fails it ends at once; a
/// step that finds the timing part (or the background) at its last frame ends the goal one pace later; any other step waits one pace.
/// So frame k of a timing part with action frame A shows at (k − 1)·pace up to the release and at (k − 2)·pace after it, and an
/// action frame of 0 or past the last frame never releases (the end frees the run, 0x429CD0).
/// </summary>
public sealed class CastEyeCandyAnimation
{
    private readonly EyeCandyClip _timing;
    private readonly EyeCandyClip? _background;
    private readonly bool _timesRelease;
    private readonly int _pace;
    private int _frame, _backgroundFrame;
    private bool _flag;
    private bool _removing;
    private long _due;

    /// <param name="timing">The timing part's clip (null: no cast eye candy).</param>
    /// <param name="background">The background's clip when it animates beside a foreground (it ends the goal at its own last frame).</param>
    /// <param name="pace">The clip whose rate sets the pace (0x4315B0: the art the play built last), when not the timing part's.</param>
    public CastEyeCandyAnimation(EyeCandyClip? timing, EyeCandyClip? background, bool timesRelease, long startMs, EyeCandyClip? pace = null)
    {
        StartMs = startMs;
        _due = startMs;
        if (timing is not { } clip || clip.Frames < 1 || (pace ?? clip).PaceMs is not { } ms) { Immediate = true; return; }
        _timing = clip; _background = background is { Frames: > 0 } b ? b : null; _timesRelease = timesRelease; _pace = ms;
    }

    public long StartMs { get; }
    /// <summary>No cast eye candy plays: the release runs at the start and the goal ends.</summary>
    public bool Immediate { get; }
    /// <summary>The timing part's frame and the background's.</summary>
    public int Frame => _frame;
    public int BackgroundFrame => _backgroundFrame;
    public bool Released { get; private set; }
    public bool Ended { get; private set; }
    /// <summary>When the next step is due.</summary>
    public long DueMs => _due;
    /// <summary>ARC-MAGIC-018: set by a release that launched a missile instead (0x429C40 → 0x4246E0, whose true step restarts the
    /// goal with a pace's delay, ledger SM1): the next step then comes one pace after the release, not at once.</summary>
    public bool PaceAfterRelease { get; set; }

    /// <summary>Runs the steps due at or before <paramref name="nowMs"/>. <paramref name="release"/> is the release frame (the target
    /// re-check and BEGIN): true when it passed. The gate (0x42CAA0) is whole here: the action flag without the release flag sets
    /// the release flag and passes.</summary>
    [OriginalFunction(0x42CAA0, OriginalFunctionScope.Full)]
    public IReadOnlyList<EyeCandyStep> Advance(long nowMs, Func<bool> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var steps = new List<EyeCandyStep>();
        if (Ended) return steps;
        if (Immediate)
        {
            Released = true;
            steps.Add(new(EyeCandyStepKind.Release, _due, Passed: release()));
            End(steps, _due);
            return steps;
        }
        // At most one release and one frame per part each step: a bounded loop whatever the clock does.
        for (var guard = 0; !Ended && _due <= nowMs && guard < 4096; guard++)
        {
            var at = _due;
            if (_removing) { End(steps, at); break; }
            // The update (0x431340): a part at its last frame ends the goal one pace later.
            if (_frame >= _timing.Frames - 1) { Remove(at); continue; }
            _frame++;
            if (_timesRelease && _frame == _timing.ActionFrame) _flag = true;
            if (_background is { } back)
            {
                if (_backgroundFrame >= back.Frames - 1) { steps.Add(new(EyeCandyStepKind.Frame, at, _frame)); Remove(at); continue; }
                _backgroundFrame++;
            }
            steps.Add(new(EyeCandyStepKind.Frame, at, _frame));
            // The gate (0x42CAA0): the action flag without the release flag fires the release once (no missile: 0x429C80).
            if (_flag && !Released)
            {
                Released = true;
                var passed = release();
                steps.Add(new(EyeCandyStepKind.Release, at, _frame, passed));
                if (!passed) { End(steps, at); break; }
                if (PaceAfterRelease) _due = at + _pace;
                continue;
            }
            _due = at + _pace;
        }
        return steps;
    }

    private void Remove(long at) { _removing = true; _due = at + _pace; }

    private void End(List<EyeCandyStep> steps, long at)
    {
        Ended = true;
        steps.Add(new(EyeCandyStepKind.Ended, at, _frame));
    }
}
