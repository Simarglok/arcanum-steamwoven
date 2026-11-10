using System.Globalization;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One interface art drawn at a place of the 800 × 600 screen (its top left).</summary>
public sealed record AreaMessagePlacedArt(int Art, int X, int Y);

/// <summary>
/// ARC-UI-124 (OH12): the original's combat panel on a hovered critter (0x554830, ledger OH12), on the 800 × 600 screen (the message
/// window's origin is (0, 441)). It takes the place of the critter's image while the hero is in combat mode: the bullseye art 582
/// <c>Pen_BullsEye</c> (80 × 89) at (207, 498), and the hit chance in the number font (font art 230, white, centred in a 64-wide
/// rectangle). With a term that made the check harder (flag bits 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x20000, 0x40000) the number
/// is at the top (215, 507) and one 20-high icon for each term stands over the bullseye's lower part, in the table's order in six places
/// (two columns at x 210 and 248, rows at y 525, 545 and 565); any more than six are not drawn. Without such a term the label
/// "To Hit:" (<c>intgame.mes</c> 20, text font 229) is centred at (215, 526) and the number under it at (215, 544). Godot-free.
/// </summary>
public static class AreaCombatPanelLayout
{
    /// <summary>The bullseye art, drawn first (0x554830 at its start, art 0x246).</summary>
    public const int BackgroundArt = 582;
    public const int BackgroundX = 207, BackgroundY = 498;
    /// <summary>The label's message in <c>intgame.mes</c> (0x554A60: entry 0x14), "To Hit:" in the game's text.</summary>
    public const int ToHitMessage = 20;
    /// <summary>The number font's art (0x64C4A0, built at 0x549E71 from art 0xE6), white.</summary>
    public const int NumberFontArt = 230;
    /// <summary>The flag bits that make the panel draw its icons (0x60FC0) and the bits that make the chance 0 (0x20080).</summary>
    public const int IconBits = 0x60FC0, ImpossibleBits = 0x20080;
    public const int MagickTechBit = 0x40000;

    /// <summary>The number's rectangle with icons (0x554830: x 0xD7, y 0x42 of the window) and the layout without them: the label (0xD7, 0x55)
    /// and the number 0x12 lower; each 64 × 64, the text centred across (0x550A10 flag 4).</summary>
    public static readonly (int X, int Y, int Width, int Height) NumberWithIcons = (215, 507, 64, 64), Label = (215, 526, 64, 64),
        NumberUnderLabel = (215, 544, 64, 64);

    /// <summary>The icons by flag bit in the table's order (0x5C732C masks, 0x5C734C arts): <c>Pen_MSR</c> 586 (weapon too heavy),
    /// <c>Pen_Range</c> 588 (out of range), <c>Pen_Perception</c> 587 (distance), <c>Pen_Cover</c> 583, <c>Pen_Light</c> 585, <c>Pen_Injury</c>
    /// 584, <c>BlockedShot</c> 845 and <c>Magic-Tech-Penalty</c> 846.</summary>
    public static IReadOnlyList<(int Flag, int Art)> Icons { get; } =
        [(0x40, 586), (0x80, 588), (0x100, 587), (0x200, 583), (0x400, 585), (0x800, 584), (0x20000, 845), (0x40000, 846)];

    /// <summary>The six places of the icons (0x5C736C, 0x5C7384 and the window's y 441), filled in order.</summary>
    public static IReadOnlyList<(int X, int Y)> Slots { get; } = [(210, 525), (248, 525), (210, 545), (248, 545), (210, 565), (248, 565)];

    /// <summary>The panel's interface art: the bullseye and the icons.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, .. Icons.Select(icon => icon.Art)];

    /// <summary>The panel's flag word for a hit chance: the difficulty's reasons with the executable's own bits, and the magick or
    /// technology penalty (0x40000).</summary>
    public static int Flags(AttackChance chance)
    {
        ArgumentNullException.ThrowIfNull(chance);
        return (int)chance.Reasons | (chance.MagickTechPenalty ? MagickTechBit : 0);
    }

    /// <summary>The panel facts of <paramref name="chance"/> with the label text.</summary>
    public static AreaCombatPanel Panel(AttackChance chance, string label) => new(chance.Percent, Flags(chance), label);

    /// <summary>The number as the panel prints it (<c>%d%%</c>, 0x5C7850): not clamped above.</summary>
    public static string PercentText(int percent) => string.Create(CultureInfo.InvariantCulture, $"{percent}%");

    /// <summary>Whether the panel draws its icons (some bit of <see cref="IconBits"/> is set), else the label above the number.</summary>
    public static bool ShowsIcons(int flags) => (flags & IconBits) != 0;

    /// <summary>The icons the panel draws for <paramref name="flags"/> with their places: the first six of the table's order that are set.</summary>
    public static IReadOnlyList<AreaMessagePlacedArt> PlacedIcons(int flags)
    {
        var placed = new List<AreaMessagePlacedArt>();
        foreach (var (flag, art) in Icons)
        {
            if (placed.Count >= Slots.Count) break;
            if ((flags & flag) == 0) continue;
            placed.Add(new(art, Slots[placed.Count].X, Slots[placed.Count].Y));
        }
        return placed;
    }

    /// <summary>The panel's arts: the bullseye first, then its icons.</summary>
    public static IReadOnlyList<AreaMessagePlacedArt> Arts(AreaCombatPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return [new(BackgroundArt, BackgroundX, BackgroundY), .. ShowsIcons(panel.Flags) ? PlacedIcons(panel.Flags) : []];
    }

    /// <summary>The panel's texts: with icons the number alone at the top; else the label and the number under it.</summary>
    public static IReadOnlyList<AreaMessageText> Texts(AreaCombatPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var number = new AreaMessageText(ShowsIcons(panel.Flags) ? NumberWithIcons : NumberUnderLabel, AreaMessageFont.Number, PercentText(panel.Percent), false,
            Centre: true);
        return ShowsIcons(panel.Flags) ? [number] : [new AreaMessageText(Label, AreaMessageFont.Text, panel.Label, false, Centre: true), number];
    }
}
