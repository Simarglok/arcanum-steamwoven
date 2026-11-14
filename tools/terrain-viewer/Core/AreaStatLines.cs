using System.Globalization;
using Arcanum.Simulation.Combat;
using Arcanum.Session;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1924 (ledger IW46): the weapon stat lines (0x556220) and the armour stat lines (0x5566B0) the inventory windows' hot spots show in the bottom strip's message window.
/// The weapon's: art 675 (0x2A3) at the window's (217, 69), the title <c>intgame.mes</c> 57 with the displayed damage rating (0x464630) as "%s: %d" on the title's rectangle, then
/// three rows of two cells in the text font (the left cell on the row's rectangle, the right cell on its right half, 0x550A10 flags 1 and 0x11): the weapon skill's effectiveness "%s: %d%%" (69)
/// and the normal damage (58), the fatigue damage (59) and the electrical (60), the fire (61) and the poison (62), each "%s: %d-%d" from the weapon's range of that type (0x465F70), "%s: 0" when
/// both ends are 0. The armour's: art 674 (0x2A2), the title 63 with the displayed armour rating (0x464700), the displayed armour class (70) "%s: %d" ("%s: 0" for 0) and the normal
/// resistance (64), the magic (65) and the electrical (66), the fire (67) and the poison (68), each "%s: %d%%" ("%s: 0" for 0). Where the bar is not in the message mode the original writes only
/// the title text through 0x550770. Godot-free.
/// </summary>
public static class AreaStatLines
{
    /// <summary>The arts drawn at the image's place: 675 for the weapon's lines, 674 for the armour's.</summary>
    public const int WeaponArt = 675, ArmourArt = 674;

    /// <summary>The <c>intgame.mes</c> entries of the weapon's lines (0x39 title, 0x3A normal, 0x3B fatigue, 0x3C electrical, 0x3D fire, 0x3E poison, 0x45 skill).</summary>
    public const int WeaponTitleMessage = 57, WeaponNormalMessage = 58, WeaponFatigueMessage = 59, WeaponElectricalMessage = 60, WeaponFireMessage = 61, WeaponPoisonMessage = 62, SkillMessage = 69;

    /// <summary>The entries of the armour's lines (0x3F title, 0x40 normal, 0x41 magic, 0x42 electrical, 0x43 fire, 0x44 poison, 0x46 armour class).</summary>
    public const int ArmourTitleMessage = 63, ArmourNormalMessage = 64, ArmourMagicMessage = 65, ArmourElectricalMessage = 66, ArmourFireMessage = 67, ArmourPoisonMessage = 68, ArmourClassMessage = 70;

    /// <summary>The weapon's lines (0x556220) for <paramref name="ratings"/>; null without the detail the reader gives.</summary>
    public static AreaMessageEntry? Weapon(SessionDollRatings? ratings, AreaGameBarTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (ratings?.Detail is not { } detail) return null;
        string Range(int message, DamageType type) => Plain(texts.Text(message), detail.Ranges[(int)type].Min, detail.Ranges[(int)type].Max);
        return new(AreaMessageKind.StatLines, Title(texts.Text(WeaponTitleMessage), ratings.Damage))
        {
            Icon = WeaponArt,
            StatCells = new(Percent(texts.Text(SkillMessage), detail.SkillEffectiveness), Range(WeaponNormalMessage, DamageType.Normal),
                Range(WeaponFatigueMessage, DamageType.Fatigue), Range(WeaponElectricalMessage, DamageType.Electrical),
                Range(WeaponFireMessage, DamageType.Fire), Range(WeaponPoisonMessage, DamageType.Poison)),
        };
    }

    /// <summary>The armour's lines (0x5566B0) for <paramref name="ratings"/>; null without the detail the reader gives.</summary>
    public static AreaMessageEntry? Armour(SessionDollRatings? ratings, AreaGameBarTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (ratings?.Detail is not { } detail) return null;
        return new(AreaMessageKind.StatLines, Title(texts.Text(ArmourTitleMessage), ratings.Armour))
        {
            Icon = ArmourArt,
            StatCells = new(Value(texts.Text(ArmourClassMessage), detail.ArmorClass), Resistance(texts.Text(ArmourNormalMessage), detail.Normal),
                Resistance(texts.Text(ArmourMagicMessage), detail.Magic), Resistance(texts.Text(ArmourElectricalMessage), detail.Electrical),
                Resistance(texts.Text(ArmourFireMessage), detail.Fire), Resistance(texts.Text(ArmourPoisonMessage), detail.Poison)),
        };
    }

    /// <summary>The title of the lines, also the only text the original writes in another bar mode (0x550770): "%s: %d" (0x5C5D94).</summary>
    public static string Title(string label, int rating) => string.Create(CultureInfo.InvariantCulture, $"{label}: {rating}");

    private static string Value(string label, int value) => value == 0 ? $"{label}: 0" : string.Create(CultureInfo.InvariantCulture, $"{label}: {value}");

    private static string Percent(string label, int value) => string.Create(CultureInfo.InvariantCulture, $"{label}: {value}%");

    private static string Resistance(string label, int value) => value == 0 ? $"{label}: 0" : Percent(label, value);

    private static string Plain(string label, int minimum, int maximum) => minimum == 0 && maximum == 0 ? $"{label}: 0" : string.Create(CultureInfo.InvariantCulture, $"{label}: {minimum}-{maximum}");

    /// <summary>The rectangles of the rows on the 800 × 600 screen (the title's 0x5C70C8 and the rows 0x5C70D8, 0x5C70E8, 0x5C70F8), and the right cell's: the row's right half (0x550A10 flag 0x10).</summary>
    public static readonly (int X, int Y, int Width, int Height) TitleRect = (290, 504, 291, 19);
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Rows { get; } = [(290, 528, 291, 18), (290, 546, 291, 18), (290, 564, 291, 18)];

    /// <summary>The right half of a row's rectangle (the width halved, the left edge moved by it).</summary>
    public static (int X, int Y, int Width, int Height) RightHalf((int X, int Y, int Width, int Height) row) => (row.X + row.Width / 2, row.Y, row.Width / 2, row.Height);

    /// <summary>The texts of the lines for the message window's drawing: the title in the title font, the six cells in the text font, all white.</summary>
    public static IReadOnlyList<AreaMessageText> Texts(AreaMessageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var texts = new List<AreaMessageText> { new(TitleRect, AreaMessageFont.Title, entry.Title, false) };
        if (entry.StatCells is not { } cells) return texts;
        string[] left = [cells.Left1, cells.Left2, cells.Left3], right = [cells.Right1, cells.Right2, cells.Right3];
        for (var row = 0; row < Rows.Count; row++)
        {
            texts.Add(new(Rows[row], AreaMessageFont.Text, left[row], false));
            texts.Add(new(RightHalf(Rows[row]), AreaMessageFont.Text, right[row], false));
        }
        return texts;
    }
}

/// <summary>
/// ARC-UI-1924 (ledger IW46): a stat hot spot of an inventory window was released over (<paramref name="Release"/>, message subtype 0, which first brings the bar to the message mode) or entered
/// (subtype 2): its <paramref name="Target"/> and the ratings the window shows for that party (null when the reader gave none).
/// </summary>
public readonly record struct AreaStatRequest(AreaStatTarget Target, SessionDollRatings? Ratings, bool Release);
