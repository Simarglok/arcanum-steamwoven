using System.Collections.Immutable;

namespace Arcanum.Simulation.Magic;

/// <summary>A maintain-bar slot's state (the second word of the original's table 0x5CB3A8): empty (0), showing its run (1), or
/// clicked, its run's stop asked (2: it keeps the run until the run's end takes it away, and takes no further click or hover).</summary>
public enum MaintainSlotState { Empty = 0, Shown = 1, Stopping = 2 }

/// <summary>One slot of the maintain bar: its run (−1 none) and state.</summary>
public readonly record struct MaintainSlot(int Run, MaintainSlotState State)
{
    public static MaintainSlot Empty { get; } = new(-1, MaintainSlotState.Empty);
}

/// <summary>
/// ARC-MAGIC-026 (magic plan MT20b): the local player's maintain bar, the original's five-slot table 0x5CB3A8 behind the top strip's
/// SplSlt1–5 buttons. Immutable.
/// </summary>
public sealed class MaintainBar
{
    private readonly ImmutableArray<MaintainSlot> _slots;

    private MaintainBar(ImmutableArray<MaintainSlot> slots) => _slots = slots;

    /// <summary>The bar with every slot empty (0x57C470, a new game or a load).</summary>
    public static MaintainBar Empty { get; } = new([.. Enumerable.Repeat(MaintainSlot.Empty, MaintainBarRules.Slots)]);

    /// <summary>The five slots, left to right.</summary>
    public IReadOnlyList<MaintainSlot> Slots => _slots;

    public MaintainSlot this[int slot] => _slots[slot];

    internal MaintainBar With(int slot, MaintainSlot value) => new(_slots.SetItem(slot, value));

    /// <summary>The slot holding <paramref name="run"/> in a state other than empty, first from the left; null none.</summary>
    public int? SlotOf(int run)
    {
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].Run == run && _slots[i].State != MaintainSlotState.Empty) return i;
        return null;
    }

    public bool SameAs(MaintainBar other) => _slots.SequenceEqual((other ?? throw new ArgumentNullException(nameof(other)))._slots);
}

/// <summary>
/// ARC-MAGIC-026 (magic plan MT20b, ledger MB1–MB9): the original's maintain bar (0x57C290–0x57C520) as pure rules. The PC's
/// intelligence / 4 slots are open (at most five; the others show their plugged art); a maintained magic spell of the PC takes the
/// first empty open slot at its first maintenance tick (the scheduler's 0x4604A0), the run's end or release frees its slot (0x4604C0),
/// and a click on a slot showing a run asks that run's stop (0x57C370 → 0x457110). MG12, the original's out-of-bounds write (a full
/// bar writes slot −1; more than five open slots index past the table), is fixed in both modes: a full bar refuses the run (the
/// scheduler then ends it) and at most five slots open.
/// </summary>
public static class MaintainBarRules
{
    /// <summary>The bar's slots (SplSlt1–5).</summary>
    public const int Slots = 5;

    /// <summary>The spells the bar takes (0x459FF0): a valid run of a spell below 223 (the original's 0xDF bound).</summary>
    public const int SpellBound = 223;

    /// <summary>The open slots (0x57C4B0, the add's count; MG12 capped at five): intelligence / 4 toward zero, 0..5.</summary>
    [OriginalFunction(0x57C4B0, OriginalFunctionScope.Partial)]
    public static int OpenSlots(int intelligence) => Math.Clamp(SpellRunRules.MaintainSlots(intelligence), 0, Slots);

    /// <summary>
    /// Adds a run (0x57C290): a spell outside 0..222 is refused; a run already shown in an open slot stays (true); otherwise the first
    /// empty open slot takes it (true) — none free refuses it (false; MG12 fixed). A slot whose run's stop was asked does not count as
    /// showing it, as in the original.
    /// </summary>
    [OriginalFunction(0x57C290, OriginalFunctionScope.Partial)]
    public static (MaintainBar Bar, bool Added) Add(MaintainBar bar, int run, int spell, int intelligence)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (run < 0 || spell is < 0 or >= SpellBound) return (bar, false);
        var open = OpenSlots(intelligence);
        int? free = null;
        for (var i = 0; i < open; i++)
        {
            if (bar[i].State == MaintainSlotState.Shown && bar[i].Run == run) return (bar, true);
            if (bar[i].Run == -1 && free is null) free = i;
        }
        return free is { } slot ? (bar.With(slot, new(run, MaintainSlotState.Shown)), true) : (bar, false);
    }

    /// <summary>Frees the first slot holding <paramref name="run"/> in a state other than empty (0x57C320); −1 or a run not on the
    /// bar changes nothing.</summary>
    [OriginalFunction(0x57C320, OriginalFunctionScope.Partial)]
    public static MaintainBar Remove(MaintainBar bar, int run)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return run != -1 && bar.SlotOf(run) is { } slot ? bar.With(slot, MaintainSlot.Empty) : bar;
    }

    /// <summary>A click on a slot (0x57C370): a slot showing a run turns to <see cref="MaintainSlotState.Stopping"/> and names the run
    /// to interrupt; any other slot (empty, already stopping, outside 0..4) does nothing (null).</summary>
    [OriginalFunction(0x57C370, OriginalFunctionScope.Partial)]
    public static (MaintainBar Bar, int Run)? Stop(MaintainBar bar, int slot)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (slot is < 0 or >= Slots || bar[slot] is not { State: MaintainSlotState.Shown, Run: >= 0 } shown) return null;
        return (bar.With(slot, shown with { State = MaintainSlotState.Stopping }), shown.Run);
    }

    /// <summary>The run a hover on a slot describes (0x57C3F0: only a slot showing its run); null otherwise.</summary>
    [OriginalFunction(0x57C3F0, OriginalFunctionScope.Partial)]
    public static int? Hovered(MaintainBar bar, int slot)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return slot is >= 0 and < Slots && bar[slot] is { State: MaintainSlotState.Shown, Run: >= 0 } shown ? shown.Run : null;
    }

    /// <summary>
    /// The bar after the run rules' notes, in order (the host's side of 0x4604A0 and 0x4604C0): a
    /// <see cref="SpellRunNoteKind.MaintainBarAdd"/> adds its run with its spell from <paramref name="runs"/> (the run still there),
    /// a <see cref="SpellRunNoteKind.MaintainBar"/> removes it. Then a slot whose run <paramref name="runs"/> no longer holds is
    /// emptied (a technical guard: a freed run's id is reused by the next run).
    /// </summary>
    public static MaintainBar Apply(MaintainBar bar, IEnumerable<SpellRunNote> notes, Func<int, SpellRun?> runs, int intelligence)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(runs);
        foreach (var note in notes)
        {
            if (note.Kind == SpellRunNoteKind.MaintainBarAdd && runs(note.Run) is { } run) bar = Add(bar, note.Run, run.Spell, intelligence).Bar;
            else if (note.Kind == SpellRunNoteKind.MaintainBar) bar = Remove(bar, note.Run);
        }
        return Prune(bar, runs);
    }

    /// <summary>Empties every slot whose run <paramref name="runs"/> no longer holds (the technical guard of <see cref="Apply"/>).</summary>
    public static MaintainBar Prune(MaintainBar bar, Func<int, SpellRun?> runs)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(runs);
        for (var i = 0; i < Slots; i++)
            if (bar[i].State != MaintainSlotState.Empty && (bar[i].Run is < 0 or >= SpellRunTable.Slots || runs(bar[i].Run) is null))
                bar = bar.With(i, MaintainSlot.Empty);
        return bar;
    }
}
