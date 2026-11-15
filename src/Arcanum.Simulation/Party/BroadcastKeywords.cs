namespace Arcanum.Simulation.Party;

/// <summary>
/// The order words of the original's broadcast (ARC-UI-2305, ledger OL3 and BM5): the keywords of <c>mes\broadcast.mes</c> (messages
/// 100, 101, … up to the first missing number) each with the type its entry in <c>Rules\BroadCastRules.mes</c> names. The tables are
/// the player's own data, read by the host; this class holds only the lookup (0x4C3B40) and the build rule (init 0x4C2C10, matching
/// 0x4C2EB0).
/// </summary>
public sealed class BroadcastKeywords
{
    /// <summary>The first keyword message in <c>mes\broadcast.mes</c> and in the rules file (init 0x4C2C10).</summary>
    public const int FirstKeyword = 100;

    /// <summary>The number of type names (messages 0 to 13 of the rules file), the fourteen <see cref="CompanionOrderType"/> values.</summary>
    public const int TypeCount = 14;

    private readonly (string Word, CompanionOrderType Type)[] _entries;

    public BroadcastKeywords(IEnumerable<(string Word, CompanionOrderType Type)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
        foreach (var (word, type) in _entries)
            if (word is null || !Enum.IsDefined(type)) throw new ArgumentException("A keyword needs a word and a defined type.", nameof(entries));
    }

    public int Count => _entries.Length;

    /// <summary>The words with their types, in keyword order.</summary>
    public IReadOnlyList<(string Word, CompanionOrderType Type)> Entries => _entries;

    /// <summary>
    /// The init's build (0x4C2C10, 0x4C2EB0): keyword <c>i</c> has the type whose name (rules file message 0 to 13, the first equal one,
    /// case-insensitive) is the text of the rules file's message 100 + i; a keyword whose name matches none is type none (the original
    /// logs an error and uses 0). A keyword without an entry in <paramref name="keywordTypeNames"/> is type none as well.
    /// </summary>
    [OriginalFunction(0x4C2C10, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C2EB0, OriginalFunctionScope.Full)]
    public static BroadcastKeywords Build(IReadOnlyList<string> words, IReadOnlyList<string> typeNames, IReadOnlyList<string> keywordTypeNames)
    {
        ArgumentNullException.ThrowIfNull(words); ArgumentNullException.ThrowIfNull(typeNames); ArgumentNullException.ThrowIfNull(keywordTypeNames);
        var entries = new List<(string, CompanionOrderType)>(words.Count);
        for (var i = 0; i < words.Count; i++)
        {
            var type = CompanionOrderType.None;
            if (i < keywordTypeNames.Count && keywordTypeNames[i].Length > 0)
                for (var t = 0; t < Math.Min(typeNames.Count, TypeCount); t++)
                    if (string.Equals(keywordTypeNames[i], typeNames[t], StringComparison.OrdinalIgnoreCase)) { type = (CompanionOrderType)t; break; }
            entries.Add((words[i], type));
        }
        return new(entries);
    }

    /// <summary>
    /// The order word's type (0x4C3B40): leading spaces (only the space character) skipped, then the whole rest compared
    /// case-insensitively with each keyword in order, the first equal one winning; no match is <see cref="CompanionOrderType.None"/>.
    /// A comma, a colon, trailing spaces or a second word make no match.
    /// </summary>
    [OriginalFunction(0x4C3B40, OriginalFunctionScope.Full)]
    public CompanionOrderType Lookup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var word = text.TrimStart(' ');
        foreach (var entry in _entries)
            if (string.Equals(word, entry.Word, StringComparison.OrdinalIgnoreCase)) return entry.Type;
        return CompanionOrderType.None;
    }
}
