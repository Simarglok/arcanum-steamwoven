using System.Collections.Immutable;

namespace Arcanum.Simulation.Magic;

/// <summary>Why a spell-book change was or was not made.</summary>
public enum SpellBookStatus
{
    /// <summary>The book changed (or, for a forced learn of a rank already known, was already there: <see cref="SpellBookChange.Changed"/> is false).</summary>
    Done,
    /// <summary>The rank is not the next one of its college (learn) or not its highest known one (unlearn).</summary>
    WrongRank,
    /// <summary>The character level is below the rank's minimum (1, 1, 5, 10, 15).</summary>
    LevelTooLow,
    /// <summary>The effective intelligence is below the spell's minimum (5).</summary>
    IntelligenceTooLow,
    /// <summary>The effective willpower is below the spell's minimum (its record's, or 6, 9, 12, 15, 18 by rank).</summary>
    WillpowerTooLow,
    /// <summary>A college is already mastered (mastery is once).</summary>
    AlreadyMastered,
    /// <summary>The college has fewer than five known ranks.</summary>
    RanksMissing,
    /// <summary>A spell outside 0..79, a college outside 0..15, or a book outside its ranges.</summary>
    Invalid,
}

/// <summary>
/// A critter's spell book: the known rank of each of the sixteen colleges (0..5, in college order) and the mastered college
/// (0..15, or −1 for none). The original keeps both in the critter's spell-and-tech field (indices 0..15 and 16).
/// </summary>
public sealed record SpellBook(ImmutableArray<int> Ranks, int MasteredCollege = SpellBookRules.NoMastery)
{
    public bool Equals(SpellBook? other) => other is not null && MasteredCollege == other.MasteredCollege &&
        (Ranks.IsDefault ? other.Ranks.IsDefault : !other.Ranks.IsDefault && Ranks.SequenceEqual(other.Ranks));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MasteredCollege);
        if (!Ranks.IsDefault) foreach (var rank in Ranks) hash.Add(rank);
        return hash.ToHashCode();
    }

    /// <summary>An empty book (0x4B1530): every rank 0, no mastery.</summary>
    public static SpellBook Empty { get; } = new([.. new int[SpellBookRules.Colleges]]);
}

/// <summary>A change of a spell book: the status, the book after it (the input unless changed), the magick points to add (a learn
/// adds one per rank, an unlearn takes one; the caller's stat setter clamps them to 0..210) and whether anything changed.</summary>
public sealed record SpellBookChange(SpellBookStatus Status, SpellBook Book, int MagickPoints)
{
    public bool Succeeded => Status == SpellBookStatus.Done;
    public bool Changed { get; init; }
}

/// <summary>
/// ARC-MAGIC-004 (magic plan MT3): the one spell-book rule that auto-level, the character editor and the scripts share, as the
/// original's spell functions apply it (0x4B1530–0x4B1CF0; contract docs/world/spell-book-runtime.md, ledger "Spell book",
/// SB1–SB11). A college's ranks are learned strictly in order and unlearned from the highest; each rank adds one magick point
/// (<see cref="SpellCostRules.RankPointCost"/>) and costs one character point (the caller's). Mastery is set once, for a
/// college with all five ranks, and kept for life. Pure functions over explicit values; spell numbers outside 0..79 and
/// colleges outside 0..15 are refused (the original would write past the sixteen ranks, SB9).
/// </summary>
public static class SpellBookRules
{
    public const int Colleges = 16, Ranks = SpellCostRules.Ranks, Spells = Colleges * Ranks, NoMastery = -1;
    /// <summary>The intelligence every rank needs (0x4B1750 → 0x4502E0: a constant).</summary>
    public const int MinimumIntelligence = 5;

    private static readonly int[] MinimumLevels = [1, 1, 5, 10, 15];

    /// <summary>A book the rules admit: sixteen ranks 0..5, mastery −1..15.</summary>
    public static bool IsValid(SpellBook? book) => book is not null && !book.Ranks.IsDefault && book.Ranks.Length == Colleges &&
        book.Ranks.All(rank => rank is >= 0 and <= Ranks) && book.MasteredCollege is >= NoMastery and < Colleges;

    /// <summary>
    /// A critter's book from its stored spell-and-tech values (the 25 values of the critter field reader, of any map): ranks
    /// from indices 0..15, mastery from 16; null when fewer than 17 values or any is outside its range.
    /// </summary>
    public static SpellBook? FromFields(IReadOnlyList<int>? spellTech)
    {
        if (spellTech is null || spellTech.Count < Colleges + 1) return null;
        var book = new SpellBook([.. spellTech.Take(Colleges)], spellTech[Colleges]);
        return IsValid(book) ? book : null;
    }

    /// <summary>The character level a rank needs (0x4B1770): 1, 1, 5, 10, 15 by rank.</summary>
    [OriginalFunction(0x4B1770, OriginalFunctionScope.Partial)]
    public static int MinimumLevel(int spell) => spell is >= 0 and < Spells ? MinimumLevels[spell % Ranks]
        : throw new ArgumentOutOfRangeException(nameof(spell));

    /// <summary>Whether a spell is known (0x4B1950): 0..79 and its rank at most its college's.</summary>
    [OriginalFunction(0x4B1950, OriginalFunctionScope.Partial)]
    public static bool IsKnown(SpellBook book, int spell) => IsValid(book) && spell is >= 0 and < Spells &&
        spell % Ranks + 1 <= book.Ranks[spell / Ranks];

    /// <summary>
    /// Learns <paramref name="spell"/> (0x4B1790). Unforced (auto-level, the editor): the rank must be the next one of its college,
    /// then <paramref name="level"/> ≥ <see cref="MinimumLevel"/>, the effective <paramref name="intelligence"/> ≥ 5 and the
    /// effective <paramref name="willpower"/> ≥ <paramref name="minimumWillpower"/>, in that order; the rank is set and one magick
    /// point added. Forced (prototypes, the cheat key): no check; a rank above the known one is set and the difference added in
    /// magick points; a rank already known changes nothing and succeeds.
    /// </summary>
    [OriginalFunction(0x4B1790, OriginalFunctionScope.Partial)]
    public static SpellBookChange Learn(SpellBook book, int spell, bool force, int level, int intelligence, int willpower, int minimumWillpower)
    {
        if (!IsValid(book) || spell is < 0 or >= Spells) return new(SpellBookStatus.Invalid, book, 0);
        var college = spell / Ranks;
        var rank = spell % Ranks + 1;
        var known = book.Ranks[college];
        if (force)
            return rank <= known ? new(SpellBookStatus.Done, book, 0)
                : new(SpellBookStatus.Done, book with { Ranks = book.Ranks.SetItem(college, rank) }, rank - known) { Changed = true };
        if (rank != known + 1) return new(SpellBookStatus.WrongRank, book, 0);
        if (MinimumLevel(spell) > level) return new(SpellBookStatus.LevelTooLow, book, 0);
        if (MinimumIntelligence > intelligence) return new(SpellBookStatus.IntelligenceTooLow, book, 0);
        if (minimumWillpower > willpower) return new(SpellBookStatus.WillpowerTooLow, book, 0);
        return new(SpellBookStatus.Done, book with { Ranks = book.Ranks.SetItem(college, rank) }, SpellCostRules.RankPointCost(spell)) { Changed = true };
    }

    /// <summary>
    /// Unlearns <paramref name="spell"/> (0x4B19B0): only the highest known rank of its college; one magick point is taken. The
    /// mastered college is kept (mastery is for life, SB6), and nothing is re-validated.
    /// </summary>
    [OriginalFunction(0x4B19B0, OriginalFunctionScope.Partial)]
    public static SpellBookChange Unlearn(SpellBook book, int spell)
    {
        if (!IsValid(book) || spell is < 0 or >= Spells) return new(SpellBookStatus.Invalid, book, 0);
        var college = spell / Ranks;
        var rank = spell % Ranks + 1;
        if (rank != book.Ranks[college]) return new(SpellBookStatus.WrongRank, book, 0);
        return new(SpellBookStatus.Done, book with { Ranks = book.Ranks.SetItem(college, rank - 1) }, -SpellCostRules.RankPointCost(spell)) { Changed = true };
    }

    /// <summary>Whether <paramref name="college"/> can be mastered (0x4B1C70): no college mastered yet and all five ranks known.</summary>
    [OriginalFunction(0x4B1C70, OriginalFunctionScope.Partial)]
    public static SpellBookStatus CanMaster(SpellBook book, int college)
    {
        if (!IsValid(book) || college is < 0 or >= Colleges) return SpellBookStatus.Invalid;
        if (book.MasteredCollege != NoMastery) return SpellBookStatus.AlreadyMastered;
        return book.Ranks[college] >= Ranks ? SpellBookStatus.Done : SpellBookStatus.RanksMissing;
    }

    /// <summary>Masters <paramref name="college"/> (0x4B1CF0, the script action's): set only when <see cref="CanMaster"/> allows it.</summary>
    [OriginalFunction(0x4B1CF0, OriginalFunctionScope.Partial)]
    public static SpellBookChange Master(SpellBook book, int college)
    {
        var status = CanMaster(book, college);
        return status == SpellBookStatus.Done ? new(status, book with { MasteredCollege = college }, 0) { Changed = true } : new(status, book, 0);
    }

    /// <summary>
    /// The stat setter's re-validation of a lower intelligence or willpower (0x4B1B90, 0x4B1C00): every known rank (ranks below each
    /// college's) needs at most <paramref name="value"/> by <paramref name="minimum"/> (5 for intelligence, the willpower minima).
    /// </summary>
    [OriginalFunction(0x4B1B90, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4B1C00, OriginalFunctionScope.Full)]
    public static bool RanksHold(ImmutableArray<int> ranks, Func<int, int> minimum, int value)
    {
        ArgumentNullException.ThrowIfNull(minimum);
        if (ranks.IsDefault || ranks.Length != Colleges) throw new ArgumentException("Sixteen ranks expected.", nameof(ranks));
        for (var college = 0; college < Colleges; college++)
            for (var rank = 0; rank < ranks[college]; rank++)
                if (minimum(college * Ranks + rank) > value) return false;
        return true;
    }
}
