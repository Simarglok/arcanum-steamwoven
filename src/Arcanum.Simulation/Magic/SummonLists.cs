namespace Arcanum.Simulation.Magic;

/// <summary>
/// ARC-MAGIC-053 (E1-e of the missing-systems plan; ledger SUM6, MG9): the six summon lists of the Summon component's <c>List:</c> (the table at 0x5B0ED8 of the retail 1.0.7.4 executable, read from its data: a count, an
/// entries pointer and a flag per list; an entry is a weight or threshold and a basic prototype number, the file is the number + 20). The flag picks the rule of the pick function (0x452900): <b>0</b> weighted (one draw over the
/// weights), <b>odd</b> by aptitude (the first entry whose threshold is not below the run's captured parent aptitude, the last threshold being 1000 so one is always found). Entries are values of the retail data: numbers
/// only, no text. Lists 1 and 2 are named by no installed record (SC12); list 0 is spell 183's, 3 the Familiar's (74), 4 Succour Beast's (53) and 5 Summon Undead's (57).
/// </summary>
public static class SummonLists
{
    /// <summary>One entry: its weight (weighted list) or threshold (aptitude list) and its basic prototype number.</summary>
    public readonly record struct Entry(int Value, int Prototype);

    /// <summary>The number of lists.</summary>
    public const int Count = 6;

    private static readonly (bool ByAptitude, Entry[] Entries)[] Lists =
    [
        (false, [new(20, 28319), new(1, 28360), new(19, 28329), new(20, 28330), new(20, 28332), new(20, 28334)]),
        (false, [new(20, 28324), new(20, 28333), new(20, 28331), new(20, 28333), new(20, 28335), new(20, 28336)]),
        (false, [new(99, 28343), new(1, 28360)]),
        (true, [new(25, 27371), new(50, 27372), new(75, 27373), new(1000, 27374)]),
        (true, [new(20, 28319), new(40, 28332), new(55, 28334), new(70, 28330), new(95, 28329), new(1000, 28360)]),
        (true, [new(20, 28468), new(60, 28469), new(85, 28470), new(1000, 28471)]),
    ];

    /// <summary>Whether <paramref name="list"/> is a list of the table.</summary>
    public static bool Exists(int list) => list is >= 0 and < Count;

    /// <summary>The entries of a list, or none for a number outside the table.</summary>
    public static IReadOnlyList<Entry> EntriesOf(int list) => Exists(list) ? Lists[list].Entries : [];

    /// <summary>Whether a list is picked by the caster's aptitude (an odd flag word) and not by weight.</summary>
    public static bool ByAptitude(int list) => Exists(list) && Lists[list].ByAptitude;

    /// <summary>The sum of a weighted list's weights (what one draw spans).</summary>
    public static int Total(int list) => Exists(list) ? Lists[list].Entries.Sum(e => e.Value) : 0;

    /// <summary>
    /// The basic prototype number the pick function (0x452900) chooses from <paramref name="list"/>, or null for a number outside the table. A weighted list takes one draw from <paramref name="draw"/> over
    /// 0..total−1 (the entry whose cumulative weight passes it; MG9: the original draws 0..100 over a total of 100 or 120, so a draw of 100 falls off the end and takes entry 0 with a log line, and a
    /// total of 120 can never reach the last entries fully: a technical bug the owner rule fixes in both modes, ledger MG9); an aptitude list takes the first entry whose threshold is not below
    /// <paramref name="aptitude"/>, the run's captured parent aptitude (+0x8C, 100 under a PC's armed "spell at maximum" fate), and draws nothing.
    /// </summary>
    public static int? Pick(int list, int aptitude, Func<int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!Exists(list)) return null;
        var (byAptitude, entries) = Lists[list];
        if (byAptitude)
        {
            foreach (var entry in entries) if (aptitude <= entry.Value) return entry.Prototype;
            return entries[^1].Prototype;
        }
        var roll = draw(0, Total(list) - 1);
        foreach (var entry in entries)
        {
            if (roll < entry.Value) return entry.Prototype;
            roll -= entry.Value;
        }
        return entries[^1].Prototype;
    }
}
