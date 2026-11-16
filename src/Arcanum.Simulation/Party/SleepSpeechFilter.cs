namespace Arcanum.Simulation.Party;

/// <summary>
/// The speech-state table's one entry, "sleeping" (ARC-WORLD-2310, ledger BM5f; exe table 0x5BCEB8, the walk 0x4F6840, the predicate 0x4F6B70, the transform 0x4F6BB0, the
/// init 0x4F69D0). Every typed order line is copied through the table before it is floated over the speaker and read for the follower walk: a speaker that is unconscious
/// or sleeping speaks the transformed text. The words are the player's own <c>mes\text_filter_sleep.mes</c> (read by the host): message 0 is the filler string, messages 1,
/// 2, … up to the first missing number are the words kept as typed. The filler counter is one global of the original, written only by the transform, never reset (not by a
/// new message, not by a load): one counter lives in each filter object, and the host keeps one filter for the whole run (used from the host's own thread only).
/// </summary>
public sealed class SleepSpeechFilter
{
    private readonly string _filler;
    private readonly string[] _keepWords;
    private int _counter;

    /// <param name="filler">Message 0, at least one character (the original divides by its length).</param>
    /// <param name="keepWords">Messages 1, 2, … in order; an empty word is dropped (in the original it would match without consuming and loop for ever).</param>
    public SleepSpeechFilter(string filler, IEnumerable<string> keepWords)
    {
        ArgumentNullException.ThrowIfNull(filler); ArgumentNullException.ThrowIfNull(keepWords);
        if (filler.Length == 0) throw new ArgumentException("The filler needs at least one character.", nameof(filler));
        _filler = filler;
        _keepWords = keepWords.Where(word => word is { Length: > 0 }).ToArray();
    }

    /// <summary>The kept words in order.</summary>
    public IReadOnlyList<string> KeepWords => _keepWords;

    /// <summary>The next filler index (the original's global counter); not saved, as it is a global of the process.</summary>
    public int Counter => _counter;

    /// <summary>
    /// The entry's predicate (0x4F6B70): the speaker is unconscious (0x45D800: not undead and no fatigue left) or asleep (0x45D870: the critter flag 0x1000, only for a PC
    /// or an NPC). The original's test also holds for an object that is not a critter, which the typing hero never is.
    /// </summary>
    [OriginalFunction(0x4F6B70, OriginalFunctionScope.Full)]
    public static bool Applies(bool unconscious, bool sleeping) => unconscious || sleeping;

    /// <summary>The table walk (0x4F6840): the transformed text when the speaker's state is the entry's, else the text as typed.</summary>
    [OriginalFunction(0x4F6840, OriginalFunctionScope.Full)]
    public string Speech(string text, bool unconscious, bool sleeping)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Applies(unconscious, sleeping) ? Transform(text) : text;
    }

    /// <summary>
    /// The transform (0x4F6BB0), same length as the text: a character that is not a letter (the C locale's letters, A–Z and a–z) is copied; at a letter every kept word in
    /// order is tried case-insensitively against the text from there (a prefix, no word boundary), and the first that fits is copied as typed and the scan goes on after
    /// it; otherwise the letter becomes the filler character at the counter, which then steps on, wrapping at the filler's length. There is no random element. A kept word that
    /// ends exactly where the text ends is not read by the original in defined form (its compare runs past both terminators into whatever the heap holds), so no value is
    /// recorded for it; here it is not kept (its letters take the filler), as a read beyond the text cannot be reproduced.
    /// </summary>
    [OriginalFunction(0x4F6BB0, OriginalFunctionScope.Partial)]
    public string Transform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new char[text.Length];
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (!char.IsAsciiLetter(c)) { result[i++] = c; continue; }
            var kept = 0;
            foreach (var word in _keepWords)
            {
                // The word must fit and the text must go on after it (the original's compare only stops on a differing character).
                if (i + word.Length < text.Length && string.Compare(text, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0) { kept = word.Length; break; }
            }
            if (kept > 0) { text.CopyTo(i, result, i, kept); i += kept; continue; }
            result[i++] = _filler[_counter];
            _counter = (_counter + 1) % _filler.Length;
        }
        return new string(result);
    }
}
