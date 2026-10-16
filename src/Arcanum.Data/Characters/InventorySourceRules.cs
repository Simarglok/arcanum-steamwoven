using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum InventorySourceStatus
{
    Parsed,
    /// <summary>Not a set the source's lookup returns: below 1, not below the table's entry count, or after the first gap in
    /// the 1..N numbering (the source stops parsing there).</summary>
    Unreachable,
    /// <summary>The entry is not <c>name: min,max rate,proto …</c> as admitted here: no colon, an empty name or one of 70
    /// or more characters, a token that is not two comma-separated digit runs, no coin pair, or more than 300 items.</summary>
    Malformed,
    /// <summary>A byte outside printable ASCII (space included): the source splits only on commas and spaces, so a tab or
    /// line feed would glue a pair to the next one and empty the set.</summary>
    InvalidText,
    /// <summary>The set has a non-empty <c>rules\InvenSourceBuy.mes</c> line. A failing buy line empties the set in the
    /// source; buy lists are not modelled, so any non-empty one is refused.</summary>
    BuyListUnsupported,
}

/// <summary>One item line: chance in percent and the basic prototype number (the prototype file is number + 20).</summary>
public readonly record struct InventorySourceEntry(int Rate, int BasicPrototype);

/// <summary>A parsed set: the coin range and the item lines in order.</summary>
public sealed record InventorySourceSet(int Number, int MinCoins, int MaxCoins, IReadOnlyList<InventorySourceEntry> Items);

public sealed record InventorySourceRead(InventorySourceStatus Status, InventorySourceSet? Set);

/// <summary>
/// <c>rules\InvenSource.mes</c> sets (ARC-RES-027; CE <c>invensource.c</c> at the pinned revision): set N is message N,
/// numbered from 1 without gaps; the lookup accepts <c>0 &lt; N &lt; entry count</c>. An entry is the set name, a colon,
/// then space-separated <c>a,b</c> pairs: the first is the coin range, each further one an item's rate and basic
/// prototype. Project admission policy: a shape the source would empty, truncate or read partially is a typed refusal.
/// Whether a listed prototype exists is the caller's check.
/// </summary>
public static class InventorySourceRules
{
    public const int MaxNameCharacters = 69, MaxItems = 300;

    public static InventorySourceRead Read(MesFile sources, MesFile? buy, int set)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (set < 1 || set >= sources.Count) return new(InventorySourceStatus.Unreachable, null);
        for (var number = 1; number <= set; number++)
            if (sources.Find(number) is null) return new(InventorySourceStatus.Unreachable, null);
        var text = sources.Find(set)!.Bytes.Span;
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e) return new(InventorySourceStatus.InvalidText, null);
        // Only a line of spaces reads as empty in the source; any other byte, a tab included, is a (failing) buy list.
        if (buy?.Find(set) is { } line && line.Bytes.Span.IndexOfAnyExcept((byte)' ') >= 0)
            return new(InventorySourceStatus.BuyListUnsupported, null);

        var entry = System.Text.Encoding.ASCII.GetString(text);
        var colon = entry.IndexOf(':');
        if (colon is < 1 or > MaxNameCharacters) return new(InventorySourceStatus.Malformed, null);
        var pairs = entry[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pairs.Length < 1 || pairs.Length - 1 > MaxItems) return new(InventorySourceStatus.Malformed, null);
        var values = new (int First, int Second)[pairs.Length];
        for (var i = 0; i < pairs.Length; i++)
        {
            var parts = pairs[i].Split(',');
            if (parts.Length != 2 || !Digits(parts[0], out var first) || !Digits(parts[1], out var second))
                return new(InventorySourceStatus.Malformed, null);
            values[i] = (first, second);
        }
        var items = values.Skip(1).Select(pair => new InventorySourceEntry(pair.First, pair.Second)).ToArray();
        return new(InventorySourceStatus.Parsed, new(set, values[0].First, values[0].Second, Array.AsReadOnly(items)));
    }

    private static bool Digits(string token, out int value)
    {
        value = 0;
        return token.Length is >= 1 and <= 9 && token.All(c => c is >= '0' and <= '9') &&
            int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
