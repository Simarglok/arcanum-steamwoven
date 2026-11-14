using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanum.Input;
using Arcanum.Presentation.Audio;
using Arcanum.Presentation.Display;
using Arcanum.Presentation.Hud;
using Arcanum.Session.Launcher;
using Arcanum.Session.Settings;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1130: the viewer's player settings as lines of the one settings file (<c>arcanum.cfg</c>, <see cref="ArcanumConfig"/>), Godot-free.
/// The settings the original registers keep the original's names and value shape (OP5, <see cref="AreaOptionTable"/>): <c>difficulty</c> 0–2,
/// <c>turn-based</c> and <c>auto attack</c> and <c>always run</c> and <c>violence filter</c> (ARC-UI-1737) 0 or 1, <c>brightness</c> 0–9 (ARC-UI-1737), <c>text floaters</c> 0–2, <c>float speed</c> 0–10 and the three
/// volumes <c>effects volume</c>, <c>voice volume</c> and <c>music volume</c> 0–10. The project's own settings have project names:
/// <c>pad scheme</c> (<c>off</c>, <c>direct</c>, <c>pointer</c>), <c>touch scheme</c> (<c>off</c>, <c>touchpad</c>) and <c>prompt style</c> (<c>auto</c>, <c>xbox</c>,
/// <c>playstation</c>, <c>nintendo</c>, <c>steamdeck</c>; ARC-GPAD-1374, the controller scheme choice of <see cref="ControlSchemeSettings"/>),
/// <c>hd interface</c> and <c>generated interface</c> (0 or 1), <c>hd pack</c> (<c>auto</c>, <c>game</c>, <c>custom</c>), <c>hd pack path</c>,
/// <c>visual profile</c> (<c>legacy</c> or <c>hd</c>) and <c>launcher</c> (the launcher object, <see cref="LauncherSettingsFile"/>). A line that is
/// absent or has a value outside its range reads as that setting's default and no other setting changes (the old files lost a whole group
/// to one bad value). A group is saved at once; a save writes only its own lines.
/// </summary>
public static class ViewerSettingsConfig
{
    public const string Difficulty = "difficulty";
    public const string TurnBased = "turn-based";
    public const string AutoAttack = "auto attack";
    public const string AlwaysRun = "always run";
    public const string TextFloaters = "text floaters";
    public const string FloatSpeed = "float speed";
    public const string CombatTaunts = "combat taunts";
    public const string ViolenceFilter = "violence filter";
    public const string Brightness = "brightness";
    public const string TextDuration = "text duration";
    public const string AutoSwitch = "auto switch";
    public const string FollowerSkills = "follower skills";
    public const string EffectsVolume = "effects volume";
    public const string VoiceVolume = "voice volume";
    public const string MusicVolume = "music volume";
    public const string HdInterface = "hd interface";
    public const string GeneratedInterface = "generated interface";
    public const string HdPack = "hd pack";
    public const string HdPackPath = "hd pack path";
    public const string VisualProfileLine = "visual profile";
    public const string PadSchemeLine = "pad scheme";
    public const string TouchSchemeLine = "touch scheme";
    public const string PromptStyleLine = "prompt style";

    private static readonly string[] GameLines = [Difficulty, TurnBased, AutoAttack, TextFloaters, FloatSpeed, CombatTaunts, ViolenceFilter, Brightness, TextDuration, AutoSwitch, FollowerSkills, HdInterface, GeneratedInterface, HdPack, HdPackPath, PadSchemeLine, TouchSchemeLine, PromptStyleLine];
    private static readonly string[] AudioLines = [EffectsVolume, VoiceVolume, MusicVolume];

    private static int? Int(ArcanumConfigRead read, string name, int minimum, int maximum)
        => read.Get(name) is { } text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum ? value : null;

    private static bool? Flag(ArcanumConfigRead read, string name) => read.Get(name) switch { "0" => false, "1" => true, _ => null };

    private static string Flag(bool value) => value ? "1" : "0";

    /// <summary>ARC-GPAD-1374: an enum line is its lower-case name; an unknown word reads as the default of that line alone.</summary>
    private static T Choice<T>(ArcanumConfigRead read, string name, T fallback) where T : struct, Enum
    {
        if (read.Get(name) is not { } text) return fallback;
        foreach (var candidate in Enum.GetValues<T>())
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)) return candidate;
        return fallback;
    }

    private static string Choice<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether the file holds any line of the game settings group (startup reports "saved setting" only then).</summary>
    public static bool HasGame(ArcanumConfigRead read) => GameLines.Any(read.Has);

    public static bool HasAudio(ArcanumConfigRead read) => AudioLines.Any(read.Has);

    public static bool HasVisualProfile(ArcanumConfigRead read) => read.Has(VisualProfileLine);

    public static AreaGameSettings ReadGame(ArcanumConfigRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var defaults = AreaGameSettings.Defaults;
        var hdInterface = Flag(read, HdInterface) ?? false;
        string? folder = read.Get(HdPackPath) is { Length: > 0 and <= AreaGameSettingsCodec.MaxPath } path ? path : null;
        return new AreaGameSettings(Int(read, Difficulty, 0, AreaGameSettings.MaxDifficulty) ?? defaults.Difficulty,
            Flag(read, TurnBased) ?? defaults.TurnBased, Flag(read, AutoAttack) ?? defaults.AutoAttack)
        {
            Floats = Int(read, TextFloaters, 0, FloaterRules.MaxLevel) ?? defaults.Floats,
            FloatSpeed = Int(read, FloatSpeed, 0, FloaterRules.MaxSpeed) ?? defaults.FloatSpeed,
            CombatTaunts = Flag(read, CombatTaunts) ?? defaults.CombatTaunts,
            ViolenceFilter = Flag(read, ViolenceFilter) ?? defaults.ViolenceFilter,
            Brightness = Int(read, Brightness, 0, BrightnessRules.Max) ?? defaults.Brightness,
            TextDuration = Int(read, TextDuration, 0, AreaOptionTable.TextDurationMax) ?? defaults.TextDuration,
            AutoSwitchWeapons = Flag(read, AutoSwitch) ?? defaults.AutoSwitchWeapons,
            FollowerSkills = Flag(read, FollowerSkills) ?? defaults.FollowerSkills,
            HdInterface = hdInterface,
            // The generated interface draws through the HD interface, so it is on only with it (the options line writes both).
            GeneratedInterface = (Flag(read, GeneratedInterface) ?? false) && hdInterface,
            HdPackSource = HdPackSourceResolver.TryParseMode(read.Get(HdPack)?.ToLowerInvariant(), out var mode) ? mode : defaults.HdPackSource,
            HdPackPath = folder,
            Pad = Choice(read, PadSchemeLine, defaults.Pad),
            Touch = Choice(read, TouchSchemeLine, defaults.Touch),
            Prompts = Choice(read, PromptStyleLine, defaults.Prompts),
        };
    }

    /// <summary>The lines of <paramref name="settings"/>. A folder path the file cannot hold (white space at an end, a control character, over
    /// <see cref="AreaGameSettingsCodec.MaxPath"/>) is not stored: its line is removed.</summary>
    public static IReadOnlyList<ArcanumConfigChange> GameChanges(AreaGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = settings.HdPackPath is { Length: <= AreaGameSettingsCodec.MaxPath } folder && ArcanumConfig.ValidValue(folder) ? folder : null;
        return
        [
            new(Difficulty, Number(settings.Difficulty)), new(TurnBased, Flag(settings.TurnBased)), new(AutoAttack, Flag(settings.AutoAttack)),
            new(TextFloaters, Number(settings.Floats)), new(FloatSpeed, Number(settings.FloatSpeed)), new(CombatTaunts, Flag(settings.CombatTaunts)), new(AutoSwitch, Flag(settings.AutoSwitchWeapons)), new(FollowerSkills, Flag(settings.FollowerSkills)),
            new(ViolenceFilter, Flag(settings.ViolenceFilter)), new(Brightness, Number(settings.Brightness)), new(TextDuration, Number(settings.TextDuration)),
            new(HdInterface, Flag(settings.HdInterface)), new(GeneratedInterface, Flag(settings.GeneratedInterface && settings.HdInterface)),
            new(HdPack, HdPackSourceResolver.ModeName(settings.HdPackSource)), new(HdPackPath, path),
            new(PadSchemeLine, Choice(settings.Controls.Pad)), new(TouchSchemeLine, Choice(settings.Controls.Touch)), new(PromptStyleLine, Choice(settings.Controls.Prompts)),
        ];
    }

    public static AudioVolumes ReadAudio(ArcanumConfigRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new(Int(read, EffectsVolume, 0, AudioVolumes.Max) ?? AudioVolumes.Default, Int(read, VoiceVolume, 0, AudioVolumes.Max) ?? AudioVolumes.Default,
            Int(read, MusicVolume, 0, AudioVolumes.Max) ?? AudioVolumes.Default);
    }

    public static IReadOnlyList<ArcanumConfigChange> AudioChanges(AudioVolumes volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        return [new(EffectsVolume, Number(volumes.Effects)), new(VoiceVolume, Number(volumes.Voice)), new(MusicVolume, Number(volumes.Music))];
    }

    public static bool ReadAlwaysRun(ArcanumConfigRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Flag(read, AlwaysRun) ?? false;
    }

    public static IReadOnlyList<ArcanumConfigChange> AlwaysRunChanges(bool value) => [new(AlwaysRun, Flag(value))];

    public static VisualProfile ReadVisualProfile(ArcanumConfigRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return string.Equals(read.Get(VisualProfileLine), "hd", StringComparison.OrdinalIgnoreCase) ? VisualProfile.Hd : VisualProfile.Legacy;
    }

    public static IReadOnlyList<ArcanumConfigChange> VisualProfileChanges(VisualProfile profile)
        => [new(VisualProfileLine, profile switch { VisualProfile.Legacy => "legacy", VisualProfile.Hd => "hd", _ => throw new ArgumentOutOfRangeException(nameof(profile)) })];
}

/// <summary>ARC-UI-1130: the texts of the files the viewer kept its settings in before the one settings file (<c>null</c>: no such file).</summary>
public sealed record LegacySettingsTexts(string? Game, string? Audio, string? AlwaysRun, string? VisualProfile, string? Launcher)
{
    public const string GameFile = "game_settings_v1.json";
    public const string AudioFile = "audio_volumes_v1.json";
    public const string AlwaysRunFile = "always_run_v1.json";
    public const string VisualProfileFile = "visual_profile_v1.json";
    public const string LauncherFile = "launcher_v1.json";

    public bool Any => Game is not null || Audio is not null || AlwaysRun is not null || VisualProfile is not null || Launcher is not null;
}

/// <summary>ARC-UI-1130: the one-time move of a player's earlier settings files into the settings file. Each file is read by its own old
/// codec, so a file that was valid keeps every value and an invalid one is skipped (its settings stay at their defaults); the old files are
/// never changed or deleted, and are not read again once the settings file exists.</summary>
public static class ViewerSettingsMigration
{
    public static IReadOnlyList<ArcanumConfigChange> Import(LegacySettingsTexts legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var changes = new List<ArcanumConfigChange>();
        if (legacy.Game is { } game && AreaGameSettingsCodec.Deserialize(game) is { } settings) changes.AddRange(ViewerSettingsConfig.GameChanges(settings));
        if (legacy.Audio is { } audio && AudioVolumesCodec.Deserialize(audio) is { } volumes) changes.AddRange(ViewerSettingsConfig.AudioChanges(volumes));
        if (legacy.AlwaysRun?.Trim() is ("on" or "off") and var run) changes.AddRange(ViewerSettingsConfig.AlwaysRunChanges(run == "on"));
        // The old file's text is exactly the codec's text of one profile; anything else (an unknown value, damage) is skipped, not turned into Legacy.
        if (legacy.VisualProfile?.Trim() is { } profile)
            foreach (var known in new[] { VisualProfile.Legacy, VisualProfile.Hd })
                if (VisualProfileCodec.Serialize(known) == profile) changes.AddRange(ViewerSettingsConfig.VisualProfileChanges(known));
        if (legacy.Launcher is { } launcher && LauncherLine(launcher) is { } line) changes.Add(new(LauncherSettingsFile.Line, line));
        return changes;
    }

    /// <summary>The launcher line of an old launcher file: a file this build reads is written again with its unknown keys; one of another
    /// schema (a newer build's) is kept as it is, on one line, so it stays refused for overwriting; an invalid one is skipped.</summary>
    private static string? LauncherLine(string text)
    {
        var read = LauncherSettingsCodec.Deserialize(text);
        try
        {
            return read.Status switch
            {
                LauncherSettingsReadStatus.Read => LauncherSettingsCodec.Serialize(read.Settings, read, indented: false),
                LauncherSettingsReadStatus.OtherSchema when JsonNode.Parse(text) is JsonObject root && root.ToJsonString() is { } compact
                    && ArcanumConfig.ValidValue(compact) => compact,
                _ => null,
            };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
}
