using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The pre-made browser's art buttons (window descriptor 0x5C5270, button table 0x5C51B0; ledger MW17).</summary>
public enum AreaPremadeBrowserButton { Start, Back, Previous, Next }

/// <summary>
/// ARC-UI-066: the original look of the pre-made character browser (window 13), read from its descriptor 0x5C5270, open 0x545870,
/// redraw 0x5458D0, button callback 0x5459F0 and the character draw 0x5448E0 it shares with the new-character window (ledger
/// MW17–MW21). The background 766 `PreGenCharacterBase` (800 × 400) is the window at (0, 41) between the strips 336 and 335 (MW1).
/// Four art buttons: Start 321 `NextBut` and Back 323 `BackBut` in the bottom strip, Previous 283 `Big_Grn_L` and Next 284
/// `Big_Grn_R` beside the portrait. The browsed hero shows its large portrait, its player name, gender, race and background name
/// centred in their fields, its background's story (or its race's text) and fifteen stats "ST: 8" in the art fonts 27 `morph15font`
/// and 171 `Cloister18Font`; each field is filled black before its text. Coordinates here are the 800 × 600 screen's. Godot-free.
/// </summary>
public static class AreaPremadeBrowserArtLayout
{
    public const int ScreenWidth = 800, ScreenHeight = 600;
    public const int BackgroundArt = 766, TopStripArt = 336, BottomStripArt = 335, StartArt = 321, BackArt = 323, PreviousArt = 283, NextArt = 284;
    /// <summary>The art fonts (MW14): fonts 0x64C210 and 0x64C218 are art 27, 0x64C214 and 0x64C21C art 171.</summary>
    public const int TextFontArt = 27, NameFontArt = 171;
    public const int WindowTop = AreaSaveListArtLayout.WindowTop, BottomStripTop = AreaSaveListArtLayout.BottomStripTop;
    /// <summary>The portrait's draw (0x4CE640: the large image, PO3).</summary>
    public const int PortraitSize = PortraitCatalogue.LargeSize;
    /// <summary>MainMenu.mes: the buttons' hints (7003 Start, 7002 Previous Screen), the stats' title "Initial Stats" (739), the race
    /// text's title "Race" (745), the race names 720 + race, the gender names 740 + gender and the race texts 770 + 2 × race + gender
    /// (MW19).</summary>
    public const int StartHint = 7003, BackHint = 7002, StatsTitleMessage = 739, RaceTitleMessage = 745, RaceNameBase = 720, GenderNameBase = 740,
        RaceTextBase = 770;
    /// <summary>stat.mes: a stat's short label is message 500 + its number (0x4B0390, 0x4B0F90).</summary>
    public const int StatLabelBase = 500;
    /// <summary>A background story is shown for a description number above 1000, the default text (0x544D36).</summary>
    public const int DefaultDescription = BackgroundTable.DefaultDescription;
    /// <summary>A tab's advance (MW4).</summary>
    public const int TabAdvance = 20;

    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, TopStripArt, BottomStripArt, StartArt, BackArt, PreviousArt, NextArt, TextFontArt, NameFontArt];

    /// <summary>The fields of the character draw 0x5448E0 and the redraw 0x5458D0 (the window's rectangles 0x5C4EC0..0x5C5090 and
    /// 0x5C4ED0, 41 rows lower on the screen).</summary>
    public static (int X, int Y) Portrait { get; } = (95, WindowTop + 28);
    public static (int X, int Y, int Width, int Height) PortraitFill { get; } = (95, WindowTop + 28, 64, 64);
    public static (int X, int Y, int Width, int Height) Name { get; } = (46, WindowTop + 183, 228, 19);
    public static (int X, int Y, int Width, int Height) Gender { get; } = (66, WindowTop + 243, 186, 19);
    public static (int X, int Y, int Width, int Height) Race { get; } = (66, WindowTop + 293, 186, 19);
    public static (int X, int Y, int Width, int Height) Background { get; } = (66, WindowTop + 343, 186, 19);
    public static (int X, int Y, int Width, int Height) StatsTitle { get; } = (339, WindowTop + 23, 409, 19);
    /// <summary>The story's field (filled black, and the story's rectangle) and the race text's title and body within it.</summary>
    public static (int X, int Y, int Width, int Height) Description { get; } = (350, WindowTop + 155, 395, 212);
    public static (int X, int Y, int Width, int Height) RaceTitle { get; } = (350, WindowTop + 159, 395, 19);
    public static (int X, int Y, int Width, int Height) RaceText { get; } = (350, WindowTop + 185, 395, 182);

    /// <summary>The stats in drawing order (table 0x5C5130, stat numbers): strength, constitution, dexterity, beauty, intelligence,
    /// willpower, perception, charisma, then carry weight, damage bonus, AC adjustment, speed, heal rate, poison recovery and reaction
    /// modifier.</summary>
    public static IReadOnlyList<int> StatOrder { get; } = [0, 2, 1, 3, 4, 6, 5, 7, 8, 9, 10, 11, 12, 13, 14];

    /// <summary>The stats' rectangles in the same order (0x5C4F50, 16 bytes each): four columns of 50, 50, 139 and 142..147 at x 346,
    /// 406, 472 and 618, rows from the window's y 46.</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> StatFields { get; } =
    [
        (346, WindowTop + 46, 50, 15), (346, WindowTop + 66, 50, 15), (346, WindowTop + 86, 50, 15), (346, WindowTop + 106, 50, 15),
        (406, WindowTop + 46, 50, 15), (406, WindowTop + 66, 50, 15), (406, WindowTop + 86, 50, 15), (406, WindowTop + 106, 50, 15),
        (472, WindowTop + 46, 139, 15), (472, WindowTop + 66, 139, 15), (472, WindowTop + 86, 139, 15), (472, WindowTop + 106, 139, 15),
        (618, WindowTop + 46, 142, 15), (618, WindowTop + 65, 147, 15), (618, WindowTop + 86, 147, 15),
    ];

    /// <summary>The text colours (MW14): 0x64C210 and 0x64C214 (250, 250, 250), 0x64C218 and 0x64C21C (255, 50, 50).</summary>
    public static (byte R, byte G, byte B) White { get; } = (250, 250, 250);
    public static (byte R, byte G, byte B) Red { get; } = (255, 50, 50);

    /// <summary>The Steamwoven stat of an original stat number 0..14 (the primary stats keep their numbers; 8..14 are the derived
    /// stats from carry weight, which Steamwoven numbers after its magick and tech points).</summary>
    public static CharacterStat Stat(int number) => number switch
    {
        >= 0 and <= 7 => (CharacterStat)number,
        >= 8 and <= 14 => CharacterStat.CarryWeight + (number - 8),
        _ => throw new ArgumentOutOfRangeException(nameof(number)),
    };

    /// <summary>A button's art and its top-left on the screen: Start and Back (table flag 1) at (675, 55) and (69, 55) of the bottom
    /// strip, Previous and Next (flag 0) at the screen's (43, 118) and (244, 118) (button create 0x5461C0, MW12).</summary>
    public static (int Art, int X, int Y) Button(AreaPremadeBrowserButton button) => button switch
    {
        AreaPremadeBrowserButton.Start => (StartArt, 675, BottomStripTop + 55),
        AreaPremadeBrowserButton.Back => (BackArt, 69, BottomStripTop + 55),
        AreaPremadeBrowserButton.Previous => (PreviousArt, 43, 118),
        AreaPremadeBrowserButton.Next => (NextArt, 244, 118),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The button's hint message in MainMenu.mes, or null (the arrows carry none, -1).</summary>
    public static int? Hint(AreaPremadeBrowserButton button) => button switch
    {
        AreaPremadeBrowserButton.Start => StartHint,
        AreaPremadeBrowserButton.Back => BackHint,
        AreaPremadeBrowserButton.Previous or AreaPremadeBrowserButton.Next => null,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The x a one-line text of <paramref name="width"/> starts at in <paramref name="field"/>: centred when narrower (the
    /// free width halved toward zero), else at the field's left (0x542DF0).</summary>
    public static int Centred((int X, int Y, int Width, int Height) field, int width) => width < field.Width ? field.X + (field.Width - width) / 2 : field.X;

    /// <summary>
    /// The lines of a text in a field <paramref name="width"/> wide: each glyph with its x from the line's left. The two characters
    /// <c>\n</c> start a line and <c>\t</c> are five spaces (0x5418A0, 0x541830, MW21). PROJECT: a line feed byte starts a line too, a
    /// tab byte advances 20 (MW4), a carriage return has no glyph; a word that does not fit starts the next line (the text draw's line
    /// break 0x535C40 is not read, F0) and spaces at the start of a wrapped line are dropped.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(int X, AreaArtGlyph Glyph)>> Wrap(AreaArtFont font, string text, int width)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        var lines = new List<IReadOnlyList<(int, AreaArtGlyph)>>();
        foreach (var paragraph in text.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\t", "     ", StringComparison.Ordinal).Split('\n'))
        {
            var line = new List<(int, AreaArtGlyph)>();
            var x = 0;
            var wrapped = false;
            for (var i = 0; i < paragraph.Length;)
            {
                var character = paragraph[i];
                if (character == '\t') { x += TabAdvance; i++; continue; }
                if (character == ' ')
                {
                    if (!(wrapped && line.Count == 0) && font.Glyph(' ') is { } space) x += space.Advance;
                    i++;
                    continue;
                }
                var end = i;
                while (end < paragraph.Length && paragraph[end] is not (' ' or '\t')) end++;
                var word = font.Layout(paragraph[i..end]);
                var measured = font.Measure(paragraph[i..end]).Width;
                if (line.Count > 0 && x + measured > width)
                {
                    lines.Add(line);
                    (line, x, wrapped) = (new(), 0, true);
                }
                foreach (var (gx, glyph) in word) line.Add((x + gx, glyph));
                if (word.Count > 0) x += word[^1].X + word[^1].Glyph.Advance;
                i = end;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>The effective stats of a hero definition at its start, in <see cref="StatOrder"/>: its stored stats under its race and
    /// gender effects (as the session's hero owner reads them, no context); null when the owner refuses them.</summary>
    public static IReadOnlyList<int>? StatValues(SessionHeroDefinition hero)
    {
        ArgumentNullException.ThrowIfNull(hero);
        var races = hero.RaceEffect is null ? ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty.Add(hero.Race, hero.RaceEffect);
        var genders = hero.GenderEffect is null ? ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty
            : ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty.Add(hero.Gender, hero.GenderEffect);
        if (CharacterStatsOwner.Create(new(hero.Race, hero.Gender, new(races, genders), CharacterStatLimits.Default)).Owner is not { } owner
            || hero.Stats.Count != SessionHeroLimits.StoredStats.Length) return null;
        for (var index = 0; index < hero.Stats.Count; index++)
            if (owner.SetBase(SessionHeroLimits.StoredStats[index], hero.Stats[index]).Disposition == CharacterStatsDisposition.Rejected) return null;
        if (owner.Query(new(0)).Snapshot is not { } snapshot) return null;
        return [.. StatOrder.Select(number => snapshot.GetEffective(Stat(number)))];
    }

    public static bool Contains((int X, int Y, int Width, int Height) rect, double x, double y) => AreaSaveListArtLayout.Contains(rect, x, y);
}

/// <summary>
/// ARC-UI-066: what the browser draws of one pre-made hero. <see cref="DescriptionTitle"/> is null for a background story (drawn in
/// <see cref="AreaPremadeBrowserArtLayout.Description"/>) and the race text's title "Race" otherwise (the text then in
/// <see cref="AreaPremadeBrowserArtLayout.RaceText"/>). Texts are the game's bytes as characters (the art font's glyph is the byte).
/// </summary>
public sealed record AreaPremadeBrowserView(int Index, string Name, string Gender, string Race, string BackgroundName, string? DescriptionTitle,
    string Description, string StatsTitle, IReadOnlyList<string> Stats, int Portrait);

/// <summary>ARC-UI-066: the browser's art and texts read from the game folder (MainMenu.mes, stat.mes, the background texts and the
/// portrait catalogue).</summary>
public sealed record AreaPremadeBrowserArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, MesFile? MainMenu, MesFile? Stats,
    BackgroundTable? Backgrounds, PortraitCatalogue? Portraits, string Detail)
{
    /// <summary>True when every art and text table read: the browser then shows the original look (all or nothing, as every one is in
    /// the installed game).</summary>
    public bool ArtReady => AreaPremadeBrowserArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0) && MainMenu is not null && Stats is not null && Backgrounds is not null;

    public AreaArtFont? TextFont => ArtReady ? new(Art[AreaPremadeBrowserArtLayout.TextFontArt].Frames) : null;
    public AreaArtFont? NameFont => ArtReady ? new(Art[AreaPremadeBrowserArtLayout.NameFontArt].Frames) : null;

    public static AreaPremadeBrowserArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), null, null, null, null, reason);

    /// <summary>Reads the art and the tables from the game folder of <paramref name="modulePath"/>; failures are reported, never
    /// thrown. The frame limit is raised to 256 for the fonts' 225 frames.</summary>
    public static AreaPremadeBrowserArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaPremadeBrowserArtLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToList();
        var menu = AreaStartingHero.Table(stack, AreaMainMenuTexts.MainMenuPath, ContentLoadPhase.GameStart, required: true, out var menuDetail);
        var stats = AreaStartingHero.Table(stack, AreaLevelTexts.StatPath, ContentLoadPhase.GameStart, required: true, out var statDetail);
        var backgrounds = BackgroundTable.Load(stack);
        var portraits = PortraitCatalogue.Load(stack);
        var tables = new[] { menuDetail, statDetail, backgrounds.Table is null ? $"backgrounds {backgrounds.Status} {backgrounds.File}" : null,
            portraits.Catalogue is null ? $"portraits {portraits.Status} {portraits.File}" : null }.Where(detail => detail is not null);
        return new(art, menu, stats, backgrounds.Table, portraits.Catalogue,
            $"art {art.Count - failed.Count}/{art.Count} read{(failed.Count == 0 ? "" : " (" + string.Join("; ", failed) + ")")}"
            + string.Concat(tables.Select(detail => "; " + detail)));
    }

    /// <summary>The large image of portrait <paramref name="id"/> from the game folder of <paramref name="modulePath"/>.</summary>
    public AreaPortraitFile Portrait(string modulePath, int id)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return new(id, AreaPortraitStatus.NotRead, null, null, $"{found.Status} {found.Detail}");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(id, AreaPortraitStatus.NotRead, null, null, $"content stack {opened.Status}");
        using var stack = opened.Stack!;
        return AreaPortraits.Load(stack, Portraits, [id], AreaPremadeBrowserArtLayout.PortraitSize)[id];
    }

    /// <summary>The browsed hero's view from its load, or null when the load or the tables are not ready.</summary>
    public AreaPremadeBrowserView? View(int index, AreaHeroLoad load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (!ArtReady || load is not { Status: AreaHeroStatus.Ready, Definition: { } definition, Read.Hero: { } hero }) return null;
        if (AreaPremadeBrowserArtLayout.StatValues(definition) is not { } values) return null;
        var description = Backgrounds!.Text(hero.BackgroundDescription);
        var story = hero.BackgroundDescription > AreaPremadeBrowserArtLayout.DefaultDescription && description.Status != BackgroundTextStatus.Missing;
        var stats = AreaPremadeBrowserArtLayout.StatOrder.Select((number, slot) =>
            $"{Text(Stats, AreaPremadeBrowserArtLayout.StatLabelBase + number)}: {values[slot]}").ToArray();
        return new(index, Latin1(hero.PlayerName.ToArray()), Text(MainMenu, AreaPremadeBrowserArtLayout.GenderNameBase + hero.Gender),
            Text(MainMenu, AreaPremadeBrowserArtLayout.RaceNameBase + hero.Race), Latin1(description.Name.Span),
            story ? null : Text(MainMenu, AreaPremadeBrowserArtLayout.RaceTitleMessage),
            story ? Latin1(description.Body.Span) : Text(MainMenu, AreaPremadeBrowserArtLayout.RaceTextBase + 2 * hero.Race + hero.Gender),
            Text(MainMenu, AreaPremadeBrowserArtLayout.StatsTitleMessage), stats, hero.Portrait);
    }

    /// <summary>A message's bytes as characters, or "" when the table lacks it (the original draws its "Error! Missing line" text, F0).</summary>
    private static string Text(MesFile? table, int number) => table?.Find(number) is { } message ? Latin1(message.Bytes.Span) : "";

    private static string Latin1(ReadOnlySpan<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes);
}
