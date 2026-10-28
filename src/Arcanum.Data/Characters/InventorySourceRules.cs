using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum InventorySourceStatus
{
    Parsed,
    /// <summary>Not a set the source's lookup returns: below 1, above the table's entry count, or at or after the first gap in
    /// the 1..N numbering (the source stops parsing there). ARC-WORLD-093: the last set is reachable (BT9).</summary>
    Unreachable,
    /// <summary>The entry is not <c>name: min,max rate,proto …</c> as admitted here: no colon, an empty name or one of 70
    /// or more characters, a token that is not two comma-separated digit runs, no coin pair, or more than 300 items.</summary>
    Malformed,
    /// <summary>A byte outside printable ASCII (space included): the source splits only on commas and spaces, so a tab or
    /// line feed would glue a pair to the next one and empty the set.</summary>
    InvalidText,
    /// <summary>ARC-WORLD-093: the source empties the set's items and buy list because its <c>rules\InvenSourceBuy.mes</c> line or its
    /// loot line fails (<see cref="InventorySourceBuyStatus.Emptied"/>, BT8, BT9); the coin pair the loot line read before the failure
    /// stays (uninitialised when it failed first, RK6). A set with a buy line that parses is read.</summary>
    Emptied,
}

/// <summary>One item line: chance in percent and the basic prototype number (the prototype file is number + 20).</summary>
public readonly record struct InventorySourceEntry(int Rate, int BasicPrototype);

/// <summary>A parsed set: the coin range and the item lines in order.</summary>
public sealed record InventorySourceSet(int Number, int MinCoins, int MaxCoins, IReadOnlyList<InventorySourceEntry> Items);

public sealed record InventorySourceRead(InventorySourceStatus Status, InventorySourceSet? Set);

/// <summary>ARC-WORLD-096: how an emptied set's coin pair reads.</summary>
public enum InventorySourceCoinsStatus
{
    /// <summary>The pair was read before the loot line failed.</summary>
    Read,
    /// <summary>The loot line failed before the pair, or the set is unreachable: the original's pair is uninitialised.</summary>
    Undefined,
    /// <summary>A pair the project does not model (an overflowing side, text outside printable ASCII).</summary>
    Unsupported,
}

/// <summary>ARC-WORLD-096: an emptied set's coin pair (<see cref="InventorySourceRules.LootCoins"/>).</summary>
public readonly record struct InventorySourceCoins(InventorySourceCoinsStatus Status, int Min, int Max);

/// <summary>
/// <c>rules\InvenSource.mes</c> sets (ARC-RES-027, docs/research/starting-hero-evidence.md): set N is message N,
/// numbered from 1 without gaps; the lookup accepts <c>0 &lt; N ≤ entry count</c> (0x4BF5D0; ARC-WORLD-093 corrects the earlier
/// "below the count", ledger BT9, LM13). An entry is the set name, a colon,
/// then space-separated <c>a,b</c> pairs: the first is the coin range, each further one an item's rate and basic
/// prototype. Project admission policy: a shape the source would empty, truncate or read partially is a typed refusal.
/// A loot or buy number without a prototype (<c>prototypeExists</c>) empties the set (<see cref="InventorySourceStatus.Emptied"/>);
/// the prototype's own shape is the caller's check.
/// </summary>
public static class InventorySourceRules
{
    public const int MaxNameCharacters = 69, MaxItems = 300;

    /// <summary>The set <paramref name="set"/>. <paramref name="prototypeExists"/> answers whether a basic prototype number has a prototype
    /// (the original's handle lookup, which the buy and loot lines' checks use).</summary>
    public static InventorySourceRead Read(MesFile sources, MesFile? buy, int set, Func<int, bool> prototypeExists)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(prototypeExists);
        // The set loop's reach, as InventorySourceBuyRules reads it (BT8): message n must be the n-th message.
        if (set < 1 || set > sources.Count) return new(InventorySourceStatus.Unreachable, null);
        for (var number = 1; number <= set; number++)
            if (sources.Messages[number - 1].Number != number) return new(InventorySourceStatus.Unreachable, null);
        var text = sources.Messages[set - 1].Bytes.Span;
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e) return new(InventorySourceStatus.InvalidText, null);
        // A failing buy line or loot line empties the whole set in the source (BT8, BT9).
        if (InventorySourceBuyRules.Read(sources, buy, set, prototypeExists).Status == InventorySourceBuyStatus.Emptied)
            return new(InventorySourceStatus.Emptied, null);

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

    /// <summary>
    /// ARC-WORLD-096: the coin pair a set the load emptied (<see cref="InventorySourceStatus.Emptied"/>) keeps (RK6): what the loot-line
    /// parse read before it failed (<see cref="InventorySourceBuyRules.LootLineCoins"/>). An unreachable set reads none
    /// (<see cref="InventorySourceCoinsStatus.Undefined"/>); text outside printable ASCII is <see cref="InventorySourceCoinsStatus.Unsupported"/>.
    /// </summary>
    public static InventorySourceCoins LootCoins(MesFile sources, int set)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (set < 1 || set > sources.Count) return new(InventorySourceCoinsStatus.Undefined, 0, 0);
        for (var number = 1; number <= set; number++)
            if (sources.Messages[number - 1].Number != number) return new(InventorySourceCoinsStatus.Undefined, 0, 0);
        var text = sources.Messages[set - 1].Bytes.Span;
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e) return new(InventorySourceCoinsStatus.Unsupported, 0, 0);
        return InventorySourceBuyRules.LootLineCoins(text);
    }

    private static bool Digits(string token, out int value)
    {
        value = 0;
        return token.Length is >= 1 and <= 9 && token.All(c => c is >= '0' and <= '9') &&
            int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
