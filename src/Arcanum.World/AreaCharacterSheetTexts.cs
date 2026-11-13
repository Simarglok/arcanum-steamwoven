using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation;

namespace Arcanum.World;

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
    private readonly MesFile? _charEdit, _stat, _menu, _interface, _tech, _gameSchemeNames, _userSchemeNames;
    private readonly SchematicCatalog? _schematics;

    private AreaCharacterSheetTexts(MesFile? charEdit, MesFile? stat, MesFile? menu, MesFile? @interface, MesFile? tech, SchematicCatalog? schematics, string detail,
        MesFile? gameSchemeNames = null, MesFile? userSchemeNames = null) =>
        (_charEdit, _stat, _menu, _interface, _tech, _schematics, Detail, _gameSchemeNames, _userSchemeNames) =
        (charEdit, stat, menu, @interface, tech, schematics, detail, gameSchemeNames, userSchemeNames);

    public string Detail { get; }

    /// <summary>The project's labels only (the synthetic self-tests have no game folder).</summary>
    public static AreaCharacterSheetTexts Project(string reason) => new(null, null, null, null, null, null, reason ?? throw new ArgumentNullException(nameof(reason)));

    public static AreaCharacterSheetTexts Create(MesFile? charEdit, MesFile? stat, MesFile? menu, MesFile? @interface = null, MesFile? tech = null,
        SchematicCatalog? schematics = null, MesFile? gameSchemeNames = null, MesFile? userSchemeNames = null) =>
        new(charEdit, stat, menu, @interface, tech, schematics, "synthetic", gameSchemeNames, userSchemeNames);

    public static AreaCharacterSheetTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var charEdit = AreaStartingHero.Table(stack, CharEditPath, ContentLoadPhase.GameStart, required: true, out var d1);
        var stat = AreaStartingHero.Table(stack, AreaLevelTexts.StatPath, ContentLoadPhase.GameStart, required: true, out var d2);
        var menu = AreaStartingHero.Table(stack, AreaMainMenuTexts.MainMenuPath, ContentLoadPhase.GameStart, required: true, out var d3);
        var gameInterface = AreaStartingHero.Table(stack, AreaLevelTexts.InterfacePath, ContentLoadPhase.GameStart, required: true, out var d4);
        var tech = AreaStartingHero.Table(stack, TechTexts.Path, ContentLoadPhase.GameStart, required: true, out var d5);
        var schematics = SchematicCatalog.Open(stack);
        // ARC-UI-1742: the scheme names the auto-level page lists (the original reads both with the level tables at start-up, 0x4A6620).
        var gameSchemes = AreaStartingHero.Table(stack, AutoLevelTables.GameNamesPath, ContentLoadPhase.GameStart, required: true, out var d6);
        var userSchemes = AreaStartingHero.Table(stack, AutoLevelTables.UserNamesPath, ContentLoadPhase.GameStart, required: true, out var d7);
        var missing = new[] { d1, d2, d3, d4, d5, d6, d7, schematics.Catalog is null ? "schematics " + schematics.Status : null }.Where(detail => detail is not null);
        return new(charEdit, stat, menu, gameInterface, tech, schematics.Catalog, missing.Any() ? "texts: " + string.Join("; ", missing) : "texts read", gameSchemes, userSchemes);
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
    [OriginalFunction(0x56EA10, OriginalFunctionScope.Partial)]
    public string? SchematicName(int discipline, int degree)
    {
        if (_schematics is not { } catalog) return null;
        var number = SchematicCatalog.FirstLearnedNumberBase + SchematicCatalog.NumbersPerDiscipline * discipline + SchematicCatalog.RecordStride * degree;
        return catalog.Read(number) is { Record: { } record } ? catalog.Text(record.NameText) : null;
    }

    /// <summary>ARC-UI-1742: whether the scheme name files read (the schemes page then lists them).</summary>
    public bool HasSchemeNames => _gameSchemeNames is not null && _userSchemeNames is not null;

    /// <summary>ARC-UI-1742: the name of <paramref name="scheme"/> (0x4A6DE0: 0 and 50..199 from <c>gamelevelname.mes</c>, 1..49 from
    /// <c>userlevelname.mes</c>), or null when the files have none (or did not read).</summary>
    [OriginalFunction(0x55D210, OriginalFunctionScope.Partial)]
    public string? SchemeName(int scheme) => _gameSchemeNames is null || _userSchemeNames is null ? null
        : AutoLevelTables.Name(_gameSchemeNames, _userSchemeNames, scheme) is { } bytes ? DecodedName(bytes) : null;

    /// <summary>ARC-UI-1742: the schemes the page lists, in the order of 0x55D060: every number from 0 whose name exists.</summary>
    public IReadOnlyList<int> SchemeNumbers() => _gameSchemeNames is null || _userSchemeNames is null ? []
        : AutoLevelTables.NamedSchemes(_gameSchemeNames, _userSchemeNames);

    private static string DecodedName(ReadOnlyMemory<byte> bytes) =>
        MesText.Decode(bytes.Span, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : string.Empty;

    /// <summary>The refusal of charedit.mes <paramref name="number"/> (17..39), or null.</summary>
    public string? Refusal(int number) => Decoded(_charEdit, number);
}
