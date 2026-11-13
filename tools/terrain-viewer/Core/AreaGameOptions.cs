using System.Globalization;
using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Input;
using Arcanum.Presentation.Display;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The original's cyclic control types (OP3): a slider bar, a list of a value file's texts, the module list.</summary>
public enum AreaOptionKind { Slider, List, Modules }

/// <summary>The original's option controls, page by page in slot order (OP2), then the project's (ARC-UI-014: the rule mode).</summary>
public enum AreaOption
{
    Module, Difficulty, ViolenceFilter, DefaultCombatMode, AutoAttack, AutoSwitchWeapons, AlwaysRun, FollowerSkills,
    Brightness, TextDuration, Floats, FloatSpeed, CombatTaunts,
    Effects, Voice, Music,
    Rules, ControlScheme, PromptStyle, EvidenceLog, TestTask,
}

/// <param name="Max">The original's highest value (a slider's 10 unless its record says otherwise, a list's last message number; a
/// project list's last index).</param>
/// <param name="Limit">Steamwoven's highest value: <see cref="Max"/>, or lower where the higher values are not built.</param>
/// <param name="ValueTable">The list's value file (its message numbers are the values, OP3); null for a slider.</param>
/// <param name="Setting">The original's <c>arcanum.cfg</c> setting name.</param>
/// <param name="Default">The value the original registers.</param>
/// <param name="Unavailable">Why Steamwoven hides the control; null when it is shown.</param>
/// <param name="LimitNote">What the values above <see cref="Limit"/> are, for the window's note.</param>
/// <param name="Project">A control the original's table does not have (ARC-UI-014): its label and values are the project's texts only,
/// never a message of the game's files, and <see cref="Setting"/> names the project's setting, not an <c>arcanum.cfg</c> one.</param>
public sealed record AreaOptionControl(AreaOption Option, AreaOptionsPage Page, int Slot, AreaOptionKind Kind, int Max, int Limit, string? ValueTable,
    string Setting, int Default, string? Unavailable, string? LimitNote = null, bool Project = false)
{
    /// <summary>The label's message in <c>mes\options_ui.mes</c>: 100 + 100 × page + slot (OP2); none for a project control.</summary>
    public int? LabelMessage => Project ? null : 100 + 100 * (int)Page + Slot;
    public bool Shown => Unavailable is null;

    /// <summary>The value after an arrow (OP3): a list wraps around at either end, a slider stops there (null: no change).</summary>
    public int? Step(int value, int direction)
    {
        if (direction == 0 || value < 0 || value > Limit) return null;
        var next = value + Math.Sign(direction);
        if (next >= 0 && next <= Limit) return next;
        return Kind == AreaOptionKind.Slider ? null : next < 0 ? Limit : 0;
    }
}

/// <summary>
/// ARC-UI-013: the original's options window (window 6, ledger OP1–OP8) as Steamwoven offers it. The table is the original's: 16
/// controls on three pages, each with its <c>arcanum.cfg</c> setting and registered default. Steamwoven shows the controls whose
/// function it has (difficulty, the default combat mode without fast turn-based, auto attack, Always Run, ARC-UI-021's floating text
/// level and speed, the three volumes) and hides the rest with the reason (FR-UI-001: only what the profile supports); a page all of
/// whose controls are hidden is hidden too.
/// </summary>
public static class AreaOptionTable
{
    public const string NoModules = "one module is offered; choosing a module is not built";
    public const string NoTextDuration = "text bubbles are not built";
    public const string NoFastTurnBased = "Fast Turn Based is not built";
    public const string NoVideoPage = "text bubbles are not built";
    public const string OffOn = "mes/OptionsOffOn.mes";
    public const string Difficulties = "mes/OptionsDifficulty.mes";
    public const string CombatModes = "mes/OptionsCombatMode.mes";
    public const string FloatLevels = "mes/OptionsFloats.mes";
    /// <summary>The rule mode control's values (a project list, no game file): <see cref="RuleMode"/> in its order.</summary>
    public const string RuleModes = "project:rule modes";
    /// <summary>The evidence log control's values (a project list): Off, On (ARC-GPAD-1376).</summary>
    public const string OffOnProject = "project:off on";
    /// <summary>The test task control's values: none, then the test script's tasks 1 to 8 (ARC-GPAD-1376).</summary>
    public const string TestTasks = "project:test tasks";
    public const int TestTaskCount = 8;
    /// <summary>ARC-GPAD-1374, ARC-GPAD-1463: the controller scheme controls' values (project lists, no game file): <see cref="ControlChoice"/> (the Game
    /// page's one Controls line) and <see cref="Arcanum.Input.PromptStyle"/> (the Video page's) in their order.</summary>
    public const string ControlSchemes = "project:control scheme", PromptStyles = "project:prompt style";

    /// <summary>Every control of the original's table (0x5CCD78, three pages of eight slots; OP2).</summary>
    public static IReadOnlyList<AreaOptionControl> All { get; } = Array.AsReadOnly(new AreaOptionControl[]
    {
        new(AreaOption.Module, AreaOptionsPage.Game, 0, AreaOptionKind.Modules, 0, 0, null, "module", 0, NoModules),
        new(AreaOption.Difficulty, AreaOptionsPage.Game, 1, AreaOptionKind.List, 2, 2, Difficulties, "difficulty", 1, null),
        new(AreaOption.ViolenceFilter, AreaOptionsPage.Game, 2, AreaOptionKind.List, 1, 1, OffOn, "violence filter", 0, null),
        new(AreaOption.DefaultCombatMode, AreaOptionsPage.Game, 3, AreaOptionKind.List, 2, 1, CombatModes, "turn-based", 0, null, NoFastTurnBased),
        new(AreaOption.AutoAttack, AreaOptionsPage.Game, 4, AreaOptionKind.List, 1, 1, OffOn, "auto attack", 1, null),
        new(AreaOption.AutoSwitchWeapons, AreaOptionsPage.Game, 5, AreaOptionKind.List, 1, 1, OffOn, "auto switch", 0, null),
        new(AreaOption.AlwaysRun, AreaOptionsPage.Game, 6, AreaOptionKind.List, 1, 1, OffOn, "always run", 0, null),
        new(AreaOption.FollowerSkills, AreaOptionsPage.Game, 7, AreaOptionKind.List, 1, 1, OffOn, "follower skills", 1, null),
        new(AreaOption.Brightness, AreaOptionsPage.Video, 0, AreaOptionKind.Slider, 9, 9, null, "brightness", 0, null),
        new(AreaOption.TextDuration, AreaOptionsPage.Video, 1, AreaOptionKind.Slider, 10, 10, null, "text duration", 6, NoTextDuration),
        // ARC-UI-021: the floating text (CH10).
        new(AreaOption.Floats, AreaOptionsPage.Video, 2, AreaOptionKind.List, 2, 2, FloatLevels, "text floaters", FloaterRules.DefaultLevel, null),
        new(AreaOption.FloatSpeed, AreaOptionsPage.Video, 3, AreaOptionKind.Slider, 10, 10, null, "float speed", FloaterRules.DefaultSpeed, null),
        new(AreaOption.CombatTaunts, AreaOptionsPage.Video, 4, AreaOptionKind.List, 1, 1, OffOn, "combat taunts", 1, null),
        new(AreaOption.Effects, AreaOptionsPage.Sound, 0, AreaOptionKind.Slider, 10, 10, null, "effects volume", 5, null),
        new(AreaOption.Voice, AreaOptionsPage.Sound, 1, AreaOptionKind.Slider, 10, 10, null, "voice volume", 5, null),
        new(AreaOption.Music, AreaOptionsPage.Sound, 2, AreaOptionKind.Slider, 10, 10, null, "music volume", 5, null),
        // ARC-UI-014: the rule mode (owner decision D8: the settings menu may switch it in play), after the original's eight Game slots.
        new(AreaOption.Rules, AreaOptionsPage.Game, 8, AreaOptionKind.List, 1, 1, RuleModes, "rules", (int)RuleMode.Steamwoven, null, Project: true),
        // ARC-GPAD-1463 (concept docs/input/controller-schemes.md §0.1, owner request 2026-11-12): one Controls line, the original or one of the three
        // experimental schemes, switching at run time and saved with the game settings. The original mouse and keyboard are live in every value.
        // ARC-GPAD-1374: the prompt style (the glyph family override) sits beside it. ARC-UI-1737: the Game page holds the original's eight slots (the
        // seven original lines it shows or will show, and Rules), so the Controls line moved to the Video page, next to the prompt style.
        new(AreaOption.ControlScheme, AreaOptionsPage.Video, 5, AreaOptionKind.List, 3, 3, ControlSchemes, "control scheme", (int)ControlChoice.Original, null, Project: true),
        new(AreaOption.PromptStyle, AreaOptionsPage.Video, 6, AreaOptionKind.List, 4, 4, PromptStyles, "prompt style", (int)Arcanum.Input.PromptStyle.Auto, null, Project: true),
        // ARC-GPAD-1376: the opt-in evidence log of the controller test (concept §0.8); off at every start, never saved. ARC-UI-1737: the original's Violence
        // Filter needs a Game page slot and the page holds eight lines, so the two test lines moved to the Video page.
        new(AreaOption.EvidenceLog, AreaOptionsPage.Video, 7, AreaOptionKind.List, 1, 1, OffOnProject, "evidence log", 0, null, Project: true),
        // The marker of the test script's task (concept §5): the log's task start and end lines, so the summariser can time it.
        new(AreaOption.TestTask, AreaOptionsPage.Video, 8, AreaOptionKind.List, TestTaskCount, TestTaskCount, TestTasks, "test task", 0, null, Project: true),
        // ARC-UI-146: the HD graphics, HD interface and HD pack lines (ARC-UI-089, ARC-UI-095, ARC-DEV-350) moved to the launcher screen (ARC-UI-143).
    });

    public static AreaOptionControl Of(AreaOption option) => All[(int)option];

    /// <summary>A page's controls in slot order (hidden ones included).</summary>
    public static IReadOnlyList<AreaOptionControl> Page(AreaOptionsPage page) => All.Where(control => control.Page == page).ToArray();

    /// <summary>A page Steamwoven offers: one with a shown control.</summary>
    public static bool Offered(AreaOptionsPage page) => All.Any(control => control.Page == page && control.Shown);
}

/// <summary>The values the window shows, from the host's settings (the original reads each through its getter when the page opens, OP4);
/// <paramref name="Rules"/> is the rule mode the session plays by (ARC-UI-014), <paramref name="Floats"/> and <paramref name="FloatSpeed"/>
/// the floating text's level and speed (ARC-UI-021).</summary>
public sealed record AreaGameOptions(int Difficulty, int CombatMode, bool AutoAttack, bool AlwaysRun, int Effects, int Voice, int Music,
    RuleMode Rules = RuleMode.Steamwoven, int Floats = FloaterRules.DefaultLevel, int FloatSpeed = FloaterRules.DefaultSpeed, bool EvidenceLog = false, int TestTask = 0,
    ControlSchemeSettings? Controls = null, bool CombatTaunts = true, bool ViolenceFilter = false, int Brightness = 0, bool AutoSwitchWeapons = false, bool FollowerSkills = true)
{
    /// <summary>ARC-GPAD-1374: the controller scheme choice the three lines show (the defaults when the host passes none).</summary>
    public ControlSchemeSettings Schemes => Controls ?? ControlSchemeSettings.Default;

    /// <summary>A shown control's value; null for a hidden one.</summary>
    public int? Value(AreaOption option) => option switch
    {
        AreaOption.Difficulty => Difficulty,
        AreaOption.DefaultCombatMode => CombatMode,
        AreaOption.AutoAttack => AutoAttack ? 1 : 0,
        AreaOption.AlwaysRun => AlwaysRun ? 1 : 0,
        AreaOption.Effects => Effects,
        AreaOption.Voice => Voice,
        AreaOption.Music => Music,
        AreaOption.Rules => (int)Rules,
        AreaOption.EvidenceLog => EvidenceLog ? 1 : 0,
        AreaOption.TestTask => TestTask,
        AreaOption.ControlScheme => (int)Schemes.Choice,
        AreaOption.PromptStyle => (int)Schemes.Prompts,
        AreaOption.Floats => Floats,
        AreaOption.FloatSpeed => FloatSpeed,
        AreaOption.CombatTaunts => CombatTaunts ? 1 : 0,
        AreaOption.ViolenceFilter => ViolenceFilter ? 1 : 0,
        AreaOption.Brightness => Brightness,
        AreaOption.AutoSwitchWeapons => AutoSwitchWeapons ? 1 : 0,
        AreaOption.FollowerSkills => FollowerSkills ? 1 : 0,
        _ => null,
    };
}

/// <summary>
/// ARC-UI-013: the original's settings the viewer keeps that no earlier store holds (Always Run and the volumes have their own):
/// <c>difficulty</c> 0–2 (easy, moderate, hard; default 1), <c>turn-based</c> (default off) and <c>auto attack</c> (default on), as
/// the original registers them (OP5); ARC-UI-021: <c>text floaters</c> 0–2 (default 1) and <c>float speed</c> 0–10 (default 2). User
/// preferences, never part of a save (FR-LIFECYCLE-004).
/// </summary>
public sealed record AreaGameSettings
{
    public const int MaxDifficulty = 2;

    private readonly int _difficulty;
    private readonly int _floats = FloaterRules.DefaultLevel;
    private readonly int _floatSpeed = FloaterRules.DefaultSpeed;

    public AreaGameSettings(int difficulty, bool turnBased, bool autoAttack) => (Difficulty, TurnBased, AutoAttack) = (difficulty, turnBased, autoAttack);

    public int Difficulty
    {
        get => _difficulty;
        init => _difficulty = value is < 0 or > MaxDifficulty ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }
    public bool TurnBased { get; init; }
    public bool AutoAttack { get; init; }

    /// <summary>ARC-UI-095: the interface art draws its HD redraws (default off); independent of the visual profile (the H key).</summary>
    public bool HdInterface { get; init; }

    /// <summary>ARC-UI-140: the interface draws its generated chrome where a recipe exists (default off; the options line's third value,
    /// saved with <see cref="HdInterface"/> on so the pictures keep the HD redraws).</summary>
    public bool GeneratedInterface { get; init; }

    /// <summary>ARC-DEV-350: where the active HD pack is read from (default Auto); it takes effect at the next start.</summary>
    public HdPackSourceMode HdPackSource { get; init; }

    /// <summary>ARC-DEV-350: the folder the player chose for <see cref="HdPackSourceMode.Custom"/> (a pack or a folder of packs); null until chosen.</summary>
    public string? HdPackPath { get; init; }

    /// <summary>ARC-GPAD-1374: the experimental controller scheme choice (concept §0.1): the pad scheme, the touch scheme and the prompt glyph
    /// family; all default to Off/Off/Auto. The original mouse and keyboard stay live whatever they hold.</summary>
    public PadScheme Pad { get; init; }
    public TouchScheme Touch { get; init; }
    public PromptStyle Prompts { get; init; }

    /// <summary>The three as the foundation core's record (a value outside its enum reads as its default there; a file with both a pad and the touch scheme on is C alone, as the Controls line shows it).</summary>
    public ControlSchemeSettings Controls { get { var raw = new ControlSchemeSettings(ControlSchemeSettings.CurrentVersion, Pad, Touch, Prompts).Validated(); return raw.WithChoice(raw.Choice); } }

    /// <summary>The "text floaters" level (<see cref="FloaterRules.Never"/>, messages, numbers).</summary>
    public int Floats
    {
        get => _floats;
        init => _floats = value is < 0 or > FloaterRules.MaxLevel ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

    /// <summary>ARC-AI-1527: the "combat taunts" setting (OH5, 0x4B82E0; default on, the original registers 1): at the end of a hit the hero's followers may comment (<see cref="Arcanum.Simulation.Ai.FollowerRules.TauntKindOf"/>).</summary>
    public bool CombatTaunts { get; init; } = true;

    /// <summary>ARC-UI-1737: the "violence filter" setting (OH2, the global 0x5DE6A4; default off): on, no pool of blood is left, a dying critter falls down plainly and the hit's blood effect is the clean one.</summary>
    public bool ViolenceFilter { get; init; }

    private readonly int _brightness;

    /// <summary>ARC-UI-1737: the "brightness" setting (OH1, whole values 0 to <see cref="BrightnessRules.Max"/>; default 0): the display's gamma factor 1.0 + 0.1 × the value, here the final colour multiply.</summary>
    public int Brightness
    {
        get => _brightness;
        init => _brightness = value is < 0 or > BrightnessRules.Max ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }
    /// <summary>ARC-COMBAT-1720: the "auto switch" setting (OH3, getter 0x4B8330; the original never registers it, so it reads 0 until written: default off): the hero's weapon wearing out wields the best remaining weapon for the target (0x4B83E0). The settings file's <c>auto switch</c> line; ARC-COMBAT-1738 shows its options row (Game page, the original's sixth slot).</summary>
    public bool AutoSwitchWeapons { get; init; }

    /// <summary>ARC-COMBAT-1777: the "follower skills" setting (OH4, getter 0x4C8FA0, the skill module's init registers 1: default on): the PC's lockpick item use and the skill bar's Disarm Traps let the follower with the best effectiveness minus difficulty act instead (the chooser 0x4C9050). The settings file's <c>follower skills</c> line; the options row is the Game page's seventh slot.</summary>
    public bool FollowerSkills { get; init; } = Arcanum.Simulation.Skills.FollowerSkillRules.DefaultOn;

    /// <summary>The "float speed" (a step of speed + 1 px).</summary>
    public int FloatSpeed
    {
        get => _floatSpeed;
        init => _floatSpeed = value is < 0 or > FloaterRules.MaxSpeed ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

    public static AreaGameSettings Defaults { get; } = new(1, RealTimeCombatRules.TurnBasedDefault, RealTimeCombatRules.AutoAttackDefault);

    /// <summary>The session's difficulty for the setting (0 easy, 1 normal, 2 hard: the skill and experience readers, SC2/XP1).</summary>
    public GameDifficulty SessionDifficulty => Difficulty switch { 0 => GameDifficulty.Easy, 2 => GameDifficulty.Hard, _ => GameDifficulty.Normal };
}

/// <summary>ARC-UI-013: the preference file's text, <c>{"version":5,"difficulty":D,"turnBased":B,"autoAttack":B,"floats":F,"floatSpeed":S,"hdInterface":B,"hdPack":"auto|game|custom","hdPackPath":null|"FOLDER","generatedInterface":B}</c>
/// (ARC-UI-021 added the floaters, ARC-UI-095 the HD interface, ARC-DEV-350 the pack source and path, ARC-UI-140 the generated interface);
/// a version 1 file (the first four) reads with the floaters' defaults, a version 2 file (the first six) with the HD interface off, a
/// version 3 file (the first seven) with the HD pack source Auto and a version 4 file (the first nine) with the generated interface off. Anything else (another version, a missing, extra or out-of-range value) reads as null; the caller then uses the defaults.</summary>
public static class AreaGameSettingsCodec
{
    public const int Version = 5;
    /// <summary>The longest folder path the file may hold.</summary>
    public const int MaxPath = 1024;

    public static string Serialize(AreaGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"version\":{Version},\"difficulty\":{settings.Difficulty},\"turnBased\":{(settings.TurnBased ? "true" : "false")},\"autoAttack\":{(settings.AutoAttack ? "true" : "false")},\"floats\":{settings.Floats},\"floatSpeed\":{settings.FloatSpeed},\"hdInterface\":{(settings.HdInterface ? "true" : "false")},\"hdPack\":\"{HdPackSourceResolver.ModeName(settings.HdPackSource)}\",\"hdPackPath\":{(settings.HdPackPath is null ? "null" : JsonSerializer.Serialize(settings.HdPackPath))},\"generatedInterface\":{(settings.GeneratedInterface ? "true" : "false")}}}");
    }

    public static AreaGameSettings? Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v is not (1 or 2 or 3 or 4 or Version)) return null;
            var count = v == 1 ? 4 : v == 2 ? 6 : v == 3 ? 7 : v == 4 ? 9 : 10;
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != count || names.Distinct(StringComparer.Ordinal).Count() != count) return null;
            if (!Int(root, "difficulty", 0, AreaGameSettings.MaxDifficulty, out var d)) return null;
            if (!Bool(root, "turnBased", out var turnBased) || !Bool(root, "autoAttack", out var autoAttack)) return null;
            var settings = new AreaGameSettings(d, turnBased, autoAttack);
            if (v == 1) return settings;
            if (!Int(root, "floats", 0, FloaterRules.MaxLevel, out var floats) || !Int(root, "floatSpeed", 0, FloaterRules.MaxSpeed, out var speed)) return null;
            settings = settings with { Floats = floats, FloatSpeed = speed };
            if (v == 2) return settings;
            if (!Bool(root, "hdInterface", out var hdInterface)) return null;
            settings = settings with { HdInterface = hdInterface };
            if (v == 3) return settings;
            if (!root.TryGetProperty("hdPack", out var mode) || mode.ValueKind != JsonValueKind.String || !HdPackSourceResolver.TryParseMode(mode.GetString(), out var source)) return null;
            if (!root.TryGetProperty("hdPackPath", out var path) || path.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
            var folder = path.GetString();
            if (folder is not null && (folder.Length == 0 || folder.Length > MaxPath || folder.Contains('\0'))) return null;
            settings = settings with { HdPackSource = source, HdPackPath = folder };
            if (v == 4) return settings;
            if (!Bool(root, "generatedInterface", out var generated)) return null;
            // The generated interface draws through the HD interface, so it is on only with it (the options line writes both).
            return settings with { GeneratedInterface = generated && settings.HdInterface };
        }
        catch (JsonException) { return null; }
    }

    private static bool Int(JsonElement root, string name, int minimum, int maximum, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)
            && value >= minimum && value <= maximum;
    }

    private static bool Bool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }
}

/// <summary>
/// ARC-UI-013: the options window's texts: the control labels of <c>mes\options_ui.mes</c> and the value texts of the list controls'
/// files, read from the installed game when they read (the player's language), otherwise the project's English text with the reason
/// in <see cref="Detail"/>. The page and Done buttons' labels are in <c>MainMenu.mes</c> (<see cref="AreaMainMenuTexts"/>). Godot-free.
/// </summary>
public sealed class AreaOptionsTexts
{
    public const string LabelsPath = "mes/options_ui.mes";
    private const int MaxText = 64;
    private static readonly IReadOnlyDictionary<int, string> ProjectLabels = new Dictionary<int, string>
    {
        [100] = "Module", [101] = "Difficulty", [102] = "Violence Filter", [103] = "Default Combat Mode", [104] = "Auto Attack",
        [105] = "Auto Switch Weapons", [106] = "Always Run", [107] = "Follower Skills",
        [200] = "Brightness", [201] = "Text Duration", [202] = "Floats", [203] = "Float Speed", [204] = "Combat Taunts",
        [300] = "Effects", [301] = "Voice", [302] = "Music",
    };
    private static readonly IReadOnlyDictionary<string, string[]> ProjectValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [AreaOptionTable.OffOn] = ["Off", "On"],
        [AreaOptionTable.Difficulties] = ["Easy", "Moderate", "Hard"],
        [AreaOptionTable.CombatModes] = ["Real Time", "Turn Based", "Fast Turn Based"],
        [AreaOptionTable.FloatLevels] = ["Never", "Minimal", "Verbose"],
    };
    // ARC-UI-014: the project controls' texts, never looked up in the game's files.
    private static readonly IReadOnlyDictionary<AreaOption, string> ProjectControlLabels = new Dictionary<AreaOption, string>
    {
        [AreaOption.Rules] = "Rules", [AreaOption.EvidenceLog] = "Evidence Log", [AreaOption.TestTask] = "Test Task",
        [AreaOption.ControlScheme] = "Controls", [AreaOption.PromptStyle] = "Prompt style",
    };
    private static readonly IReadOnlyDictionary<string, string[]> ProjectControlValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [AreaOptionTable.RuleModes] = ["Steamwoven", "Original"],
        [AreaOptionTable.OffOnProject] = ["Off", "On"],
        [AreaOptionTable.TestTasks] = ["None", "Task 1", "Task 2", "Task 3", "Task 4", "Task 5", "Task 6", "Task 7", "Task 8"],
        [AreaOptionTable.ControlSchemes] = ["Original", "A Direct", "B Pointer", "C Touch+pad"],
        [AreaOptionTable.PromptStyles] = ["Auto", "Xbox", "PlayStation", "Nintendo", "Steam Deck"],
    };
    private readonly Func<string, int, string?> _original;

    private AreaOptionsTexts(Func<string, int, string?> original, bool fromGame, string detail)
    {
        _original = original; FromGame = fromGame; Detail = detail;
    }

    public bool FromGame { get; }
    public string Detail { get; }

    public static AreaOptionsTexts Project(string reason) => new((_, _) => null, false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Texts over a lookup by file and message number (tests); what it does not answer takes the project's text.</summary>
    public static AreaOptionsTexts From(Func<string, int, string?> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads the label and value files from the game folder of <paramref name="modulePath"/> (game start sources, as the
    /// menu's texts); a file that does not read leaves its texts the project's, named in <see cref="Detail"/>.</summary>
    public static AreaOptionsTexts Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Project($"{LabelsPath} not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Project($"{LabelsPath} not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var kept = new Dictionary<(string, int), string>();
        var missing = new List<string>();
        foreach (var (path, numbers) in ProjectValues.Select(pair => (pair.Key, Enumerable.Range(0, pair.Value.Length)))
            .Prepend((LabelsPath, ProjectLabels.Keys)))
        {
            if (AreaStartingHero.Table(stack, path, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table) { missing.Add(detail ?? path); continue; }
            foreach (var number in numbers)
                if (table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= MaxText } text })
                    kept[(path, number)] = text;
        }
        var total = ProjectLabels.Count + ProjectValues.Values.Sum(values => values.Length);
        return new((path, number) => kept.GetValueOrDefault((path, number)), true,
            $"options texts: {kept.Count} of {total}" + (missing.Count == 0 ? "" : "; not read: " + string.Join(", ", missing)));
    }

    /// <summary>A control's label.</summary>
    public string Label(AreaOptionControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.LabelMessage is not { } message) return ProjectControlLabels.GetValueOrDefault(control.Option, $"[{control.Option}]");
        return _original(LabelsPath, message) is { Length: > 0 } text ? text : ProjectLabels.GetValueOrDefault(message, $"[{message}]");
    }

    /// <summary>A value as the control shows it: a list's text (message <paramref name="value"/> of its file), a slider's number.</summary>
    public string Value(AreaOptionControl control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.ValueTable is not { } table) return value.ToString(CultureInfo.InvariantCulture) + "/" + control.Max.ToString(CultureInfo.InvariantCulture);
        if (control.Project)
            return ProjectControlValues.TryGetValue(table, out var own) && value >= 0 && value < own.Length ? own[value] : $"[{value}]";
        if (_original(table, value) is { Length: > 0 } text) return text;
        return ProjectValues.TryGetValue(table, out var values) && value >= 0 && value < values.Length ? values[value] : $"[{value}]";
    }
}
