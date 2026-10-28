using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum InventorySourceBuyStatus
{
    /// <summary>No buy list: no reachable set, a set without a buy line, or an empty line (spaces only). Buys nothing.</summary>
    None,
    /// <summary>The line's first token is <c>ALL</c> in any case: buys every item.</summary>
    Everything,
    /// <summary>The line lists basic prototype numbers: buys exactly those.</summary>
    Listed,
    /// <summary>The original empties the whole set (its loot too): a line of 2000 or more characters, a 301st number, a number outside
    /// the basic prototype ranges or 0, or one without a prototype, or a set whose loot line does not parse. Buys nothing.</summary>
    Emptied,
}

/// <summary>A set's buy list: whether it buys everything, or the basic prototype numbers it buys.</summary>
public sealed record InventorySourceBuyList(InventorySourceBuyStatus Status, IReadOnlyList<int> BasicPrototypes)
{
    public static InventorySourceBuyList Nothing { get; } = new(InventorySourceBuyStatus.None, Array.Empty<int>());

    /// <summary>The dealer test of one item by its basic prototype number (0x464200: the prototype number less 20).</summary>
    [OriginalFunction(0x464200, OriginalFunctionScope.Partial)]
    public bool Deals(int basicPrototype) => Status == InventorySourceBuyStatus.Everything ||
        Status == InventorySourceBuyStatus.Listed && BasicPrototypes.Contains(basicPrototype);
}

/// <summary>
/// ARC-UI-050: the merchant buy lists of <c>rules\InvenSourceBuy.mes</c> as the original's inventory-source load reads them
/// (0x4BF640, 0x4BFAA0; ledger BT8): the line of set N is message N (order and gaps in the buy file do not matter); its first
/// space-separated token upper-cased equal to <c>ALL</c> buys everything; otherwise every space-separated token is read as a C
/// <c>atoi</c> number and must be a basic prototype number of the original's ranges (0x468610) whose prototype exists, at most 300.
/// Any failure empties the set, as does a failing loot line of the set (<see cref="LootLineFails"/>, which skips the buy line). A set
/// the loot loop never reaches (below 1, above the loot file's message count, or past its first numbering gap) has no list.
/// </summary>
public static class InventorySourceBuyRules
{
    public const int MaxItems = 300, MaxLineCharacters = 1999;

    // The basic prototype ranges of 0x468610, half-open.
    private static readonly (int First, int End)[] Ranges =
    [
        (1000, 1008), (2008, 2023), (3023, 3061), (4006, 4051), (5028, 5029), (6029, 6179), (7038, 7042), (8042, 8293), (9056, 9057),
        (10057, 10145), (11059, 11143), (12060, 12063), (13061, 13062), (14062, 14130), (15065, 15213), (16066, 16079), (17067, 17317),
        (26000, 26008), (27309, 27394), (28310, 28472),
    ];

    /// <summary>Whether <paramref name="number"/> lies in one of the original's basic prototype ranges.</summary>
    public static bool InBasicRange(int number) => Ranges.Any(range => number >= range.First && number < range.End);

    /// <summary>The buy list of <paramref name="set"/>. <paramref name="prototypeExists"/> answers whether a basic prototype number has a
    /// prototype (the original's handle lookup).</summary>
    [OriginalFunction(0x4BF640, OriginalFunctionScope.Partial)]
    public static InventorySourceBuyList Read(MesFile sources, MesFile? buy, int set, Func<int, bool> prototypeExists)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(prototypeExists);
        // The set loop walks the loot file while message n is the n-th message; the lookup takes 1..count (0x4BF5D0).
        if (set < 1 || set > sources.Count) return InventorySourceBuyList.Nothing;
        for (var number = 1; number <= set; number++)
            if (sources.Messages[number - 1].Number != number) return InventorySourceBuyList.Nothing;
        if (LootLineFails(sources.Messages[set - 1].Bytes.Span, prototypeExists)) return new(InventorySourceBuyStatus.Emptied, Array.Empty<int>());
        if (buy?.Find(set) is not { } line) return InventorySourceBuyList.Nothing;
        return ParseLine(line.Bytes.Span, prototypeExists);
    }

    /// <summary>
    /// Whether the original's loot-line parse fails (0x4BF7B0), which empties the set and skips its buy line: a line of 2000 or more
    /// characters, no name (an empty line or only colons), a name of 70 or more characters, a pair without its right side, a 301st item,
    /// or an item number outside the basic prototype ranges or without a prototype. The coin pair and the rates are not checked.
    /// </summary>
    [OriginalFunction(0x4BF7B0, OriginalFunctionScope.Partial)]
    public static bool LootLineFails(ReadOnlySpan<byte> line, Func<int, bool> prototypeExists)
    {
        ArgumentNullException.ThrowIfNull(prototypeExists);
        if (line.Length > MaxLineCharacters) return true;
        var tokens = new CTokenizer(System.Text.Encoding.Latin1.GetString(line));
        if (tokens.Next(":") is not { } name) return true;
        if (name.Length >= 70) return true;
        var items = -1;
        while (tokens.Next(",") is not null)
        {
            if (items == MaxItems) return true;
            if (tokens.Next(" ") is not { } right) return true;
            if (items >= 0 && (Atoi(right) is not { } number || !InBasicRange(number) || !prototypeExists(number))) return true;
            items++;
        }
        return false;
    }

    // The C runtime's strtok over one string: each call skips leading delimiters, takes the token up to the next delimiter and
    // continues after it; null when nothing is left.
    private sealed class CTokenizer(string text)
    {
        private int _at;

        public string? Next(string delimiters)
        {
            while (_at < text.Length && delimiters.Contains(text[_at])) _at++;
            if (_at >= text.Length) return null;
            var start = _at;
            while (_at < text.Length && !delimiters.Contains(text[_at])) _at++;
            var token = text[start.._at];
            if (_at < text.Length) _at++;
            return token;
        }
    }

    /// <summary>One buy line (0x4BFAA0).</summary>
    [OriginalFunction(0x4BFAA0, OriginalFunctionScope.Full)]
    public static InventorySourceBuyList ParseLine(ReadOnlySpan<byte> line, Func<int, bool> prototypeExists)
    {
        ArgumentNullException.ThrowIfNull(prototypeExists);
        if (line.Length > MaxLineCharacters) return new(InventorySourceBuyStatus.Emptied, Array.Empty<int>());
        var tokens = System.Text.Encoding.Latin1.GetString(line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return InventorySourceBuyList.Nothing;
        // _strupr upper-cases a..z only.
        if (string.Equals(tokens[0], "ALL", StringComparison.OrdinalIgnoreCase) && tokens[0].All(c => c < 0x80))
            return new(InventorySourceBuyStatus.Everything, Array.Empty<int>());
        if (tokens.Length > MaxItems) return new(InventorySourceBuyStatus.Emptied, Array.Empty<int>());
        var numbers = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var number = Atoi(tokens[i]);
            if (number is not { } value || !InBasicRange(value) || !prototypeExists(value))
                return new(InventorySourceBuyStatus.Emptied, Array.Empty<int>());
            numbers[i] = value;
        }
        return new(InventorySourceBuyStatus.Listed, Array.AsReadOnly(numbers));
    }

    // The C runtime's atoi: leading white space, an optional sign, then digits; anything else stops it (no digits read as 0). A value
    // past 32 bits is null here (no basic prototype number is that large).
    private static int? Atoi(string token)
    {
        var i = 0;
        while (i < token.Length && token[i] is '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = false;
        if (i < token.Length && token[i] is '+' or '-') negative = token[i++] == '-';
        long value = 0;
        for (; i < token.Length && token[i] is >= '0' and <= '9'; i++)
        {
            value = value * 10 + (token[i] - '0');
            if (value > int.MaxValue) return null;
        }
        return (int)(negative ? -value : value);
    }
}
