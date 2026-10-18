using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

/// <summary>Where the effect number of a blessing or curse came from (<see cref="BlessingRules.EffectOf"/>).</summary>
public enum BlessingEffectSource
{
    /// <summary>The entry's text is a number.</summary>
    Number,
    /// <summary>The number is below 50: the source reads no entry and its empty text gives effect 0.</summary>
    BelowRange,
    /// <summary>The table has no effect entry for the number: effect 0, as for an empty text.</summary>
    Missing,
    /// <summary>The text starts with a number and continues with other characters, which the source's <c>atoi</c> ignores.</summary>
    Prefix,
    /// <summary>The text does not start with a number: effect 0.</summary>
    NotANumber,
    /// <summary>A number outside the 32-bit range, where <c>atoi</c> is undefined: refused, no effect number.</summary>
    Overflow,
}

/// <summary>The effect number of a blessing or curse and how it was read; <see cref="Effect"/> is null only for
/// <see cref="BlessingEffectSource.Overflow"/>.</summary>
public readonly record struct BlessingEffect(int? Effect, BlessingEffectSource Source);

/// <summary>
/// The module's blessing and curse tables, <c>mes\gamebless.mes</c> and <c>mes\gamecurse.mes</c> (ARC-WORLD-035; read as the
/// original executable's blessing and curse functions read them, ledger BC1–BC6): each number from 50 has a bank of
/// ten entries, <c>number × 10 + 0</c> its name, <c>+ 1</c> its effect number, <c>+ 2</c> its description; entry 1000 is the
/// "received" message. A number below 50 reads no entry.
/// </summary>
public static class BlessingRules
{
    public const int FirstNumber = 50, Bank = 10, NameField = 0, EffectField = 1, DescriptionField = 2, ReceivedMessage = 1000;

    /// <summary>
    /// The effect a blessing or curse adds and removes (0x4C4420, 0x4C3F70): <c>atoi</c> of its effect
    /// entry, so a number below 50, a missing entry or a text without a leading number gives effect 0 (the module's
    /// <c>gameeffect.mes</c> effect 0), as the original does.
    /// </summary>
    [OriginalFunction(0x4C4420, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C3F70, OriginalFunctionScope.Partial)]
    public static BlessingEffect EffectOf(MesFile table, int number)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (number < FirstNumber || number > (int.MaxValue - EffectField) / Bank) return new(0, BlessingEffectSource.BelowRange);
        if (table.Find(number * Bank + EffectField) is not { } message) return new(0, BlessingEffectSource.Missing);
        return Atoi(message.Bytes.Span);
    }

    /// <summary>The numbers from 50 that have an effect entry, ascending (the entries below 1000).</summary>
    public static IReadOnlyList<int> Numbers(MesFile table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Messages.Select(m => m.Number).Where(n => n >= FirstNumber * Bank && n < ReceivedMessage && n % Bank == EffectField)
            .Select(n => n / Bank).Order().ToArray();
    }

    // C atoi: leading white space, an optional sign, then decimal digits up to the first other character.
    private static BlessingEffect Atoi(ReadOnlySpan<byte> text)
    {
        var i = 0;
        while (i < text.Length && text[i] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r') i++;
        var negative = false;
        if (i < text.Length && text[i] is (byte)'+' or (byte)'-') negative = text[i++] == (byte)'-';
        var start = i;
        long value = 0;
        while (i < text.Length && text[i] is >= (byte)'0' and <= (byte)'9')
        {
            value = value * 10 + (text[i++] - (byte)'0');
            if (value > (long)int.MaxValue + 1) return new(null, BlessingEffectSource.Overflow);
        }
        if (i == start) return new(0, BlessingEffectSource.NotANumber);
        var signed = negative ? -value : value;
        if (signed is < int.MinValue or > int.MaxValue) return new(null, BlessingEffectSource.Overflow);
        return new((int)signed, i == text.Length ? BlessingEffectSource.Number : BlessingEffectSource.Prefix);
    }
}
