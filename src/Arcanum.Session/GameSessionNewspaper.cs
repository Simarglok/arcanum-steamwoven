using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-230 (schema 74, the <c>newspaper</c> section; ledger NQ1–NQ5): the newspaper issue queue of the original, a table of 25 words (0x6876D0) and its count (0x6876CC) that the
/// game writes into its save (0x4BF100) and reads back (0x4BF0C0). A new game sets the first four words to −1, the fifth (the head, the issue shown now) to 5000 and the count to 5
/// (0x4BF080); the rest is zero. The dialog action <c>np</c> queues issues behind the head (0x4BF140). The daily newspaper event that removes the head (0x4BF270, 0x4BF2C0) and
/// the reading window that prints the head are not modelled yet, so a build writes no state a pop leaves: the first four words stay −1 and every word from the count on stays 0, which
/// the load checks. A game of an older schema has the new game's queue.
/// </summary>
public sealed record SessionNewspaperState
{
    /// <summary>The state with a private copy of <paramref name="entries"/> (a caller's array is not kept).</summary>
    public SessionNewspaperState(IReadOnlyList<int> entries, int count)
    {
        Entries = System.Collections.Immutable.ImmutableArray.CreateRange(entries);
        Count = count;
    }

    /// <summary>The 25 words of the table (immutable).</summary>
    public IReadOnlyList<int> Entries { get; }

    /// <summary>The count of the original's table (5 at a new game).</summary>
    public int Count { get; }

    /// <summary>The words of the table.</summary>
    public const int Capacity = 25;

    /// <summary>The index of the head: the words before it are the reserved ones (−1 at a new game).</summary>
    public const int Head = 4;

    /// <summary>The head issue of a new game; an operand below it is an issue number, one from it on a marker that is never looked up.</summary>
    public const int NewGameHead = 5000;

    /// <summary>The queue of a new game.</summary>
    public static SessionNewspaperState NewGame { get; } = new([-1, -1, -1, -1, NewGameHead, .. new int[Capacity - Head - 1]], Head + 1);

    /// <summary>The queue of a new game, the form a missing section means.</summary>
    public static SessionNewspaperState Empty => NewGame;

    /// <summary>Whether the table has the shape a build that cannot pop writes (see the type remarks).</summary>
    public bool IsCanonical =>
        Entries.Count == Capacity && Count is >= Head + 1 and <= Capacity && Entries.Take(Head).All(entry => entry == -1) && Entries.Skip(Count).All(entry => entry == 0);

    public bool Equals(SessionNewspaperState? other) => other is not null && Count == other.Count && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => HashCode.Combine(Count, Entries.Count == 0 ? 0 : Entries[Math.Min(Head, Entries.Count - 1)]);
}

public sealed partial class GameSessionOwner
{
    private SessionNewspaperState _newspaper = SessionNewspaperState.NewGame;

    /// <summary>The newspaper issue queue now (ARC-DEV-230).</summary>
    public SessionNewspaperState QueryNewspaper()
    {
        ThrowIfDisposed();
        return _newspaper;
    }

    /// <summary>
    /// ARC-DEV-230 (0x4BF140, ledger NQ1–NQ3): queues newspaper issue <paramref name="issue"/>. An issue number below 5000 that is already among the words from the head on is left
    /// alone (the zero words past the count count too, so <c>np 0</c> finds the empty slots). With <paramref name="front"/> the words from the head on move up one place (the last one is
    /// lost) and the issue takes the place after the head, the count rising to 25 at most; without it the issue takes the word at the count (the last word once the table is full).
    /// NoChange when nothing is written (a found issue); one revision otherwise.
    /// </summary>
    [OriginalFunction(0x4BF140, OriginalFunctionScope.Full)]
    public GameSessionMutationResult QueueNewspaper(GameSessionVersion expected, int issue, bool front)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var entries = _newspaper.Entries.ToArray();
        var count = _newspaper.Count;
        if (issue < SessionNewspaperState.NewGameHead)
            for (var i = SessionNewspaperState.Head; i < SessionNewspaperState.Capacity; i++)
                if (entries[i] == issue) return NoChange();
        if (front)
        {
            // The loop moves words 4..23 to 5..24 and then the issue overwrites word 5 (the copy of the head is lost, the head stays where it is).
            for (var i = SessionNewspaperState.Capacity - 2; i >= SessionNewspaperState.Head; i--) entries[i + 1] = entries[i];
            entries[SessionNewspaperState.Head + 1] = issue;
            if (count < SessionNewspaperState.Capacity) count++;
        }
        else if (count < SessionNewspaperState.Capacity)
        {
            entries[count] = issue;
            count++;
        }
        else entries[SessionNewspaperState.Capacity - 1] = issue;
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _newspaper = new SessionNewspaperState(entries, count);
        return Changed();
    }

    private SessionNewspaperState CaptureNewspaper() => _newspaper;

    // The shape the codec cannot know: the table as a build that cannot pop writes it.
    private static GameSessionFailure ImportNewspaper(SessionNewspaperState saved) =>
        saved.IsCanonical ? GameSessionFailure.None : GameSessionFailure.InvalidSnapshot;
}
