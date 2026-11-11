using System.Globalization;
using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
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
    Rules, HdGraphics, HdInterface, HdPack,
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
    public const string NoViolenceFilter = "its effect on blood and death art is not built";
    public const string NoAutoSwitch = "switching weapons by itself is not built";
    public const string NoFollowerSkills = "followers do not use skills on their own yet";
    public const string NoBrightness = "the gamma control is not built";
    public const string NoTextDuration = "text bubbles are not built";
    public const string NoTaunts = "combat taunts are not built";
    public const string NoFastTurnBased = "Fast Turn Based is not built";
    public const string NoVideoPage = "brightness, text bubbles and combat taunts are not built";
    public const string OffOn = "mes/OptionsOffOn.mes";
    public const string Difficulties = "mes/OptionsDifficulty.mes";
    public const string CombatModes = "mes/OptionsCombatMode.mes";
    public const string FloatLevels = "mes/OptionsFloats.mes";
    /// <summary>The rule mode control's values (a project list, no game file): <see cref="RuleMode"/> in its order.</summary>
    public const string RuleModes = "project:rule modes";
    /// <summary>ARC-UI-089: the HD graphics control's values (a project list): Off, On.</summary>
    public const string HdGraphicsValues = "project:hd graphics";
    /// <summary>ARC-UI-095: the HD interface control's values (a project list): Off, On.</summary>
    public const string HdInterfaceValues = "project:hd interface";
    /// <summary>ARC-DEV-350: the HD pack source control's values (a project list): Auto, Game, Custom (<see cref="HdPackSourceMode"/>).</summary>
    public const string HdPackValues = "project:hd pack";

    /// <summary>Every control of the original's table (0x5CCD78, three pages of eight slots; OP2).</summary>
    public static IReadOnlyList<AreaOptionControl> All { get; } = Array.AsReadOnly(new AreaOptionControl[]
    {
        new(AreaOption.Module, AreaOptionsPage.Game, 0, AreaOptionKind.Modules, 0, 0, null, "module", 0, NoModules),
        new(AreaOption.Difficulty, AreaOptionsPage.Game, 1, AreaOptionKind.List, 2, 2, Difficulties, "difficulty", 1, null),
        new(AreaOption.ViolenceFilter, AreaOptionsPage.Game, 2, AreaOptionKind.List, 1, 1, OffOn, "violence filter", 0, NoViolenceFilter),
        new(AreaOption.DefaultCombatMode, AreaOptionsPage.Game, 3, AreaOptionKind.List, 2, 1, CombatModes, "turn-based", 0, null, NoFastTurnBased),
        new(AreaOption.AutoAttack, AreaOptionsPage.Game, 4, AreaOptionKind.List, 1, 1, OffOn, "auto attack", 1, null),
        new(AreaOption.AutoSwitchWeapons, AreaOptionsPage.Game, 5, AreaOptionKind.List, 1, 1, OffOn, "auto switch", 0, NoAutoSwitch),
        new(AreaOption.AlwaysRun, AreaOptionsPage.Game, 6, AreaOptionKind.List, 1, 1, OffOn, "always run", 0, null),
        new(AreaOption.FollowerSkills, AreaOptionsPage.Game, 7, AreaOptionKind.List, 1, 1, OffOn, "follower skills", 1, NoFollowerSkills),
        new(AreaOption.Brightness, AreaOptionsPage.Video, 0, AreaOptionKind.Slider, 9, 9, null, "brightness", 0, NoBrightness),
        new(AreaOption.TextDuration, AreaOptionsPage.Video, 1, AreaOptionKind.Slider, 10, 10, null, "text duration", 6, NoTextDuration),
        // ARC-UI-021: the floating text (CH10).
        new(AreaOption.Floats, AreaOptionsPage.Video, 2, AreaOptionKind.List, 2, 2, FloatLevels, "text floaters", FloaterRules.DefaultLevel, null),
        new(AreaOption.FloatSpeed, AreaOptionsPage.Video, 3, AreaOptionKind.Slider, 10, 10, null, "float speed", FloaterRules.DefaultSpeed, null),
        new(AreaOption.CombatTaunts, AreaOptionsPage.Video, 4, AreaOptionKind.List, 1, 1, OffOn, "combat taunts", 1, NoTaunts),
        new(AreaOption.Effects, AreaOptionsPage.Sound, 0, AreaOptionKind.Slider, 10, 10, null, "effects volume", 5, null),
        new(AreaOption.Voice, AreaOptionsPage.Sound, 1, AreaOptionKind.Slider, 10, 10, null, "voice volume", 5, null),
        new(AreaOption.Music, AreaOptionsPage.Sound, 2, AreaOptionKind.Slider, 10, 10, null, "music volume", 5, null),
        // ARC-UI-014: the rule mode (owner decision D8: the settings menu may switch it in play), after the original's eight Game slots.
        new(AreaOption.Rules, AreaOptionsPage.Game, 8, AreaOptionKind.List, 1, 1, RuleModes, "rules", (int)RuleMode.Steamwoven, null, Project: true),
        // ARC-UI-089: HD graphics, beside the rule mode: the saved visual profile (Legacy/HD, ARC-UI-006) for the pack's sprites and ground.
        new(AreaOption.HdGraphics, AreaOptionsPage.Game, 9, AreaOptionKind.List, 1, 1, HdGraphicsValues, "hd graphics", 0, null, Project: true),
        // ARC-UI-095: HD interface, its own switch for the interface art alone, independent of the visual profile.
        new(AreaOption.HdInterface, AreaOptionsPage.Game, 10, AreaOptionKind.List, 1, 1, HdInterfaceValues, "hd interface", 0, null, Project: true),
        // ARC-DEV-350: where the HD pack is read from (Auto: the iCloud copy when it is here and whole, else the installed copy).
        new(AreaOption.HdPack, AreaOptionsPage.Game, 11, AreaOptionKind.List, 2, 2, HdPackValues, "hd pack", 0, null, Project: true),
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
    RuleMode Rules = RuleMode.Steamwoven, int Floats = FloaterRules.DefaultLevel, int FloatSpeed = FloaterRules.DefaultSpeed, bool HdGraphics = false,
    bool HdInterface = false, HdPackSourceMode HdPackSource = HdPackSourceMode.Auto)
{
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
        AreaOption.HdGraphics => HdGraphics ? 1 : 0,
        AreaOption.HdInterface => HdInterface ? 1 : 0,
        AreaOption.HdPack => (int)HdPackSource,
        AreaOption.Floats => Floats,
        AreaOption.FloatSpeed => FloatSpeed,
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

    /// <summary>ARC-DEV-350: where the active HD pack is read from (default Auto); it takes effect at the next start.</summary>
    public HdPackSourceMode HdPackSource { get; init; }

    /// <summary>ARC-DEV-350: the folder the player chose for <see cref="HdPackSourceMode.Custom"/> (a pack or a folder of packs); null until chosen.</summary>
    public string? HdPackPath { get; init; }

    /// <summary>The "text floaters" level (<see cref="FloaterRules.Never"/>, messages, numbers).</summary>
    public int Floats
    {
        get => _floats;
        init => _floats = value is < 0 or > FloaterRules.MaxLevel ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

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

/// <summary>ARC-UI-013: the preference file's text, <c>{"version":4,"difficulty":D,"turnBased":B,"autoAttack":B,"floats":F,"floatSpeed":S,"hdInterface":B,"hdPack":"auto|game|custom","hdPackPath":null|"FOLDER"}</c>
/// (ARC-UI-021 added the floaters, ARC-UI-095 the HD interface, ARC-DEV-350 the last two); a version 1 file (the first four) reads with the
/// floaters' defaults, a version 2 file (the first six) with the HD interface off and a version 3 file (the first seven) with the HD pack
/// source Auto. Anything else (another version, a missing, extra or out-of-range value) reads as null; the caller then uses the defaults.</summary>
public static class AreaGameSettingsCodec
{
    public const int Version = 4;
    /// <summary>The longest folder path the file may hold.</summary>
    public const int MaxPath = 1024;

    public static string Serialize(AreaGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"version\":{Version},\"difficulty\":{settings.Difficulty},\"turnBased\":{(settings.TurnBased ? "true" : "false")},\"autoAttack\":{(settings.AutoAttack ? "true" : "false")},\"floats\":{settings.Floats},\"floatSpeed\":{settings.FloatSpeed},\"hdInterface\":{(settings.HdInterface ? "true" : "false")},\"hdPack\":\"{HdPackSourceResolver.ModeName(settings.HdPackSource)}\",\"hdPackPath\":{(settings.HdPackPath is null ? "null" : JsonSerializer.Serialize(settings.HdPackPath))}}}");
    }

    public static AreaGameSettings? Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v is not (1 or 2 or 3 or Version)) return null;
            var count = v == 1 ? 4 : v == 2 ? 6 : v == 3 ? 7 : 9;
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
            return settings with { HdPackSource = source, HdPackPath = folder };
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
    private static readonly IReadOnlyDictionary<AreaOption, string> ProjectControlLabels = new Dictionary<AreaOption, string> { [AreaOption.Rules] = "Rules", [AreaOption.HdGraphics] = "HD graphics", [AreaOption.HdInterface] = "HD interface", [AreaOption.HdPack] = "HD pack" };
    private static readonly IReadOnlyDictionary<string, string[]> ProjectControlValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [AreaOptionTable.RuleModes] = ["Steamwoven", "Original"],
        [AreaOptionTable.HdGraphicsValues] = ["Off", "On"],
        [AreaOptionTable.HdInterfaceValues] = ["Off", "On"],
        [AreaOptionTable.HdPackValues] = ["Auto", "Game", "Custom"],
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
