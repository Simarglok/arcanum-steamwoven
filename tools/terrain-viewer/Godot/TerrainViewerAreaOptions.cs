using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-013: the options window (the original's window 6, ledger OP1–OP8), opened by Options in the main menu and in both in-play
// windows. Each change applies at once, as the original's setters do: the difficulty goes to the session (its skill and experience
// readers), the default combat mode is the turn-based setting Space also toggles (a hero in combat mode starts or ends a round),
// auto attack is the real-time attack repeat's setting, Always Run and the three volumes are the palette's. They are user
// preferences, saved at once in the viewer's preference files (the original writes arcanum.cfg when the game exits) and never in a
// save; Always Run and the volumes keep their own files. ARC-UI-014: the window's Rules line switches the session between the two
// built-in rule modes (owner decision D8); every session the viewer then makes, loads or brings takes the chosen mode. The mode is
// stored in the launcher's settings file (ARC-DEV-046's store), which the launcher screen also writes; each ordinary run starts in it.
public partial class TerrainViewerApp
{
    private GameSettingsStore? _gameSettingsStore;
    private AreaGameSettings _gameSettings = AreaGameSettings.Defaults;
    private string _gameSettingsOrigin = "default";
    private AreaOptionsTexts? _optionsTexts;
    private RuleMode _ruleMode = RuleMode.Steamwoven;

    private void ConfigureGameSettings()
    {
        _gameSettingsStore = new GameSettingsStore(AutomatedVisualRun);
        _gameSettings = _gameSettingsStore.Load();
        _gameSettingsOrigin = !_gameSettingsStore.Persistent ? "default (automated run)" : _gameSettingsStore.HasSaved ? "saved setting" : "default (nothing saved yet)";
        _turnBasedSetting = _gameSettings.TurnBased;
        // ARC-UI-014: the rule mode of the launcher settings (read once a run; a start through the launcher has its Play's).
        LauncherSettingsRead();
        _ruleMode = _launcherSettings.Rules;
        GD.Print($"ARC-UI-014 rules {RuleModeNames.Name(_ruleMode)} from {(LauncherSettingsPath() is null ? "default (automated run)" : "launcher settings")}");
        GD.Print($"ARC-UI-013 game settings difficulty={_gameSettings.Difficulty} turnBased={_gameSettings.TurnBased} autoAttack={_gameSettings.AutoAttack} floats={_gameSettings.Floats} floatSpeed={_gameSettings.FloatSpeed} hdInterface={_gameSettings.HdInterface} generatedInterface={_gameSettings.GeneratedInterface} hdPack={HdPackSourceResolver.ModeName(_gameSettings.HdPackSource)} hdPackPath={(_gameSettings.HdPackPath is null ? "none" : "set")} from={_gameSettingsOrigin}");
    }

    /// <summary>The values the options window shows: the host's settings as they are now.</summary>
    private AreaGameOptions CurrentGameOptions() => new(_gameSettings.Difficulty, _turnBasedSetting ? 1 : 0, _gameSettings.AutoAttack, _alwaysRunOption,
        _audioVolumes.Effects, _audioVolumes.Voice, _audioVolumes.Music, _ruleMode, _gameSettings.Floats, _gameSettings.FloatSpeed);

    /// <summary>The options texts, read once a run (the game's files, or the project's with the reason).</summary>
    private AreaOptionsTexts OptionsTexts()
    {
        if (_optionsTexts is { } read) return read;
        var module = (_arguments ?? _launchArguments)?.ModulePath;
        var texts = SelfTestBootstrap || module is null ? AreaOptionsTexts.Project(module is null ? "no module" : "self-test: no game folder") : AreaOptionsTexts.Load(module);
        GD.Print($"ARC-UI-013 options texts fromGame={texts.FromGame} detail={texts.Detail}");
        return _optionsTexts = texts;
    }

    /// <summary>Before the menu renders the options window: its texts and the values the host has now.</summary>
    private void BindOptionsWindow()
    {
        if (_mainMenu is not { Window: AreaMenuWindow.Options } menu) return;
        menu.OptionsTexts = OptionsTexts();
        menu.SetOptions(CurrentGameOptions());
    }

    /// <summary>Applies a change the options window asked for; null when it took effect, else why the setting stayed.</summary>
    private string? ApplyOptionChange(AreaOption option, int value)
    {
        string? refused = null;
        switch (option)
        {
            case AreaOption.Difficulty:
                SaveGameSettings(_gameSettings with { Difficulty = value });
                if (_areaSession is { } session) ApplySessionSettings(session);
                break;
            case AreaOption.DefaultCombatMode:
                var turnBased = value == 1;
                // The original's setter runs the turn-based switch (OP5): with a hero it is Space's toggle; with none (the main menu over
                // a scene without a hero) the setting changes alone.
                if (turnBased != _turnBasedSetting)
                {
                    if (_areaSession?.QueryHero() is not null && _combatHost is not null && _worldActor.Session is AreaGridSession) ToggleTurnBased();
                    else _turnBasedSetting = turnBased;
                }
                if (_turnBasedSetting != turnBased) refused = _turnBasedNotice;
                else SaveGameSettings(_gameSettings with { TurnBased = turnBased });
                break;
            case AreaOption.AutoAttack: SaveGameSettings(_gameSettings with { AutoAttack = value == 1 }); break;
            // ARC-UI-021: the floating text's level and speed (CH10); lines in flight keep moving at the new speed.
            case AreaOption.Floats or AreaOption.FloatSpeed:
                var floater = AreaOptionTable.Of(option);
                if (value < 0 || value > floater.Limit) { refused = "out of range"; break; }
                SaveGameSettings(option == AreaOption.Floats ? _gameSettings with { Floats = value } : _gameSettings with { FloatSpeed = value });
                break;
            case AreaOption.AlwaysRun: SetAlwaysRun(value == 1); break;
            case AreaOption.Effects: PaletteVolumeChanged(AudioChannel.Effects, value); break;
            case AreaOption.Voice: PaletteVolumeChanged(AudioChannel.Voice, value); break;
            case AreaOption.Music: PaletteVolumeChanged(AudioChannel.Music, value); break;
            case AreaOption.Rules:
                // The contract's safety rules make a switch in play safe: the next decision of every domain follows it, nothing is converted
                // and nothing is saved (docs/world/rule-modes.md).
                if (!Enum.IsDefined((RuleMode)value)) { refused = "no such rule mode"; break; }
                _ruleMode = (RuleMode)value;
                SaveLauncherRules();
                if (_areaSession is { } ruled) ApplySessionSettings(ruled);
                break;
            default: refused = option + " is not offered"; break;
        }
        GD.Print($"ARC-UI-013 option {option}={value} {(refused is null ? "applied" : "refused: " + refused)}");
        return refused;
    }

    /// <summary>What the next start will read for the saved HD pack source (the launcher screen's note, ARC-UI-143).</summary>
    private string HdPackSourceText()
    {
        // An automated run reads no pack folder (ARC-DEV-032); an ordinary run stats every pack file once, no frame is read.
        var text = AutomatedVisualRun
            ? $"HD pack source {HdPackSourceResolver.ModeName(_gameSettings.HdPackSource)}: pack folders are not searched in an automated run"
            : ResolveHdPack(_gameSettings.HdPackSource, _gameSettings.HdPackPath).Notice();
        if (!AutomatedVisualRun && !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ARC_HD_PACK"))) text += " (this run's ARC_HD_PACK overrides it)";
        return text;
    }

    /// <summary>The settings a session takes from the player rather than from a save: the difficulty (its skill and experience readers,
    /// ARC-WORLD-030) and the rule mode (ARC-UI-014). Neither is saved with it.</summary>
    private void ApplySessionSettings(GameSessionOwner session)
    {
        if (session.SelectDifficulty(_gameSettings.SessionDifficulty) == GameSessionDisposition.Rejected)
            GD.Print($"ARC-UI-013 difficulty {_gameSettings.SessionDifficulty} refused by the session");
        var rules = session.SelectRules(_ruleMode);
        if (rules == GameSessionDisposition.Rejected) GD.Print($"ARC-UI-014 rules {_ruleMode} refused by the session");
        else if (rules == GameSessionDisposition.Changed) GD.Print($"ARC-UI-014 rules {session.Rules.Name}");
    }

    private void SaveGameSettings(AreaGameSettings settings)
    {
        if (settings == _gameSettings) return;
        _gameSettings = settings;
        var saved = _gameSettingsStore?.Save(settings) == true;
        _gameSettingsOrigin = saved ? "saved setting" : _gameSettingsStore?.Persistent == true ? "not saved" : "this run only";
        GD.Print($"ARC-UI-013 game settings difficulty={settings.Difficulty} turnBased={settings.TurnBased} autoAttack={settings.AutoAttack} floats={settings.Floats} floatSpeed={settings.FloatSpeed} hdInterface={settings.HdInterface} generatedInterface={settings.GeneratedInterface} hdPack={HdPackSourceResolver.ModeName(settings.HdPackSource)} hdPackPath={(settings.HdPackPath is null ? "none" : "set")} saved={saved}");
    }
}

/// <summary>The settings of <see cref="AreaGameSettings"/> (ARC-UI-013), following <see cref="AudioVolumesStore"/>:
/// <c>user://game_settings_v1.json</c> holding <see cref="AreaGameSettingsCodec"/> text; a missing, unreadable or invalid file reads
/// as the original's defaults. <c>GAME_SETTINGS_TEST_PATH</c> isolates tests; an automated run without it has no file at all.</summary>
public sealed class GameSettingsStore
{
    public const string TestVariable = "GAME_SETTINGS_TEST_PATH";
    public const string UserPath = "user://game_settings_v1.json";
    private readonly string? _path;

    public GameSettingsStore(bool automated)
    {
        var test = OS.GetEnvironment(TestVariable);
        _path = !string.IsNullOrWhiteSpace(test) ? test : automated ? null : UserPath;
    }

    public bool Persistent => _path is not null;
    public bool HasSaved => _path is not null && global::Godot.FileAccess.FileExists(_path);

    public AreaGameSettings Load()
    {
        if (_path is null || !global::Godot.FileAccess.FileExists(_path)) return AreaGameSettings.Defaults;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? AreaGameSettings.Defaults : AreaGameSettingsCodec.Deserialize(file.GetAsText()) ?? AreaGameSettings.Defaults;
    }

    public bool Save(AreaGameSettings settings)
    {
        if (_path is null) return false;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) return false;
        file.StoreString(AreaGameSettingsCodec.Serialize(settings));
        file.Flush();
        return file.GetError() == Error.Ok;
    }
}
