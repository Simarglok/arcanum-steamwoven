using System.Globalization;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-076: the original look of the character sheet's skills page and tech page, read from the retail 1.0.7.4 executable (ledger
/// CHW10–CHW16). Both pages are windows of 235 × 325 at the main window's (503, 63); every coordinate here is <b>panel-relative</b> (the
/// exe's text tables hold screen coordinates, the drawer 0x55B880 subtracts the panel's origin). Neither page has a scrollbar, a pager or
/// an unspent-points line. Godot-free.
/// </summary>
public static class AreaCharacterSheetPagesLayout
{
    public const int SkillsBackgroundArt = 29, TechBackgroundArt = 30, GroupTabArtBase = 302, DisciplineArtBase = 306, GaugeArt = 623,
        SkillPlusArt = 624, SkillMinusArt = 625, TechPlusArt = 647, TechMinusArt = 648, IconsFontArt = 300, NickFontArt = 301, GarmondFontArt = 483;
    public const int Groups = 4, GroupRows = 4, Disciplines = 8, Degrees = 7, GaugeWidth = 120, GaugeHeight = 27, PixelsPerLevel = 6;

    /// <summary>The learned-degree icon of each discipline (table 0x5C8FA8): herbology, chemistry, electrical, explosives, gunsmithy,
    /// mechanical, smithy, therapeutics.</summary>
    private static readonly int[] DegreeIcons = [653, 649, 650, 651, 652, 654, 655, 656];

    public static IReadOnlyList<int> ArtNumbers { get; } =
    [
        SkillsBackgroundArt, TechBackgroundArt, .. Enumerable.Range(GroupTabArtBase, Groups), .. Enumerable.Range(DisciplineArtBase, Disciplines),
        GaugeArt, SkillPlusArt, SkillMinusArt, TechPlusArt, TechMinusArt, .. DegreeIcons.Order(), IconsFontArt, NickFontArt, GarmondFontArt,
    ];

    public static int DegreeIcon(int discipline) => DegreeIcons[discipline];

    // ---- the skills page (window 0x64CA6C, art 29, callback 0x55D3A0) ----

    /// <summary>A group tab's top-left (table 0x5C82B0): combat, thieving, social, technological.</summary>
    public static (int X, int Y) GroupTab(int group) => (new[] { 21, 75, 129, 185 }[group], 17);

    /// <summary>Row <paramref name="row"/> (0..3) of a group: the y of its name (63 + 66 row).</summary>
    public static int RowY(int row) => 63 + 66 * row;

    public static (int X, int Y) SkillMinus(int row) => (18, 84 + 66 * row);

    public static (int X, int Y) SkillPlus(int row) => (192, 84 + 66 * row);

    /// <summary>The level gauge's rectangle at full level; its drawn part is <see cref="GaugeVisible"/> wide.</summary>
    public static (int X, int Y) Gauge(int row) => (59, 87 + 66 * row);

    /// <summary>The drawn width of the gauge art for an effective level: 6 pixels a level, nothing at level 0 (0x55BF20).</summary>
    public static int GaugeVisible(int level) => Math.Clamp(level, 0, GaugeWidth / PixelsPerLevel) * PixelsPerLevel;

    /// <summary>A row's pointer region (table 0x5C8CA8): the name and the gauge.</summary>
    public static (int X, int Y, int Width, int Height) SkillRegion(int row) => (17, 63 + 66 * row, 200, 60);

    /// <summary>The training text after the name: <c>" (%s)"</c> (0x5C9070) from the training's name; nothing untrained.</summary>
    public static string TrainingText(string? trainingName) => string.IsNullOrEmpty(trainingName) ? string.Empty : $" ({trainingName})";

    /// <summary>The pointer's message id of a group tab (135 + group, charedit.mes) and of a skill (1000 + its page row 0..15).</summary>
    public static int GroupHint(int group) => 135 + group;

    public static int SkillHint(int skillRow) => 1000 + skillRow;

    // ---- the tech page (window 0x64CA8C, art 30, callback 0x55D940) ----

    /// <summary>A discipline button's top-left (table 0x5C87D0): the eight arts 306..313 alternate high and low.</summary>
    public static (int X, int Y) DisciplineTab(int discipline) =>
        (new[] { 8, 34, 61, 88, 116, 142, 171, 198 }[discipline], new[] { 7, 29, 7, 28, 7, 29, 7, 29 }[discipline]);

    /// <summary>The header's icon glyph place (17, 70) in Icons17: the character '1' + discipline.</summary>
    public static (int X, int Y) HeaderIcon { get; } = (17, 70);

    public static char HeaderGlyph(int discipline) => (char)('1' + discipline);

    /// <summary>The header text <c>"%s %s"</c> (discipline name, degree name), centred on x 118 at y 69.</summary>
    public static (int CentreX, int Y) HeaderText { get; } = (118, 69);

    /// <summary>The expertise number's place in Flare12, blue.</summary>
    public static (int X, int Y) Expertise { get; } = (204, 72);

    /// <summary>Degree row <paramref name="row"/> (0..6, degree row + 1): where its schematic's name goes (and 15 lower its components).</summary>
    public static (int X, int Y) DegreeName(int row) => (46, 94 + 33 * row);

    /// <summary>A learned degree's icon slot (also the plus's slot for the next degree, and the minus's for the last one).</summary>
    public static (int X, int Y) DegreeSlot(int row) => (8, 96 + 33 * row);

    /// <summary>The degree's colour: white up to the stored degree, yellow for the next one, grey after it; all white at degree 7
    /// (0x55C620..0x55C749).</summary>
    public static AreaSheetColour DegreeColour(int row, int degree) => degree >= Degrees || row + 1 <= degree ? AreaSheetColour.White
        : row == degree ? AreaSheetColour.Yellow : AreaSheetColour.Grey;

    /// <summary>Whether the minus is shown: some degree is learned and, in the game (mode 1), the stored degree is above the editor's
    /// snapshot (0x55C7CA).</summary>
    public static bool MinusShown(int degree, int floorDegree) => degree > 0 && floorDegree < degree;

    public static bool PlusShown(int degree) => degree < Degrees;

    /// <summary>The header region (9, 66, 219, 27), message 139, and the degree regions k = 1..7 (32, 99 + 32 (k − 1), 177, 27); the pointer's
    /// step is 32 while the rows' is 33 (table 0x5C8D08).</summary>
    public static (int X, int Y, int Width, int Height) HeaderRegion { get; } = (9, 66, 219, 27);

    public static (int X, int Y, int Width, int Height) DegreeRegion(int degree) => (32, 99 + 32 * (degree - 1), 177, 27);

    public const int HeaderHint = 139;

    /// <summary>The message ids of the tech page: a discipline 4000 + d, a degree 5000 + 8 d + k.</summary>
    public static int DisciplineHint(int discipline) => 4000 + discipline;

    public static int DegreeHint(int discipline, int degree) => 5000 + 8 * discipline + degree;

    public static bool Inside((int X, int Y, int Width, int Height) rect, double x, double y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;
}
