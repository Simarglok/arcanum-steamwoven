using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;

namespace Arcanum.World;

/// <summary>One eye candy a run shows now: its owner, the run's handle, the goal's entry and frames, and the parts the entry places.</summary>
public sealed record ShownOverlay(string Owner, GoalHandle Handle, GoalEyeCandy Eye, EyeCandyParts Parts, GoalType Goal);

/// <summary>
/// ARC-DEV-143 (MIG13 S9b, docs/architecture/goal-table.md §3.5, the S9b read GT83–GT93): the host of the shown map's overlay runs: the eye candy of any object (goals 45–48, 58, 59, 75, 76) and the hover of the objects the floating spell raises (goal 44). An eye
/// candy is a run beside the owner's main run (priority 5, no interrupt, carried): the cast host's EyeCandy components start it through <see cref="Play"/> (the add 0x4CCF70: the goal is chosen by the play flags, the duplicate test, the places in the owner's
/// overlay lists, the queue under the previous run) and take it off through <see cref="Remove"/> (0x4243E0: a level-6 interrupt of the goal-45 runs of the same entry and tag). The run steps on the animation clock inside the frame step (<see cref="IGoalStepHost"/>),
/// so the parts stop with the Esc pause; the frame lives on the goal, and the viewer draws <see cref="Shown"/>. The host answers the overlay ports (<see cref="IAiOverlayWorld"/>) from the ART facts the viewer reads.
/// <b>Not ready:</b> the sound, the light and the tinting of an eye candy, the destroy flag (goal 32 queued under the run), the fire damage of goals 75/76 to items and the source's bonus and hostility (S9c burns the critters, <see cref="AreaFireBurner"/>), the callbacks into the spell run (counted, <see cref="Callbacks"/>;
/// the cast host does not wait for them), the sites that are not an EyeCandy component (struck critters, weapons, scripts, traps) and the draw of the hover's raised body.
/// </summary>
public sealed class AreaEyeCandyHost : IAiOverlayWorld, IGoalStepHost
{
    /// <summary>The places of an owner's overlay lists (0x4CCF70): 7 foreground, 7 background, 4 underlay.</summary>
    public const int ForegroundPlaces = 7, BackgroundPlaces = 7, UnderlayPlaces = 4;

    private readonly GameSessionOwner _session;
    private readonly Func<int, SpellEyeCandyEntry?> _entry;
    private readonly Func<uint, EyeCandyParts, EyeCandyFacts?> _facts;
    private readonly Func<int, int, int> _random;
    private readonly Func<string, IEnumerable<EyeCandyParts>>? _otherParts;
    private readonly AreaFireBurner? _fire;
    private readonly Dictionary<string, int> _hover = new(StringComparer.Ordinal);
    private readonly StartPorts _startPorts = new();
    // The handle of the last eye-candy run started, host-wide like the original's global 0x601700 (GT70): a stack flag queues under it whatever its owner.
    private GoalHandle? _last;

    /// <param name="entry">The eye-candy table's entry by index, or null.</param>
    /// <param name="facts">The ART facts of an entry's art and parts (the header reads), or null when the art does not read.</param>
    /// <param name="random">A number in the inclusive range (0x4CEF00).</param>
    /// <param name="otherParts">The parts of the owner's other shown eye candies that are not runs of this host (the casting goal's own cast animation), which fill the same overlay lists.</param>
    /// <param name="fire">The burn of the fire-damage eye candy 75/76 (GT94), or null when nothing burns (the damage is counted only).</param>
    public AreaEyeCandyHost(GameSessionOwner session, Func<int, SpellEyeCandyEntry?> entry, Func<uint, EyeCandyParts, EyeCandyFacts?> facts, Func<int, int, int>? random = null,
        Func<string, IEnumerable<EyeCandyParts>>? otherParts = null, AreaFireBurner? fire = null)
    {
        _fire = fire;
        _otherParts = otherParts;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _random = random ?? ((low, high) => Random.Shared.Next(low, high + 1));
    }

    public GameSessionOwner Session => _session;

    /// <summary>The overlay runs this host stepped.</summary>
    public int Steps { get; private set; }

    /// <summary>The runs the table refused to start (a full table, or a start that found the table held by a pass in progress: the add is dropped).</summary>
    public int Refused { get; private set; }

    /// <summary>The adds the host refused before a run (a duplicate, a full list, no part to animate).</summary>
    public int Skipped { get; private set; }

    /// <summary>The fire damages of goals 75/76 reached (<see cref="AreaFireBurner"/> does the damage).</summary>
    public long FireDamages { get; private set; }

    /// <summary>The callbacks of goals 47/48 (phase 3) and 58/59 (phase 4) reached, as (tag, phase); the spell run does not wait for them yet.</summary>
    public List<(int Tag, int Phase)> Callbacks { get; } = [];

    /// <summary>The pixel offset y the hover has applied to an object (0 when none), as the original's stored object offset: a run ended from outside leaves it.</summary>
    public int HoverOffset(string owner) => _hover.GetValueOrDefault(owner);

    /// <summary>The eye candies that show their parts now (the run has begun), with the parts their entries place.</summary>
    public IEnumerable<ShownOverlay> Shown()
    {
        foreach (var run in _session.QueryGoals().Runs)
        {
            var top = run.Top;
            if (run.Done || !AiExecutorRules.IsEyeCandyGoal(top.Goal) || top.EyeCandy is not { } eye || (top.Flags & GoalFrameFlags.Begun) == 0) continue;
            if (PartsOf(eye.Entry) is not { } parts) continue;
            yield return new(run.Owner, run.Handle, eye, parts, top.Goal);
        }
    }

    /// <summary>The overlay runs of an owner (eye candy and hover).</summary>
    public IEnumerable<GoalRun> RunsOf(string owner) => _session.QueryGoals().RunsOf(owner).Where(r => !r.Done && AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal));

    private EyeCandyParts? PartsOf(int index) =>
        _entry(index) is { } e ? new((e.Flags & SpellEyeCandyFlags.Foreground) != 0, (e.Flags & SpellEyeCandyFlags.Background) != 0, (e.Flags & SpellEyeCandyFlags.Underlay) != 0) : null;

    // ---- the add and the remove ----

    /// <summary>
    /// The add 0x4CCF70 for an entry that animates with a foreground or underlay part: refused when the owner has no free place in a list the parts go to, or (play flag 0x80) when a goal-45 run of the owner has the same art and tag; started through the
    /// no-interrupt starter, or (play flag 0x2) inserted under the previous eye candy run so that it plays after it. Returns whether a run holds the eye candy now.
    /// </summary>
    public bool Play(string owner, int entryIndex, uint play, int tag, string? source = null)
    {
        if (_entry(entryIndex) is not { Art: { } art } entry || PartsOf(entryIndex) is not { } parts) return false;
        if (!SpellEyeCandyRules.Plays((entry.Flags & SpellEyeCandyFlags.Animates) != 0, parts)) return false;
        if (!PlacesFree(owner, parts)) { Skipped++; return false; }
        if ((play & AiExecutorRules.PlayCheckAlready) != 0
            && _session.QueryGoals().RunsOf(owner).Any(r => !r.Done && r.Bottom.Goal == GoalType.EyeCandy && r.Top.EyeCandy is { } e && e.Tag == tag && e.Art == art)) { Skipped++; return false; }
        var frame = AiExecutorRules.EyeCandyFrame(play, art, entryIndex, tag, (entry.Flags & SpellEyeCandyFlags.Forever) != 0, source);
        // Play flag 0x2: the frame goes under the previous eye-candy run (0x44DBE0, the bottom insert), which then plays it after its own.
        if ((play & AiExecutorRules.PlayStack) != 0 && _last is { } previous && _session.QueryGoals().TryGet(previous, out var held) && !held.Done && AiExecutorRules.IsEyeCandyGoal(held.Bottom.Goal)
            && held.Frames.Length < GoalRun.MaxFrames && _session.ReplaceGoalFrames(previous, held.Frames.Insert(0, frame)) == SessionGoalFailure.None) return true;
        var result = _session.StartGoal(new GoalStartRequest(owner, GoalStarters.NoInterrupt, frame), _startPorts);
        if (!result.Started) { Refused++; return false; }
        _last = result.Handle;
        return true;
    }

    /// <summary>The remove 0x4CD940 / 0x4243E0: the owner's runs whose bottom goal is exactly 45 and whose current frame has this entry's art and (unless <paramref name="anyTag"/>) this tag end at level 6. Returns how many. A looping entry played as 47, 58 or 75 (or queued under another run) cannot be removed this way (GT71, GT85).</summary>
    public int Remove(string owner, int entryIndex, int tag, bool anyTag)
    {
        var removed = 0;
        foreach (var run in _session.QueryGoals().RunsOf(owner).Where(r => !r.Done && r.Bottom.Goal == GoalType.EyeCandy && r.Top.EyeCandy is { } e && e.Entry == entryIndex && (anyTag || e.Tag == tag)).ToArray())
        {
            if (_session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel).Succeeded) removed++;
        }
        return removed;
    }

    /// <summary>Every eye-candy run with this tag ends at level 6 (the cast host freed the spell run the eye candy belongs to). Returns how many.</summary>
    public int EndTagged(int tag)
    {
        var ended = 0;
        foreach (var run in _session.QueryGoals().Runs.Where(r => !r.Done && AiExecutorRules.IsEyeCandyGoal(r.Bottom.Goal) && r.Frames.Any(f => f.EyeCandy?.Tag == tag)).ToArray())
        {
            if (_session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel).Succeeded) ended++;
        }
        return ended;
    }

    /// <summary>The AGoal component for goal 44 (0x4514E0): the hover starts through the no-interrupt starter unless the owner hovers already (a second hover would stack the bob).</summary>
    public bool StartHover(string owner)
    {
        if (_session.QueryGoals().RunsOf(owner).Any(r => !r.Done && r.Bottom.Goal == GoalType.Floating)) return false;
        var result = _session.StartGoal(new GoalStartRequest(owner, GoalStarters.NoInterrupt, AiExecutorRules.HoverFrame()), _startPorts);
        if (!result.Started) Refused++;
        return result.Started;
    }

    /// <summary>The AGoalTerminate component for goal 44 (0x4516D0 → 0x44E4D0): the owner's hover runs end at level 6 and leave the object raised (no end node runs).</summary>
    public int EndHover(string owner)
    {
        var ended = 0;
        foreach (var run in _session.QueryGoals().RunsOf(owner).Where(r => !r.Done && r.Bottom.Goal == GoalType.Floating).ToArray())
        {
            if (_session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel).Succeeded) ended++;
        }
        return ended;
    }

    /// <summary>The overlay runs are given up (a map change, a quick load, a replaced session): they end at level 6 and the hovering objects keep their offsets.</summary>
    public void LoseAll()
    {
        try
        {
            foreach (var run in _session.QueryGoals().Runs.Where(r => !r.Done && AiExecutorRules.IsOverlayRunGoal(r.Bottom.Goal)).ToArray())
                _session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel);
        }
        catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to end */ }
        _last = null;
    }

    /// <summary>Whether the owner has a free place in every list the parts go to: the shown eye candies of the owner count 7 foreground, 7 background and 4 underlay at most (0x4CCF70).</summary>
    public bool PlacesFree(string owner, EyeCandyParts parts) => PlacesFree(owner, parts, null);

    // The run being stepped is left out of the count when it asks for its own places: a queued eye candy begins in the step that popped the one before it, whose parts the table still shows (the finish 0x431A40 freed them first).
    private bool PlacesFree(string owner, EyeCandyParts parts, int? steppingSlot)
    {
        var others = _otherParts?.Invoke(owner).ToArray() ?? [];
        int Count(Func<EyeCandyParts, bool> part) => Shown().Count(s => s.Owner == owner && s.Handle.Slot != steppingSlot && part(s.Parts)) + others.Count(part);
        return (!parts.Foreground || Count(p => p.Foreground) < ForegroundPlaces) && (!parts.Background || Count(p => p.Background) < BackgroundPlaces)
            && (!parts.Underlay || Count(p => p.Underlay) < UnderlayPlaces);
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        Steps++;
        var stepped = GoalStepper.StepOverlay(run, this);
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    // ---- the overlay ports ----

    public EyeCandyFacts? EyeCandyFactsOf(string owner, GoalFrame goal) =>
        goal.EyeCandy is { } eye && PartsOf(eye.Entry) is { } parts ? _facts(eye.Art, parts) : null;

    // The first begin (0x4CDC50 → 0x4CD7A0): a free place in each list; the run itself is not shown yet, so it does not count.
    public bool EyeCandyPlace(string owner, GoalFrame goal) => goal.EyeCandy is { } eye && PartsOf(eye.Entry) is { } parts && PlacesFree(owner, parts, _session.QueryGoals().SteppingSlot);

    public int EyeCandyRandom(int low, int high) => _random(low, high);

    // The burn of 75/76 (0x42A720): 1 normal damage to the critters on the owner's own tile, the owner itself included when it is one.
    public int? FireRound() => _fire?.Round;

    public int Burn(string owner, bool normal, bool doubled)
    {
        FireDamages++;
        return _fire is null ? 0 : _fire.Burn(_fire.TileOf(owner), normal, doubled);
    }

    public void EyeCandyCallback(string owner, int tag, int phase) { if (tag >= 0) Callbacks.Add((tag, phase)); }

    public void HoverShift(string owner, int deltaY) => _hover[owner] = _hover.GetValueOrDefault(owner) + deltaY;

    // The table's start ports: the add passes no gate (it starts beside the main run, no interrupt), nothing loads, and an eye candy is placed on objects that are not destroyed.
    private sealed class StartPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
