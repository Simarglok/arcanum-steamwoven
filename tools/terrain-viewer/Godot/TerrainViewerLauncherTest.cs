using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-014 over synthetic folders and a temporary settings file, in the production app with a stand-in for the start:
    /// the first run shows the launcher without a folder (Play off), a non-game folder is named in words, a chosen game folder with
    /// Original and "show at start" is stored by Play; the next start shows it again for "show at start"; the options window's Rules
    /// line writes the file; Reset replaces a newer build's file with the defaults.</summary>
    private static async Task AssertLauncherAsync(Node host)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-014-").FullName;
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try { summary = await app.LauncherForSelfTest(directory); }
        finally
        {
            viewport.Free();
            System.IO.Directory.Delete(directory, recursive: true);
        }
        GD.Print($"ARC-UI-014 self-test PASS synthetic launcher ({summary})");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<string> LauncherForSelfTest(string directory)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("launcher: " + why); }
        var settingsPath = System.IO.Path.Combine(directory, "settings", LauncherSettingsFile.FileName);
        var game = System.IO.Path.Combine(directory, "game");
        foreach (var (name, bytes) in new[] { ("tig.dat", 3), ("arcanum1.dat", 5), ("arcanum2.dat", 7), ("modules/Arcanum.dat", 11) })
        {
            var path = System.IO.Path.Combine(game, name);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[bytes]);
        }
        var empty = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "empty")).FullName;
        TerrainViewerArguments? started = null;
        (_launcherPathResolved, _launcherPath, _launcherStartForSelfTest) = (true, settingsPath, arguments => started = arguments);
        _launcherPlatformForSelfTest = new(GameFolderPlatformKind.MacOS, System.IO.Path.Combine(directory, "home"), System.IO.Path.Combine(directory, "Applications"), null, null, _ => null);
        string[] Lines() => [.. _launcherPanel!.LinesForSelfTest];
        LauncherSettings Stored() => LauncherSettingsFile.Load(settingsPath).Settings;
        async Task Start(bool requested) { _launcherRead = null; started = null; await StartFromGameFolderAsync(requested); }

        // First run: no file, no candidate: the screen, without a folder, Play off.
        await Start(false);
        Require(_launcher is { Reason: LauncherShowReason.FirstRun } && _launcherPanel!.IsOpen && !_launcherPanel.PlayEnabledForSelfTest && started is null,
            "the first run shows the launcher without a folder: " + string.Join(" | ", Lines()));
        Require(Lines()[2] == "Game folder: none found. Choose the folder of the installed game." && Lines()[3] == "Rules: [Steamwoven] Original",
            "folder and rules lines: " + string.Join(" | ", Lines()));
        _launcherPanel!.ChooseForSelfTest(empty);
        Require(!_launcherPanel.PlayEnabledForSelfTest && Lines()[5].StartsWith("Not a game folder the game can open: no tig.dat here", StringComparison.Ordinal),
            "a non-game folder is named in words: " + Lines()[5]);
        _launcherPanel.PlayForSelfTest();
        Require(started is null, "Play does nothing without a ready folder");
        _launcherPanel.ChooseForSelfTest(game);
        _launcherPanel.RulesForSelfTest(RuleMode.Original);
        _launcherPanel.ShowAtStartForSelfTest(true);
        Require(_launcherPanel.PlayEnabledForSelfTest && Lines()[3] == "Rules: Steamwoven [Original]" && Lines()[4] == "Show at every start: on",
            "a game folder, Original and show at start: " + string.Join(" | ", Lines()));
        _launcherPanel.PlayForSelfTest();
        Require(started?.GameRoot is { } root && System.IO.Path.GetFullPath(root) == System.IO.Path.GetFullPath(game) && !_launcherPanel.IsOpen,
            "Play started the chosen folder's map mode: " + started?.GameRoot);
        var stored = Stored();
        Require(stored is { Rules: RuleMode.Original, ShowAtStart: true, GameIdentity: not null } && stored.GameRoot == started!.GameRoot,
            "Play stored the folder, its identity, the rules and show at start");

        // The next start: the stored folder with "show at start" shows the screen again; Play opens it.
        await Start(false);
        Require(_launcher is { Reason: LauncherShowReason.ShowAtStart, CanPlay: true } && Lines()[3] == "Rules: Steamwoven [Original]", "show at start: " + string.Join(" | ", Lines()));
        _launcherPanel.ShowAtStartForSelfTest(false);
        _launcherPanel.PlayForSelfTest();
        Require(started is not null && !Stored().ShowAtStart, "Play with show at start off");
        await Start(false);
        Require(started is not null && _launcher is null && !_launcherPanel.IsOpen, "a stored unchanged folder goes straight to the start");
        await Start(true);
        Require(_launcher is { Reason: LauncherShowReason.Requested } && started is null, "--launcher or Shift opens it on request");

        // The options window's Rules line writes the same file.
        _ruleMode = RuleMode.Steamwoven;
        SaveLauncherRules();
        Require(Stored() is { Rules: RuleMode.Steamwoven, GameRoot: not null }, "the Rules line stored Steamwoven and kept the folder");

        // Reset replaces even a newer build's file with the defaults.
        System.IO.File.WriteAllText(settingsPath, """{"schema":2,"gameRoot":"/x"}""");
        await Start(true);
        _launcherPanel.ChooseForSelfTest(game);
        _launcherPanel.PlayForSelfTest();
        Require(started is not null && System.IO.File.ReadAllText(settingsPath) == """{"schema":2,"gameRoot":"/x"}""", "Play never overwrites a newer build's file");
        await Start(true);
        Require(_launcher!.FileStatus == LauncherSettingsReadStatus.OtherSchema && Lines()[1].Contains("newer build's", StringComparison.Ordinal), "the newer file is named");
        _launcherPanel.ResetForSelfTest();
        Require(LauncherSettingsFile.Load(settingsPath) is { Status: LauncherSettingsReadStatus.Read } reset && reset.Settings == LauncherSettings.Defaults
                && Lines()[5] == "Settings reset to the defaults.", "Reset wrote the defaults over it: " + Lines()[5]);
        _launcher = null; _launcherPanel.CloseView();
        return "first run without a folder, a non-game folder in words, Play storing folder/identity/Original/show at start, show at start "
               + "reopening it, a stored folder going straight on, --launcher, the Rules line writing the file, Reset over a newer build's file";
    }

    /// <summary>ARC-UI-014 native smoke: the launcher a start without inputs opened (run with <c>--launcher</c>, or a first run), over the
    /// player's installation: a folder found, an empty folder refused in words, the folder chosen again, Original and "show at start"
    /// toggled, Play storing them (with <c>LAUNCHER_SETTINGS_TEST_PATH</c>) and starting the main map under Original; the map smoke
    /// stages then run.</summary>
    private async Task RunLauncherSmokeAsync()
    {
        var empty = System.IO.Directory.CreateTempSubdirectory("arc-ui-014-smoke-").FullName;
        try
        {
            static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var panel = _launcherPanel!;
            Require(_launcher is { CanPlay: true, Folder: not null } && panel.IsOpen && panel.PlayEnabledForSelfTest,
                "the launcher found a game folder: " + string.Join(" | ", panel.LinesForSelfTest));
            var folder = _launcher!.Folder!.Path;
            panel.ChooseForSelfTest(empty);
            Require(!panel.PlayEnabledForSelfTest && panel.LinesForSelfTest[5].StartsWith("Not a game folder", StringComparison.Ordinal), "an empty folder is refused");
            panel.ChooseForSelfTest(folder);
            panel.RulesForSelfTest(RuleMode.Original);
            panel.ShowAtStartForSelfTest(true);
            panel.ShowAtStartForSelfTest(false);
            var lines = panel.LinesForSelfTest.ToArray();
            Require(panel.PlayEnabledForSelfTest && lines[3] == "Rules: Steamwoven [Original]" && lines[4] == "Show at every start: off", "choices: " + string.Join(" | ", lines));
            panel.PlayForSelfTest();
            Require(!panel.IsOpen && _ruleMode == RuleMode.Original && _arguments?.GameRoot is not null, "Play started the main map under Original");
            var path = LauncherSettingsPath();
            if (path is not null)
                Require(LauncherSettingsFile.Load(path).Settings is { Rules: RuleMode.Original, ShowAtStart: false } stored && stored.GameRoot == _launcherSettings.GameRoot,
                    "Play stored the folder and Original");
            GD.Print("ARC-UI-014 native-smoke launcher PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Lines = lines[..3], Rules = RuleModeNames.Name(_ruleMode), Stored = path is not null }));
        }
        catch (InvalidOperationException error)
        {
            GD.PrintErr("ARC-UI-014 native-smoke launcher FAILED: " + error.Message);
            GetTree().Quit(1);
        }
        finally { System.IO.Directory.Delete(empty, recursive: true); }
    }
}
