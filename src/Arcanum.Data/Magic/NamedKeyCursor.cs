using Arcanum.Data.Audio;

namespace Arcanum.Data.Magic;

/// <summary>Why a named-key value ends the original's process (it exits or crashes); both reader modes refuse it.</summary>
internal enum NamedKeyFault
{
    None,
    /// <summary>A pair value without its opening <c>(</c>: the original logs and exits.</summary>
    MissingParenthesis,
    /// <summary>A name its table does not hold: the original exits, or for the stat table reads a null entry and crashes.</summary>
    UnknownName,
    /// <summary>A flag name its table does not hold, an empty one included: the original exits.</summary>
    UnknownFlag,
    /// <summary>A flag-list separator right before the end of the value: the original reads through a null cursor and crashes.</summary>
    TrailingSeparator,
    /// <summary>A positional value (one without a key) read after the line has ended: the original reads through a null cursor
    /// and crashes.</summary>
    MissingValue,
}

/// <summary>
/// ARC-MAGIC-001: the original's named-key string parser (checked in the 1.0.7.4 executable, ledger "Spell records"), over one
/// MES text with the comma as value delimiter. The cursor starts at the text; a key matches only when it starts, case-sensitively,
/// at the cursor after white space (space, tab, line feed, vertical tab, form feed, carriage return); a key that is not there
/// leaves the cursor where it is, so a key out of the expected order is never read. A value ends at the next comma (the cursor
/// then follows it) or at the end of the text (the cursor is then spent). The text must hold no byte above 0x7F: the original's
/// white-space test reads its class table at a negative index for one (callers refuse such a text first).
/// </summary>
internal sealed class NamedKeyCursor(string text)
{
    private const char Delimiter = ',', PairSeparator = '@';
    private readonly string _text = text;
    private int _at;

    /// <summary>Where the cursor is, or −1 when it is spent.</summary>
    public int Position => _at;
    public NamedKeyFault Fault { get; private set; }
    /// <summary>The token that ended the process, for a refusal.</summary>
    public string? FaultToken { get; private set; }

    /// <summary>The text from the cursor after white space (empty when spent), for strict mode's leftover check.</summary>
    public string Rest()
    {
        if (_at < 0) return "";
        SkipSpace();
        return _text[_at..];
    }

    /// <summary>A named integer: the key, then <see cref="IntValue"/>. Null when the key is not at the cursor.</summary>
    [OriginalFunction(0x532320, OriginalFunctionScope.Full)]
    public int? NamedInt(string key, out string token)
    {
        token = "";
        return Key(key) ? IntValue(out token) : null;
    }

    /// <summary>A named pair <c>(a@b)</c>: the key, white space, then <see cref="PairValue"/>.</summary>
    [OriginalFunction(0x532710, OriginalFunctionScope.Full)]
    public (int First, int Second)? NamedPair(string key, out string token)
    {
        token = "";
        if (!Key(key)) return null;
        SkipSpace();
        return PairValue(out token);
    }

    /// <summary>ARC-MAGIC-017: a named triple <c>(a@b@c)</c>: the key, white space, then <see cref="TripleValue"/>.</summary>
    [OriginalFunction(0x532800, OriginalFunctionScope.Full)]
    public (int First, int Second, int Third)? NamedTriple(string key, out string token)
    {
        token = "";
        if (!Key(key)) return null;
        SkipSpace();
        return TripleValue(out token);
    }

    /// <summary>A named pair <c>(name@b)</c> whose name is looked up in <paramref name="names"/>.</summary>
    [OriginalFunction(0x5328F0, OriginalFunctionScope.Full)]
    public (int First, int Second)? NamedNamePair(string key, IReadOnlyList<string?> names, out string token)
    {
        token = "";
        if (!Key(key)) return null;
        SkipSpace();
        return NamePairValue(names, out token);
    }

    /// <summary>A named value that must be one of <paramref name="names"/>; the index.</summary>
    [OriginalFunction(0x532480, OriginalFunctionScope.Full)]
    public int? NamedName(string key, IReadOnlyList<string?> names, out string token)
    {
        token = "";
        if (!Key(key)) return null;
        SkipSpace();
        return NameValue(names, out token);
    }

    /// <summary>A named flag list whose names stand for bits 0, 1, 2, … in table order.</summary>
    [OriginalFunction(0x532AC0, OriginalFunctionScope.Full)]
    public uint? NamedFlagBits(string key, IReadOnlyList<string> names, out IReadOnlyList<string> tokens, out string value)
    {
        tokens = []; value = "";
        if (!Key(key)) return null;
        SkipSpace();
        return FlagBitsValue(names, out tokens, out value);
    }

    /// <summary>A named flag list whose names stand for the values of <paramref name="values"/>.</summary>
    [OriginalFunction(0x532630, OriginalFunctionScope.Full)]
    public uint? NamedFlags(string key, IReadOnlyList<string> names, IReadOnlyList<uint> values, out IReadOnlyList<string> tokens, out string value)
    {
        tokens = []; value = "";
        if (!Key(key)) return null;
        SkipSpace();
        return FlagsValue(names, values, out tokens, out value);
    }

    /// <summary>A named 64-bit flag list whose names stand for <paramref name="values"/> (the spell target names).</summary>
    [OriginalFunction(0x5329E0, OriginalFunctionScope.Full)]
    public ulong? NamedFlags64(string key, IReadOnlyList<string> names, IReadOnlyList<ulong> values, out IReadOnlyList<string> tokens, out string value)
    {
        tokens = []; value = "";
        if (!Key(key)) return null;
        SkipSpace();
        return Flags64Value(names, values, out tokens, out value);
    }

    /// <summary>A named range <c>a-b</c>: the key, white space, then <see cref="RangeValue"/>.</summary>
    [OriginalFunction(0x532550, OriginalFunctionScope.Full)]
    public (int First, int Second)? NamedRange(string key, out string token)
    {
        token = "";
        if (!Key(key)) return null;
        SkipSpace();
        return RangeValue(out token);
    }

    /// <summary>
    /// <c>a-b</c> up to the delimiter: a is the <c>atoi</c> of the text before the first <c>-</c>, b of the text after it, or a
    /// again when there is no <c>-</c>.
    /// </summary>
    [OriginalFunction(0x531AB0, OriginalFunctionScope.Full)]
    public (int First, int Second)? RangeValue(out string token)
    {
        SkipSpace();
        token = Take(out var next);
        _at = next;
        var dash = token.IndexOf('-');
        var first = SoundSchemeParser.Atoi(dash < 0 ? token : token[..dash]);
        return (first, dash < 0 ? first : SoundSchemeParser.Atoi(token.AsSpan(dash + 1)));
    }

    /// <summary>The integer up to the delimiter, as C <c>atoi</c> reads it. On a spent cursor (a positional value after the
    /// line has ended) it is a fault.</summary>
    [OriginalFunction(0x531990, OriginalFunctionScope.Full)]
    public int? IntValue(out string token)
    {
        token = "";
        if (_at < 0) return Fail<int>(NamedKeyFault.MissingValue, "");
        SkipSpace();
        token = Take(out var next);
        _at = next;
        return SoundSchemeParser.Atoi(token);
    }

    /// <summary>
    /// <c>(a@b)</c> up to the delimiter: a missing <c>(</c> is a fault; a is the <c>atoi</c> of the text after it (up to
    /// <c>@</c>), b of the text after <c>@</c>, or a again when there is no <c>@</c>. The closing parenthesis is never checked.
    /// </summary>
    [OriginalFunction(0x531B70, OriginalFunctionScope.Full)]
    public (int First, int Second)? PairValue(out string token)
    {
        SkipSpace();
        if (!Open(out token)) return null;
        token = Take(out var next);
        _at = next;
        var inner = token.AsSpan(1);
        var at = inner.IndexOf(PairSeparator);
        var first = SoundSchemeParser.Atoi(at < 0 ? inner : inner[..at]);
        return (first, at < 0 ? first : SoundSchemeParser.Atoi(inner[(at + 1)..]));
    }

    /// <summary>
    /// ARC-MAGIC-017: <c>(a@b@c)</c> up to the delimiter, as <see cref="PairValue"/>: a missing <c>(</c> is a fault; a, b and c are
    /// the <c>atoi</c> of the texts after <c>(</c>, after the first <c>@</c> and after the second; without a second <c>@</c> c is a,
    /// without any b and c are a. The closing parenthesis is never checked.
    /// </summary>
    [OriginalFunction(0x531C60, OriginalFunctionScope.Full)]
    public (int First, int Second, int Third)? TripleValue(out string token)
    {
        SkipSpace();
        if (!Open(out token)) return null;
        token = Take(out var next);
        _at = next;
        var inner = token.AsSpan(1);
        var at = inner.IndexOf(PairSeparator);
        var first = SoundSchemeParser.Atoi(at < 0 ? inner : inner[..at]);
        if (at < 0) return (first, first, first);
        var rest = inner[(at + 1)..];
        var second = rest.IndexOf(PairSeparator);
        return (first, SoundSchemeParser.Atoi(second < 0 ? rest : rest[..second]), second < 0 ? first : SoundSchemeParser.Atoi(rest[(second + 1)..]));
    }

    /// <summary>
    /// <c>(name@b)</c>: the name runs from right after <c>(</c> to the first space before <c>@</c> (or the delimiter) and is
    /// compared ASCII case-insensitively with each entry in table order; no match is a fault (a null entry crashes the
    /// original before the end of its table). b as in <see cref="PairValue"/>, or the name's index when there is no <c>@</c>.
    /// </summary>
    [OriginalFunction(0x531D80, OriginalFunctionScope.Full)]
    public (int First, int Second)? NamePairValue(IReadOnlyList<string?> names, out string token)
    {
        SkipSpace();
        if (!Open(out token)) return null;
        token = Take(out var next);
        _at = next;
        var inner = token[1..];
        var at = inner.IndexOf(PairSeparator);
        var nameEnd = at < 0 ? inner.Length : at;
        var space = inner.IndexOf(' ', 0, nameEnd);
        var name = inner[..(space < 0 ? nameEnd : space)];
        var index = Match(names, name);
        if (index < 0) return Fail<(int, int)>(NamedKeyFault.UnknownName, name);
        return (index, at < 0 ? index : SoundSchemeParser.Atoi(inner.AsSpan(at + 1)));
    }

    /// <summary>The whole text up to the delimiter (trailing white space included) compared ASCII case-insensitively with each
    /// name; no match is a fault (a null entry crashes the original before the end of its table), and so is a spent cursor.</summary>
    [OriginalFunction(0x531EE0, OriginalFunctionScope.Full)]
    public int? NameValue(IReadOnlyList<string?> names, out string token)
    {
        token = "";
        if (_at < 0) return Fail<int>(NamedKeyFault.MissingValue, "");
        SkipSpace();
        token = Take(out var next);
        _at = next;
        var index = Match(names, token);
        return index < 0 ? Fail<int>(NamedKeyFault.UnknownName, token) : index;
    }

    /// <summary>A flag list whose names stand for bits (<see cref="Flags"/>).</summary>
    [OriginalFunction(0x532B90, OriginalFunctionScope.Full)]
    public uint? FlagBitsValue(IReadOnlyList<string> names, out IReadOnlyList<string> tokens, out string value) =>
        (uint?)Flags(names, index => 1u << index, out tokens, out value);

    /// <summary>A flag list whose names stand for table values (<see cref="Flags"/>).</summary>
    [OriginalFunction(0x531FB0, OriginalFunctionScope.Full)]
    public uint? FlagsValue(IReadOnlyList<string> names, IReadOnlyList<uint> values, out IReadOnlyList<string> tokens, out string value) =>
        (uint?)Flags(names, index => values[index], out tokens, out value);

    /// <summary>A flag list whose names stand for 64-bit values (<see cref="Flags"/>).</summary>
    [OriginalFunction(0x532160, OriginalFunctionScope.Full)]
    public ulong? Flags64Value(IReadOnlyList<string> names, IReadOnlyList<ulong> values, out IReadOnlyList<string> tokens, out string value) =>
        Flags(names, index => values[index], out tokens, out value);

    /// <summary>
    /// The value up to the delimiter as names separated by a space or <c>|</c>, each OR-ed in. A name ends at whichever comes
    /// first; when a space comes first and a <c>|</c> follows later, the walk resumes after that <c>|</c>, so names between
    /// them are skipped (<c>A B|C</c> reads A and C). A name that matches no entry (an empty one included) is a fault; a
    /// separator that is the last character of the value is a fault (the original's cursor becomes null and is read).
    /// </summary>
    private ulong? Flags(IReadOnlyList<string> names, Func<int, ulong> value, out IReadOnlyList<string> tokens, out string text)
    {
        SkipSpace();
        text = Take(out var next);
        _at = next;
        var read = new List<string>();
        tokens = read;
        ulong flags = 0;
        var cur = 0;
        while (true)
        {
            var space = text.IndexOf(' ', cur);
            var bar = text.IndexOf('|', cur);
            int end, separator;
            if (space < 0 && bar < 0) (end, separator) = (text.Length, -1);
            else if (bar < 0) (end, separator) = (space, space);
            else if (space < 0 || bar < space) (end, separator) = (bar, bar);
            else (end, separator) = (space, bar);
            var name = text[cur..end];
            var index = Match(names, name);
            if (index < 0) return Fail<ulong>(NamedKeyFault.UnknownFlag, name);
            read.Add(name);
            flags |= value(index);
            if (separator < 0) return flags;
            cur = separator + 1;
            if (cur == text.Length) return Fail<ulong>(NamedKeyFault.TrailingSeparator, text);
            while (cur < text.Length && IsSpace(text[cur])) cur++;
        }
    }

    private bool Key(string key)
    {
        if (_at < 0) return false;
        SkipSpace();
        if (!_text.AsSpan(_at).StartsWith(key, StringComparison.Ordinal)) return false;
        _at += key.Length;
        return true;
    }

    private bool Open(out string token)
    {
        token = "";
        if (_at >= 0 && _at < _text.Length && _text[_at] == '(') return true;
        token = Take(out _);
        Fail(NamedKeyFault.MissingParenthesis, token);
        return false;
    }

    /// <summary>The text from the cursor to the delimiter, and where the cursor goes after it (−1 at the end of the text).</summary>
    private string Take(out int next)
    {
        if (_at < 0) { next = -1; return ""; }
        var start = _at;
        var comma = _text.IndexOf(Delimiter, start);
        next = comma < 0 ? -1 : comma + 1;
        return _text[start..(comma < 0 ? _text.Length : comma)];
    }

    private void SkipSpace()
    {
        while (_at >= 0 && _at < _text.Length && IsSpace(_text[_at])) _at++;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static int Match(IReadOnlyList<string?> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
            if (names[i] is { } entry && string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private T? Fail<T>(NamedKeyFault fault, string token) where T : struct
    {
        if (Fault == NamedKeyFault.None) (Fault, FaultToken) = (fault, token);
        _at = -1;
        return null;
    }

    private bool Fail(NamedKeyFault fault, string token)
    {
        Fail<int>(fault, token);
        return false;
    }
}
