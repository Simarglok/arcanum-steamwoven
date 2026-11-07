using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The pages of the sheet's right-hand panel, in the order of its four tabs (open 0x5597C0; the main page on the left never changes).</summary>
public enum AreaSheetTab { Skills, Tech, Spells, Schemes }

/// <summary>The three fonts the sheet draws with: art 27 <c>morph15</c> (labels, values, the name), 229 <c>Flare12</c> (derived stats and
/// resistances), 171 <c>Cloister18</c> (the alignment and aptitude numbers).</summary>
public enum AreaSheetFont { Label, Flare, Cloister }

/// <summary>The colours of the sheet's fonts (init 0x55E30B..0x55EA2C): white, the name's gold, the green of a raised stat, the red of a
/// lowered one.</summary>
public enum AreaSheetColour { White, Gold, Green, Red, Yellow, Grey, Blue }

/// <summary>One text of the sheet's window: its font, the window-relative top-left (<see cref="Centred"/>: the centre of the text on X),
/// the string and the colour.</summary>
public sealed record AreaSheetText(AreaSheetFont Font, int X, int Y, string Text, AreaSheetColour Colour, bool Centred);

/// <summary>A pointer region of the sheet (a table 0x5C89A8 entry): the message it shows in the text bar and its window-relative rectangle.</summary>
public sealed record AreaSheetRegion(int Id, int X, int Y, int Width, int Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// ARC-UI-075: the original look of the character sheet (<c>charedit</c>, the <c>C</c> window), main page, read from the retail 1.0.7.4
/// executable (ledger CHW1–CHW9). The window is art 22 <c>char_maint</c> (800 × 400) at the screen's (0, 41), between the interface bars;
/// every coordinate here is <b>window-relative</b> (the exe's text tables 0x5C8150, 0x5C7E70 and 0x5C8940 hold screen y, the drawer
/// 0x55B880 subtracts the window origin). The names of the stats, "Health" and "Fatigue" are part of the background; the exe draws only
/// their numbers. The four tabs change only the right-hand panel at (503, 63). Godot-free.
/// </summary>
public static class AreaCharacterSheetArtLayout
{
    public const int Width = 800, Height = 400, WindowTop = 41;
    public const int BackgroundArt = 22, MinusArt = 23, PlusArt = 24, SkillsTabArt = 34, TechTabArt = 35, SpellsTabArt = 36, SchemesTabArt = 568,
        AlignmentArt = 254, AptitudeArt = 255, LabelFontArt = 27, FlareFontArt = 229, CloisterFontArt = 171;
    /// <summary>The right-hand panel's place and size (the four panel windows at the screen's (503, 104), 0x55BAF5; art 29..31, 567: 235 × 325).</summary>
    public static (int X, int Y, int Width, int Height) Panel { get; } = (503, 63, 235, 325);

    /// <summary>The arts the main page needs; the schemes tab (568) is drawn when it reads and is no reason for the diagnostic window.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [BackgroundArt, MinusArt, PlusArt, SkillsTabArt, TechTabArt, SpellsTabArt, AlignmentArt, AptitudeArt, LabelFontArt, FlareFontArt, CloisterFontArt];

    public static IReadOnlyList<int> AllArtNumbers { get; } = [.. ArtNumbers, SchemesTabArt, .. AreaCharacterSheetPagesLayout.ArtNumbers];

    /// <summary>The colour of a font colour (the init indexes the channel tables with these 0..255 values).</summary>
    public static (byte R, byte G, byte B) Rgb(AreaSheetColour colour) => colour switch
    {
        AreaSheetColour.Gold => (255, 210, 0),
        AreaSheetColour.Green => (0, 255, 0),
        AreaSheetColour.Red => (255, 0, 0),
        AreaSheetColour.Yellow => (255, 255, 0),
        AreaSheetColour.Grey => (128, 128, 128),
        AreaSheetColour.Blue => (0, 0, 255),
        _ => (255, 255, 255),
    };

    // ---- the tabs (created at 0x559B7C, 0x559BD9, 0x559C06, 0x559C33; sounds 3000 / 3001) ----

    /// <summary>A tab's art, top-left and hover message.</summary>
    public static (int Art, int X, int Y, int Hint) Tab(AreaSheetTab tab) => tab switch
    {
        AreaSheetTab.Skills => (SkillsTabArt, 527, 11, 132),
        AreaSheetTab.Tech => (TechTabArt, 595, 11, 133),
        AreaSheetTab.Spells => (SpellsTabArt, 663, 11, 134),
        AreaSheetTab.Schemes => (SchemesTabArt, 754, 21, 140),
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    // ---- the + and − buttons (tables 0x5C7F88, 0x5C8028; hover ids 0x5C80C8) ----

    /// <summary>The rows of <see cref="AreaStatsPage"/>: 0 hit points, 1 fatigue, 2..9 the primary stats (strength, constitution, dexterity,
    /// beauty; intelligence, willpower, perception, charisma). Their buttons' top-left: the minus art left of the number, the plus art right.</summary>
    public static (int X, int Y) Minus(int row) => (Column(row, 409, 170, 313), RowY(row));

    public static (int X, int Y) Plus(int row) => (Column(row, 465, 224, 367), RowY(row));

    /// <summary>The hover message of a row's buttons (106 health, 107 fatigue, 109..116 the stats).</summary>
    public static int RowHint(int row) => row switch
    {
        0 => 106,
        1 => 107,
        >= 2 and < AreaStatsPage.Rows => 109 + row - 2,
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    private static int Column(int row, int pool, int left, int right) => row < 2 ? pool : row < 6 ? left : right;

    // hit points 144, fatigue 214; the stats: four rows of 40 from 117.
    private static int RowY(int row) => row switch
    {
        0 => 144,
        1 => 214,
        _ => 117 + 40 * ((row - 2) % 4),
    };

    // ---- the hover regions (table 0x5C89A8, art −1, 32 entries {x, y, w, h, id}) ----

    public static IReadOnlyList<AreaSheetRegion> Regions { get; } = BuildRegions();

    private static List<AreaSheetRegion> BuildRegions()
    {
        var regions = new List<AreaSheetRegion>
        {
            new(100, 141, 21, 66, 66), new(101, 212, 15, 243, 75), new(102, 15, 138, 85, 85),
            new(103, 337, 34, 123, 18), new(104, 337, 34, 123, 18), new(105, 337, 72, 123, 18),
            new(106, 410, 134, 74, 37), new(107, 410, 204, 74, 37), new(108, 751, 88, 38, 274),
        };
        for (var row = 0; row < 4; row++) regions.Add(new(109 + row, 124, 110 + 40 * row, 126, 31));
        for (var row = 0; row < 4; row++) regions.Add(new(113 + row, 266, 110 + 40 * row, 126, 31));
        regions.Add(new(117, 51, 282, 253, 18));
        for (var row = 0; row < 4; row++) regions.Add(new(118 + row, 49, 308 + 18 * row, 125, 14));
        for (var row = 0; row < 4; row++) regions.Add(new(122 + row, 178, 308 + 18 * row, 125, 14));
        regions.Add(new(126, 315, 282, 136, 18));
        for (var row = 0; row < 5; row++) regions.Add(new(127 + row, 322, 301 + 16 * row, 105, 14));
        return regions;
    }

    /// <summary>The message id the pointer is over: the first region of the table that contains it, as the exe's hit test 0x5380F0 walks the
    /// buttons in creation order (ledger CHW8). So 101 covers 103..105 but a strip five pixels wide, and 104, with 103's rectangle, never
    /// answers: the original's flaw, kept in Original mode.</summary>
    public static int? RegionAt(double x, double y)
    {
        foreach (var region in Regions) if (region.Contains(x, y)) return region.Id;
        return null;
    }

    // ---- the gauges (0x55EC90) ----

    /// <summary>Frame of the alignment gauge art 254 for the sheet's alignment (the stored value divided by ten, as the view holds it): the
    /// number divided by ten (truncating) plus ten, clamped to the art's frames (0..20).</summary>
    public static int AlignmentFrame(int alignment, int frames) => Math.Clamp(alignment / 10 + 10, 0, Math.Max(0, frames - 1));

    /// <summary>The alignment gauge's place: its frame lands with its hot point on the window's (56, 181) (0x55ED31 subtracts the hot point).</summary>
    public static (int X, int Y) AlignmentTopLeft(int hotX, int hotY) => (56 - hotX, 181 - hotY);

    /// <summary>The aptitude marker art 255's top-left: x 775, y 221 less the aptitude × 128 / 100 (truncating).</summary>
    public static (int X, int Y) AptitudeMarker(int aptitude) => (775, 221 - aptitude * 128 / 100);

    /// <summary>The strip behind the marker that the redraw restores from art 22 first (0x55EDCA): x 775, y 87, height 278.</summary>
    public static (int X, int Y, int Height) AptitudeStrip { get; } = (775, 87, 278);

    // ---- the texts ----

    /// <summary>The name is cut from its end until it is at most this wide (0x55A3A2).</summary>
    public const int NameWidth = 243;

    /// <summary>The longest prefix of <paramref name="name"/> whose width is at most <see cref="NameWidth"/>.</summary>
    public static string FitName(string name, Func<string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(measure);
        while (name.Length > 0 && measure(name) > NameWidth) name = name[..^1];
        return name;
    }

    /// <summary>
    /// Every text of the main page for <paramref name="view"/>: the name (gold), level, points and experience to the next level, race and
    /// gender (the age is not a session value yet, ledger CHW2), maximum hit points and fatigue, the eight primary stats (green when raised,
    /// red when lowered by effects), the eight derived stats and five resistances with their labels, the two headers, the alignment number
    /// and the two aptitude numbers. <paramref name="measure"/> gives a text's width in a font.
    /// </summary>
    public static IReadOnlyList<AreaSheetText> Texts(AreaCharacterSheetView view, AreaCharacterSheetTexts texts, Func<AreaSheetFont, string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(measure);
        var list = new List<AreaSheetText>
        {
            new(AreaSheetFont.Label, 212, 15, FitName(view.Name, text => measure(AreaSheetFont.Label, text)), AreaSheetColour.Gold, false),
            Line(texts.Line(1), 212, 34, view.Level.ToString()),
            Line(texts.Line(0), 212, 53, view.UnspentPoints.ToString()),
        };
        // The last level prints the sentinel as any other value (0x4A68B0 returns 999999 at level 51 or above, XP3; CHW2 has no exception).
        if (view.ToNextLevel is { } next) list.Add(Line(texts.Line(2), 212, 72, next.ToString()));
        else list.Add(new(AreaSheetFont.Label, 212, 72, texts.Line(2), AreaSheetColour.White, false));
        list.Add(Line(texts.Line(4), 337, 34, texts.RaceName(view.Race)));
        list.Add(Line(texts.Line(5), 337, 53, texts.GenderName(view.Gender)));
        list.Add(new(AreaSheetFont.Label, 178, 282, texts.Line(7), AreaSheetColour.White, true));
        list.Add(new(AreaSheetFont.Label, 383, 282, texts.Line(8), AreaSheetColour.White, true));
        list.Add(new(AreaSheetFont.Label, 447, 143, $" {view.MaxHitPoints} ", AreaSheetColour.White, true));
        list.Add(new(AreaSheetFont.Label, 447, 213, $" {view.MaxFatigue} ", AreaSheetColour.White, true));
        for (var i = 0; i < view.Stats.Count && i < 8; i++)
        {
            var stat = view.Stats[i];
            var x = i < 4 ? 211 : 350;
            list.Add(new(AreaSheetFont.Label, x, 115 + 40 * (i % 4), $" {stat.Value} ", stat.Mark switch
            {
                AreaSheetMark.Raised => AreaSheetColour.Green, AreaSheetMark.Lowered => AreaSheetColour.Red, _ => AreaSheetColour.White,
            }, true));
        }
        for (var i = 0; i < view.Secondary.Count && i < 8; i++)
            list.Add(new(AreaSheetFont.Flare, i < 4 ? 49 : 178, 308 + 18 * (i % 4), $"{texts.DerivedName(i)}: {view.Secondary[i].Value}", AreaSheetColour.White, false));
        for (var i = 0; i < view.Resistances.Count && i < 5; i++)
            list.Add(new(AreaSheetFont.Flare, 322, 301 + 16 * i, $"{texts.Line(12 + i)}: {view.Resistances[i].Value}", AreaSheetColour.White, false));
        list.Add(new(AreaSheetFont.Cloister, 57, 198, view.Alignment.ToString(), AreaSheetColour.White, true));
        list.Add(new(AreaSheetFont.Cloister, 767, 66, view.MagickAptitude.ToString(), AreaSheetColour.White, true));
        list.Add(new(AreaSheetFont.Cloister, 767, 365, view.TechAptitude.ToString(), AreaSheetColour.White, true));
        return list;
    }

    private static AreaSheetText Line(string label, int x, int y, string value) => new(AreaSheetFont.Label, x, y, $"{label}: {value}", AreaSheetColour.White, false);

    /// <summary>The x a text is drawn from: its own x, or for a centred text <c>|x|</c> less half its width.</summary>
    public static int Left(AreaSheetText text, int width) => text.Centred ? text.X - width / 2 : text.X;
}

/// <summary>
/// ARC-UI-075: the texts of the sheet read from the game folder: <c>charedit.mes</c> (the labels 0..8 and 12..16, the pointer texts 100..140
/// and the refusals 17..39), <c>stat.mes</c> (the derived stats' names, stat numbers 8..15) and <c>MainMenu.mes</c> (the race and gender
/// names, as the new-character window prints them). A missing text falls back to a project label.
/// </summary>
public sealed class AreaCharacterSheetTexts
{
    public const string CharEditPath = "mes/charedit.mes";
    public const int RaceNameBase = 720, GenderNameBase = 740, DerivedStatBase = 8;
    private static readonly string[] ProjectLines = ["Points", "Level", "XP Next", "", "Race", "Gender", "Age", "Derived Stats", "Resistance"];
    private static readonly string[] ProjectResistances = ["Damage", "Magick", "Fire", "Poison", "Electrical"];
    private static readonly string[] ProjectDerived =
        ["Carry Wt", "Damage Bonus", "AC Adjust", "Speed", "Heal Rate", "Poison Rec", "Reaction", "Followers"];
    private readonly MesFile? _charEdit, _stat, _menu, _interface, _tech;
    private readonly SchematicCatalog? _schematics;

    private AreaCharacterSheetTexts(MesFile? charEdit, MesFile? stat, MesFile? menu, MesFile? @interface, MesFile? tech, SchematicCatalog? schematics, string detail) =>
        (_charEdit, _stat, _menu, _interface, _tech, _schematics, Detail) = (charEdit, stat, menu, @interface, tech, schematics, detail);

    public string Detail { get; }

    /// <summary>The project's labels only (the synthetic self-tests have no game folder).</summary>
    public static AreaCharacterSheetTexts Project(string reason) => new(null, null, null, null, null, null, reason ?? throw new ArgumentNullException(nameof(reason)));

    public static AreaCharacterSheetTexts Create(MesFile? charEdit, MesFile? stat, MesFile? menu, MesFile? @interface = null, MesFile? tech = null,
        SchematicCatalog? schematics = null) => new(charEdit, stat, menu, @interface, tech, schematics, "synthetic");

    public static AreaCharacterSheetTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var charEdit = AreaStartingHero.Table(stack, CharEditPath, ContentLoadPhase.GameStart, required: true, out var d1);
        var stat = AreaStartingHero.Table(stack, AreaLevelTexts.StatPath, ContentLoadPhase.GameStart, required: true, out var d2);
        var menu = AreaStartingHero.Table(stack, AreaMainMenuTexts.MainMenuPath, ContentLoadPhase.GameStart, required: true, out var d3);
        var gameInterface = AreaStartingHero.Table(stack, AreaLevelTexts.InterfacePath, ContentLoadPhase.GameStart, required: true, out var d4);
        var tech = AreaStartingHero.Table(stack, TechTexts.Path, ContentLoadPhase.GameStart, required: true, out var d5);
        var schematics = SchematicCatalog.Open(stack);
        var missing = new[] { d1, d2, d3, d4, d5, schematics.Catalog is null ? "schematics " + schematics.Status : null }.Where(detail => detail is not null);
        return new(charEdit, stat, menu, gameInterface, tech, schematics.Catalog, missing.Any() ? "texts: " + string.Join("; ", missing) : "texts read");
    }

    private static string? Decoded(MesFile? table, int number) =>
        table?.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } text }
            ? text : null;

    /// <summary>charedit.mes 0..8 (the labels), 12..16 (the resistances) or any other number the sheet shows.</summary>
    public string Line(int number) => Decoded(_charEdit, number) ?? number switch
    {
        >= 0 and <= 8 => ProjectLines[number],
        >= 12 and <= 16 => ProjectResistances[number - 12],
        _ => $"[{number}]",
    };

    /// <summary>The name of derived stat <paramref name="index"/> (0..7: carry weight .. maximum followers; the exe's 0x4B0F80).</summary>
    public string DerivedName(int index) => Decoded(_stat, DerivedStatBase + index) ?? ProjectDerived[index];

    public string RaceName(CharacterRace race) => Decoded(_menu, RaceNameBase + (int)race) ?? AreaCharacterSheet.RaceName(race);

    public string GenderName(CharacterGender gender) => Decoded(_menu, GenderNameBase + (int)gender) ?? (gender == CharacterGender.Female ? "female" : "male");

    /// <summary>The text the pointer shows for a region or button id: charedit.mes of the same number (title, a new line, the text); null
    /// when the table has none.</summary>
    public string? Hint(int id) => Decoded(_charEdit, id);

    /// <summary>ARC-UI-076: a text of <c>intgame.mes</c> (17 "Minimum IN", 28 + stat the stats' names), else <paramref name="fallback"/>.</summary>
    public string Interface(int number, string fallback) => Decoded(_interface, number) ?? fallback;

    /// <summary>ARC-UI-076: <c>tech.mes</c>: a discipline's name (0..7), a degree's name (8 + degree, 0..7: layman .. doctorate), a
    /// discipline's description (16 + d) and a degree's (<see cref="TechTexts.DegreeDescriptionMessage"/>); empty when missing.</summary>
    public string TechDisciplineName(int discipline) => Decoded(_tech, discipline) ?? $"Discipline {discipline + 1}";

    public string TechDegreeName(int degree) => Decoded(_tech, TechTexts.FirstDegreeName + degree) ?? $"Degree {degree}";

    public string TechDisciplineDescription(int discipline) => Decoded(_tech, TechTexts.FirstDisciplineDescription + discipline) ?? string.Empty;

    public string TechDegreeDescription(int discipline, int degree) => Decoded(_tech, TechTexts.DegreeDescriptionMessage(discipline, degree)) ?? string.Empty;

    /// <summary>ARC-UI-076: the name of the schematic of <paramref name="degree"/> (1..7) in <paramref name="discipline"/> (0x56EA10: the
    /// record 1990 + 200 d + 10 degree of <c>schematic.mes</c>, its name text from <c>schematic_text.mes</c>); null when it does not read.</summary>
    public string? SchematicName(int discipline, int degree)
    {
        if (_schematics is not { } catalog) return null;
        var number = SchematicCatalog.FirstLearnedNumberBase + SchematicCatalog.NumbersPerDiscipline * discipline + SchematicCatalog.RecordStride * degree;
        return catalog.Read(number) is { Record: { } record } ? catalog.Text(record.NameText) : null;
    }

    /// <summary>The refusal of charedit.mes <paramref name="number"/> (17..39), or null.</summary>
    public string? Refusal(int number) => Decoded(_charEdit, number);
}

/// <summary>ARC-UI-075: what the sheet's original look draws from the game folder: its interface art and its texts.</summary>
public sealed record AreaCharacterSheetArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, AreaCharacterSheetTexts Texts, string Detail)
{
    /// <summary>True when every art of the main page read: the sheet then shows the original look.</summary>
    public bool ArtReady => AreaCharacterSheetArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load)
        && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

    /// <summary>ARC-UI-076: true when the skills and tech pages' arts read too: those pages then show the original look.</summary>
    public bool PagesReady => ArtReady && AreaCharacterSheetPagesLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load)
        && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

    public static AreaCharacterSheetArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), AreaCharacterSheetTexts.Project(reason), reason);

    public static AreaCharacterSheetArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        // The fonts have 225 frames.
        var art = AreaInterfaceArt.Load(stack, AreaCharacterSheetArtLayout.AllArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var texts = AreaCharacterSheetTexts.Load(stack);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, texts, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {texts.Detail}");
    }
}
