using Arcanum.Presentation.Steamwoven;
using Arcanum.World;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-143, ARC-UI-2174: the HD lines (HD Off/On, HD pack, generated interface) on the launcher screen, before the game starts (ARC-UI-146 removed them from the options
// window). HD is the one saved value (the hd line of arcanum.cfg, ARC-UI-1130; the game settings group): On means the HD interface art and fonts and the relit lighting
// together, Off is Legacy; a change is saved at once, and the game that Play starts reads it like any start (the pack source from the next start). HD pack only says where
// the interface pack is read from; the generated interface is experimental and honoured only while HD is On.
public partial class TerrainViewerApp
{
    // What the next start reads for the pack (and the environment overrides of this run), and why a choice was not saved; the screen shows both.
    private string _launcherHdPackNote = "";
    private string _launcherHdProblem = "";

    /// <summary>Reads the saved settings (a run with no file keeps what the screen has) and shows them with the note on the pack.</summary>
    private void LauncherHdLoad()
    {
        _gameSettingsStore ??= new GameSettingsStore(AutomatedVisualRun);
        _visualStore ??= new VisualProfileSettingsStore(AutomatedVisualRun);
        if (_gameSettingsStore.Persistent) _gameSettings = _gameSettingsStore.Load();
        _launcherHdPackNote = HdPackSourceText();
        if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ARC_VISUAL_PROFILE")) && !AutomatedVisualRun)
            _launcherHdPackNote += " (this run's ARC_VISUAL_PROFILE overrides HD)";
        _launcherHdProblem = "";
        LauncherHdShow();
    }

    private void LauncherHdShow()
    {
        if (_launcherPanel is null) return;
        _launcherPanel.Hd = new LauncherHdLines(_gameSettings.HdInterface, _gameSettings.GeneratedInterface && _gameSettings.HdInterface, _gameSettings.HdPackSource,
            _launcherHdProblem.Length == 0 ? _launcherHdPackNote : _launcherHdPackNote + " | " + _launcherHdProblem, _gameSettings.InterfaceLayout);
    }

    /// <summary>ARC-UI-2890: the layout of the generated interface (column, column thin, full width), saved at once; this run's ARC_STEAMWOVEN_LAYOUT still overrides it.</summary>
    private void LauncherHdLayout(SteamwovenLayoutChoice choice)
    {
        if (!Enum.IsDefined(choice)) return;
        SaveGameSettings(_gameSettings with { InterfaceLayout = choice });
        GD.Print($"ARC-UI-2890 launcher interface layout={SteamwovenLayoutChoices.Name(choice)}");
        LauncherHdProblem(GameSettingsProblem());
    }

    private void LauncherHd(bool on)
    {
        // The generated interface is honoured only while HD is On, so Off clears it (the settings group never holds it without HD).
        SaveGameSettings(_gameSettings with { HdInterface = on, GeneratedInterface = _gameSettings.GeneratedInterface && on });
        GD.Print($"ARC-UI-2174 launcher HD={(on ? "On" : "Off")}");
        LauncherHdProblem(GameSettingsProblem());
    }

    private void LauncherHdGenerated(bool on)
    {
        if (on && !_gameSettings.HdInterface) return; // the row is off while HD is Off
        SaveGameSettings(_gameSettings with { GeneratedInterface = on });
        GD.Print($"ARC-UI-2174 launcher generated interface={(on ? "On" : "Off")}");
        LauncherHdProblem(GameSettingsProblem());
    }

    private void LauncherHdPack(HdPackSourceMode mode)
    {
        if (!Enum.IsDefined(mode)) return;
        SaveGameSettings(_gameSettings with { HdPackSource = mode });
        _launcherHdPackNote = HdPackSourceText();
        GD.Print($"ARC-UI-143 launcher HD pack={HdPackSourceResolver.ModeName(mode)}; " + _launcherHdPackNote);
        LauncherHdProblem(GameSettingsProblem());
        if (mode == HdPackSourceMode.Custom) LauncherAskHdPackFolder();
    }

    private void LauncherHdProblem(string problem)
    {
        _launcherHdProblem = problem;
        LauncherHdShow();
    }

    /// <summary>Why the last game-settings save did not reach the file (a persistent run only), else empty.</summary>
    private string GameSettingsProblem() => _gameSettingsStore is { Persistent: true } && _gameSettingsOrigin == "not saved" ? "the HD settings could not be saved" : "";

    private void LauncherHdPackNoteAppend(string text)
    {
        _launcherHdPackNote += " | " + text;
        LauncherHdShow();
    }

    /// <summary>The system folder dialog for the Custom source (a pack folder, or a folder of packs); where there is none the note names the setting.</summary>
    private void LauncherAskHdPackFolder()
    {
        if (AutomatedVisualRun || DisplayServer.GetName() == "headless" || !DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile))
        {
            LauncherHdPackNoteAppend("no folder dialog here: the folder is the hdPackPath of the game settings file");
            return;
        }
        var start = _gameSettings.HdPackPath is { } saved && System.IO.Directory.Exists(saved) ? saved
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var failure = DisplayServer.FileDialogShow("HD pack: a pack folder or a folder of packs", start, "", false, DisplayServer.FileDialogMode.OpenDir, [],
            Callable.From<bool, string[], int>((ok, paths, _) => CallDeferred(MethodName.LauncherHdPackFolderChosen, ok && paths.Length > 0 ? paths[0] : "")));
        if (failure != Error.Ok) LauncherHdProblem("the folder dialog did not open (" + failure + ")");
    }

    private void LauncherHdPackFolderChosen(string folder)
    {
        // Cancelled, the launcher closed meanwhile, or Play is under way (the rows are off then): the setting stays as it is.
        if (folder.Length == 0 || _launcher is not { Busy: false }) return;
        if (folder.Length > AreaGameSettingsCodec.MaxPath || !Arcanum.Session.Settings.ArcanumConfig.ValidValue(folder) || !System.IO.Directory.Exists(folder)) { LauncherHdProblem("that folder cannot be used"); return; }
        SaveGameSettings(_gameSettings with { HdPackSource = HdPackSourceMode.Custom, HdPackPath = folder });
        _launcherHdPackNote = HdPackSourceText();
        LauncherHdProblem(GameSettingsProblem());
    }

    /// <summary>The self-test's way to give the folder the dialog would return.</summary>
    internal void LauncherHdPackFolderForSelfTest(string folder) => LauncherHdPackFolderChosen(folder);
}
