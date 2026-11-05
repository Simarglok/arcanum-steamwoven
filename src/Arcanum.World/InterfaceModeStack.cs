namespace Arcanum.World;

/// <summary>ARC-DEV-108: what became of a request to the mode stack.</summary>
public enum InterfaceModeStatus
{
    /// <summary>The change ran; the steps say which leave and enter cases it ran.</summary>
    Applied,
    /// <summary>A change was already running (an observer asked for another one): nothing was done, as the original's lock does (SA37).</summary>
    Locked,
    /// <summary>The opener refused for this hero (SA52); nothing was done.</summary>
    Refused,
    /// <summary>A push would pass <see cref="InterfaceModeLimits.MaxDepth"/>; nothing was done. The original has no such refusal (it
    /// would overwrite the depth word, inference), the depth it reaches in play is three.</summary>
    DepthExceeded,
    /// <summary>The mode is not one of the 21, or a push of mode 0 or an opener given a mode of another kind; nothing was done.</summary>
    InvalidMode,
}

/// <summary>ARC-DEV-108: the leave case of the old mode, or the enter case of the new one, that one change ran (SA37).</summary>
/// <param name="Leave">True for a leave case, false for an enter case.</param>
/// <param name="Mode">The mode whose case runs.</param>
/// <param name="Other">For a leave, the mode that becomes current (the mode below on a pop, the pushed mode on a push); for an enter,
/// the mode that was current before.</param>
/// <param name="Popped">True when the change is a pop: for a leave, the dialog ends on a pop and is only suspended by a push; for an
/// enter, the dialog resumes when it is returned to by a pop (mode table, row 3).</param>
public readonly record struct InterfaceModeStep(bool Leave, InterfaceMode Mode, InterfaceMode Other, bool Popped)
{
    /// <summary>For a leave: the mode's window closes (<see cref="InterfaceModes.LeaveClosesWindow"/>).</summary>
    public bool ClosesWindow => Leave && InterfaceModes.LeaveClosesWindow(Mode, Other);

    /// <summary>For a leave: a held item is put back (<see cref="InterfaceModes.LeavePutsHeldItemBack"/>).</summary>
    public bool PutsHeldItemBack => Leave && InterfaceModes.LeavePutsHeldItemBack(Mode, Other);
}

/// <summary>ARC-DEV-108: the result of one request: its status, and the leave and enter cases in the order the original runs them.</summary>
public sealed record InterfaceModeChange(InterfaceModeStatus Status, IReadOnlyList<InterfaceModeStep> Steps)
{
    /// <summary>True when the stack changed (or a pop at the base ran its cases, as in the original).</summary>
    public bool Applied => Status == InterfaceModeStatus.Applied;

    internal static InterfaceModeChange Without(InterfaceModeStatus status) => new(status, []);
}

/// <summary>ARC-DEV-108: the explicit bound of the stack. The original has 33 slots and no check.</summary>
public sealed record InterfaceModeLimits(int MaxDepth = 33)
{
    /// <summary>The default: the original's 33 slots.</summary>
    public static InterfaceModeLimits Default { get; } = new();
}

/// <summary>ARC-DEV-108: the host that carries out the cases of a change (opens and closes Godot panels, ends a dialog, puts an item
/// back). Called after the stack holds the new state and while the change's lock is set.</summary>
public interface IInterfaceModeObserver
{
    /// <summary>One leave or enter case, in the original's order.</summary>
    void Step(InterfaceModeStep step);
}

/// <summary>
/// ARC-DEV-108 (MIG16, TM13, GR35): the original's interface mode stack (<c>original-scripts-authority.md</c> SA37–SA40): a stack of
/// modes whose base is the game view (mode 0); one change function pushes a mode or pops one, runs the old mode's leave case and the
/// new mode's enter case, and cannot be refused; a window opener pops and pushes; the spell and skill target modes pop the mode below
/// them too when they are left. Not saved (the original does not save it); the hosts draw from it. Godot-free, one thread.
/// </summary>
public sealed class InterfaceModeStack
{
    private readonly List<InterfaceMode> _modes = [InterfaceMode.Game];
    private readonly InterfaceModeLimits _limits;
    private readonly IInterfaceModeObserver? _observer;
    private bool _locked;

    /// <summary>A stack holding the game view only.</summary>
    public InterfaceModeStack(IInterfaceModeObserver? observer = null, InterfaceModeLimits? limits = null)
    {
        _limits = limits ?? InterfaceModeLimits.Default;
        if (_limits.MaxDepth < 2) throw new ArgumentOutOfRangeException(nameof(limits), "the stack must hold the game view and one mode");
        _observer = observer;
    }

    /// <summary>The mode on top (the original's current mode, 0x551A00).</summary>
    public InterfaceMode Current => _modes[^1];

    /// <summary>The number of modes held, the game view included (1 at rest).</summary>
    public int Depth => _modes.Count;

    /// <summary>The modes from the base up.</summary>
    public IReadOnlyList<InterfaceMode> Modes => _modes.ToArray();

    /// <summary>True when the mode is anywhere on the stack.</summary>
    public bool Contains(InterfaceMode mode) => _modes.Contains(mode);

    /// <summary>The cursor family of the top mode (SA41).</summary>
    public InterfaceCursor Cursor => InterfaceModes.Cursor(Current);

    /// <summary>True when the game view acts on the mouse in the top mode (SA44).</summary>
    public bool GameViewActs(bool anotherHasTheTurn = false) => InterfaceModes.GameViewActs(Current, anotherHasTheTurn);

    /// <summary>The original's change function with an argument other than 0 (SA37): pushes <paramref name="mode"/>. There is no
    /// replace; mode 0 is the base and is never pushed (the original's call with 0 is the pop).</summary>
    public InterfaceModeChange Push(InterfaceMode mode)
    {
        if (!InterfaceModes.IsDefined(mode) || mode == InterfaceMode.Game) return InterfaceModeChange.Without(InterfaceModeStatus.InvalidMode);
        return Change(mode, pop: false);
    }

    /// <summary>The original's change function with argument 0 (SA37): pops the top mode; at the base the game view stays (the leave
    /// and enter cases of mode 0 still run).</summary>
    public InterfaceModeChange Pop() => Change(InterfaceMode.Game, pop: true);

    /// <summary>The two reset functions (0x54A330, 0x54AA30): the stack is the game view alone, with no leave or enter case. Like the
    /// original's, it does not look at the change lock: an observer that calls it during a change clears the stack under that change.</summary>
    public void Reset()
    {
        _modes.Clear();
        _modes.Add(InterfaceMode.Game);
    }

    /// <summary>
    /// A window opener (SA40): when its window is already open it closes it (the toggle); else it refuses for a hero that
    /// <see cref="InterfaceModes.OpenerRefuses"/> names; else it pops and pushes, so the window replaces the top mode. The original
    /// reads the window's own open flag; here "open" is "on the stack", and a window under an overlay (an inventory under a target mode
    /// or the quantity picker) is closed by popping until it is gone (inference: the close functions pop, SA37; the target mode's
    /// second pop takes the inventory with it, SA38).
    /// </summary>
    public InterfaceModeChange OpenWindow(InterfaceMode window, bool heroDead = false, bool heroUnconscious = false)
    {
        if (!InterfaceModes.IsWindow(window)) return InterfaceModeChange.Without(InterfaceModeStatus.InvalidMode);
        if (Contains(window))
        {
            var steps = new List<InterfaceModeStep>();
            while (Contains(window))
            {
                var popped = Pop();
                if (!popped.Applied) return new(popped.Status, steps);
                steps.AddRange(popped.Steps);
            }
            return new(InterfaceModeStatus.Applied, steps);
        }
        if (InterfaceModes.OpenerRefuses(window, heroDead, heroUnconscious)) return InterfaceModeChange.Without(InterfaceModeStatus.Refused);
        return Replace(window);
    }

    /// <summary>
    /// The start of a target mode (SA40, SA51): with <paramref name="overInventory"/> (the target mask has the inventory bit) it is
    /// pushed over the top and the inventory stays below; otherwise it replaces the top like a window (inference: the opener rows name
    /// only the push for the inventory case). A dead hero is refused for all three, a dead or unconscious one too (SA52).
    /// </summary>
    public InterfaceModeChange OpenTarget(InterfaceMode target, bool overInventory, bool heroDead = false, bool heroUnconscious = false)
    {
        if (!InterfaceModes.IsTarget(target)) return InterfaceModeChange.Without(InterfaceModeStatus.InvalidMode);
        if (InterfaceModes.OpenerRefuses(target, heroDead, heroUnconscious)) return InterfaceModeChange.Without(InterfaceModeStatus.Refused);
        return overInventory ? Push(target) : Replace(target);
    }

    /// <summary>The quantity picker (13), the matchmaker (17) or the follower order (18): pushed over the top, no hero test (SA40, SA52).</summary>
    public InterfaceModeChange OpenOverlay(InterfaceMode overlay) => InterfaceModes.IsOverlay(overlay)
        ? Push(overlay) : InterfaceModeChange.Without(InterfaceModeStatus.InvalidMode);

    // Pops, then pushes: the window replaces the top mode. Both halves run under their own lock; the steps join. A refused second
    // half leaves the stack popped, as the original's two calls would.
    private InterfaceModeChange Replace(InterfaceMode mode)
    {
        var popped = Pop();
        if (!popped.Applied) return popped;
        var pushed = Push(mode);
        return new(pushed.Status, [.. popped.Steps, .. pushed.Steps]);
    }

    private InterfaceModeChange Change(InterfaceMode mode, bool pop)
    {
        if (_locked) return InterfaceModeChange.Without(InterfaceModeStatus.Locked);
        if (!pop && _modes.Count >= _limits.MaxDepth) return InterfaceModeChange.Without(InterfaceModeStatus.DepthExceeded);
        _locked = true;
        try
        {
            var steps = new List<InterfaceModeStep>();
            var next = mode;
            var popping = pop;
            // A leave case of mode 1 or 2 asks for one more pop (SA38); the follow-up pop asks again only if it leaves 1 or 2 too.
            // Bounded: each repeat is a pop, a pop at the base leaves mode 0, which asks for nothing, and the round count is capped.
            for (var round = 0; round <= _limits.MaxDepth; round++)
            {
                var old = Current;
                if (popping)
                {
                    if (_modes.Count > 1) _modes.RemoveAt(_modes.Count - 1);
                    next = Current;
                }
                else _modes.Add(next);
                steps.Add(new(true, old, next, popping));
                steps.Add(new(false, next, old, popping));
                if (!InterfaceModes.LeavePopsAgain(old)) break;
                popping = true;
            }
            if (_observer is not null) foreach (var step in steps) _observer.Step(step);
            return new(InterfaceModeStatus.Applied, steps);
        }
        finally { _locked = false; }
    }
}
