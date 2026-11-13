using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

/// <summary>The willpower minima of the 80 spells, or a refusal naming the first record that failed.</summary>
public sealed record SpellWillpowerRead(ExperienceTableStatus Status, IReadOnlyList<int>? Minima, int? Number = null);

/// <summary>
/// The data of the auto-level schemes (ARC-WORLD-036): the rule text of a scheme number as the source looks it up
/// (0x4A6E40: 1..49 from <c>rules\userlevel.mes</c>, 50..199 from <c>rules\gamelevel.mes</c>, nothing for 0 or
/// any other number or a number without an entry) and the willpower each spell needs (<c>rules\spelllist.mes</c>: the
/// <c>IQ:</c> key that starts the second line of spell <c>n</c>'s record, entry <c>1001 + 50n</c>; 6, 9, 12, 15, 18 by rank
/// without it). Bytes only; the rule grammar is the Simulation's <c>AutoLevelRules.Parse</c>.
/// </summary>
public static class AutoLevelTables
{
    public const int FirstUserScheme = 1, FirstGameScheme = 50, LastScheme = 199, Spells = 80;
    /// <summary>ARC-UI-1742: the name files of the schemes (<c>mes\userlevelname.mes</c> for 1..49, <c>mes\gamelevelname.mes</c> for 0 and 50..199).</summary>
    public const string UserNamesPath = "mes/userlevelname.mes", GameNamesPath = "mes/gamelevelname.mes",
        UserRulesPath = "rules/userlevel.mes", GameRulesPath = "rules/gamelevel.mes";
    private const int SpellBase = 1000, SpellStride = 50;
    private static readonly int[] DefaultWillpower = [6, 9, 12, 15, 18];

    /// <summary>The rule text of <paramref name="scheme"/>, or null when the source finds none.</summary>
    [OriginalFunction(0x4A6E40, OriginalFunctionScope.Full)]
    public static ReadOnlyMemory<byte>? Rule(MesFile game, MesFile user, int scheme)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(user);
        if (scheme is < FirstUserScheme or > LastScheme) return null;
        var file = scheme < FirstGameScheme ? user : game;
        return file.Find(scheme)?.Bytes;
    }

    /// <summary>
    /// ARC-UI-1742: the name text of <paramref name="scheme"/> as the sheet's scheme page looks it up (0x4A6DE0): scheme 0 and 50..199 from
    /// <c>mes\gamelevelname.mes</c>, 1..49 from <c>mes\userlevelname.mes</c>, nothing for any other number or a number without an entry. The
    /// function returns the text's pointer (the earlier reading, FT8, took it for the number), and a stored empty text is a name.
    /// </summary>
    [OriginalFunction(0x4A6DE0, OriginalFunctionScope.Full)]
    public static ReadOnlyMemory<byte>? Name(MesFile gameNames, MesFile userNames, int scheme)
    {
        ArgumentNullException.ThrowIfNull(gameNames);
        ArgumentNullException.ThrowIfNull(userNames);
        if (scheme is < 0 or > LastScheme) return null;
        var file = scheme is >= FirstUserScheme and < FirstGameScheme ? userNames : gameNames;
        return file.Find(scheme)?.Bytes;
    }

    /// <summary>
    /// ARC-UI-1742: the scheme numbers the sheet's scheme page lists, in order: every number 0..199 whose name <see cref="Name"/> finds
    /// (0x55D060 fills its list from 0 up and skips a number without a name).
    /// </summary>
    public static IReadOnlyList<int> NamedSchemes(MesFile gameNames, MesFile userNames) =>
        Array.AsReadOnly(Enumerable.Range(0, LastScheme + 1).Where(scheme => Name(gameNames, userNames, scheme) is not null).ToArray());

    /// <summary>
    /// The willpower minimum of each spell 0..79. A record line that starts (after spaces) with <c>IQ:</c> must give one number
    /// before the next comma (optional spaces and sign, 1..9 digits), or the table is refused with that entry's number; a line
    /// without the key, or a missing line, keeps the rank default.
    /// </summary>
    public static SpellWillpowerRead ReadSpellWillpower(MesFile spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        var minima = new int[Spells];
        for (var spell = 0; spell < Spells; spell++)
        {
            minima[spell] = DefaultWillpower[spell % 5];
            var number = SpellBase + SpellStride * spell + 1;
            if (spells.Find(number) is not { } message) continue;
            var text = System.Text.Encoding.Latin1.GetString(message.Bytes.Span).TrimStart(' ', '\t', '\n', '\r');
            if (!text.StartsWith("IQ:", StringComparison.Ordinal)) continue;
            var end = text.IndexOf(',');
            var value = (end < 0 ? text[3..] : text[3..end]).Trim(' ', '\t');
            var digits = value.Length > 0 && value[0] is '+' or '-' ? value[1..] : value;
            if (digits.Length is < 1 or > 9 || digits.Any(c => c is < '0' or > '9'))
                return new(ExperienceTableStatus.InvalidNumber, null, number);
            minima[spell] = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) * (value[0] == '-' ? -1 : 1);
        }
        return new(ExperienceTableStatus.Read, Array.AsReadOnly(minima));
    }
}
