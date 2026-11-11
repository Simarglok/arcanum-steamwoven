using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        // ARC-DEV-069: Play prepares the game data off the main thread before it starts; a synthetic folder holds no archives to build from, so
        // the preparation is a stand-in (the installation has its own prototype files) except in its own section below.
        _launcherProvisionForSelfTest = (_, _, _) => new(PrototypeProvisionStatus.Present);
        PrototypeCacheMount.Use(null);
        async Task Play() { _prepareTask = null; _launcherPanel!.PlayForSelfTest(); if (_prepareTask is { } task) await task; }

        // First run: no file, no candidate: the screen, without a folder, Play off.
        await Start(false);
        Require(_launcher is { Reason: LauncherShowReason.FirstRun } && _launcherPanel!.IsOpen && !_launcherPanel.PlayEnabledForSelfTest && started is null,
            "the first run shows the launcher without a folder: " + string.Join(" | ", Lines()));
        Require(Lines()[2] == "Game folder: none found. Choose the folder of the installed game." && Lines()[3] == "Rules: [Steamwoven] Original",
            "folder and rules lines: " + string.Join(" | ", Lines()));
        _launcherPanel!.ChooseForSelfTest(empty);
        Require(!_launcherPanel.PlayEnabledForSelfTest && Lines()[5].StartsWith("Not a game folder the game can open: no tig.dat here", StringComparison.Ordinal),
            "a non-game folder is named in words: " + Lines()[5]);
        await Play();
        Require(started is null, "Play does nothing without a ready folder");
        _launcherPanel.ChooseForSelfTest(game);
        _launcherPanel.RulesForSelfTest(RuleMode.Original);
        _launcherPanel.ShowAtStartForSelfTest(true);
        // ARC-DEV-050: the debug mode is off until ticked; Play stores it and turns the run's debug mode on.
        Require(Lines()[6] == "Debug mode: off", "the debug mode is off by default: " + Lines()[6]);
        _launcherPanel.DebugModeForSelfTest(true);
        var debugBefore = DebugMode;
        Require(_launcherPanel.PlayEnabledForSelfTest && Lines()[3] == "Rules: Steamwoven [Original]" && Lines()[4] == "Show at every start: on"
                && Lines()[6] == "Debug mode: on", "a game folder, Original, show at start and debug mode: " + string.Join(" | ", Lines()));
        await Play();
        Require(DebugMode && Stored().DebugMode, "Play stored the debug mode and turned it on");
        DebugMode = debugBefore;
        Require(started?.GameRoot is { } root && System.IO.Path.GetFullPath(root) == System.IO.Path.GetFullPath(game) && !_launcherPanel.IsOpen,
            "Play started the chosen folder's map mode: " + started?.GameRoot);
        var stored = Stored();
        Require(stored is { Rules: RuleMode.Original, ShowAtStart: true, GameIdentity: not null } && stored.GameRoot == started!.GameRoot,
            "Play stored the folder, its identity, the rules and show at start");

        // The next start: the stored folder with "show at start" shows the screen again; Play opens it.
        await Start(false);
        Require(_launcher is { Reason: LauncherShowReason.ShowAtStart, CanPlay: true } && Lines()[3] == "Rules: Steamwoven [Original]", "show at start: " + string.Join(" | ", Lines()));
        _launcherPanel.ShowAtStartForSelfTest(false);
        await Play();
        Require(started is not null && !Stored().ShowAtStart, "Play with show at start off");
        await Start(false);
        Require(started is not null && _launcher is null && !_launcherPanel.IsOpen, "a stored unchanged folder goes straight to the start");
        await Start(true);
        Require(_launcher is { Reason: LauncherShowReason.Requested } && started is null, "--launcher or Shift opens it on request");

        // The options window's Rules line writes the same file.
        _ruleMode = RuleMode.Steamwoven;
        SaveLauncherRules();
        Require(Stored() is { Rules: RuleMode.Steamwoven, GameRoot: not null }, "the Rules line stored Steamwoven and kept the folder");

        // ARC-DEV-074: install from the GOG installer over a stand-in for innoextract (the real one runs in the opt-in Session test).
        await Start(true);
        var installer = System.IO.Path.Combine(directory, "setup_synthetic.exe");
        System.IO.File.WriteAllBytes(installer, [1, 2, 3]);
        var unpacked = System.IO.Path.Combine(directory, "unpacked");
        _launcherUnpackerForSelfTest = () => null;
        _launcherPanel!.InstallRequestForSelfTest();
        Require(Lines()[5].Contains("innoextract", StringComparison.Ordinal) && Lines()[7] == "Installer: idle, install on",
            "without the tool the screen says how to get it: " + Lines()[5]);
        byte[] exe = [1, 2, 3, 4, 5];
        var blockExtract = false;
        GogToolRunner stand = async (tool, arguments, onLine, cancel) =>
        {
            List<string> lines = [];
            void Emit(string line) { lines.Add(line); onLine(line); }
            (string Name, byte[] Bytes)[] files = [("tig.dat", [1, 2, 3]), ("arcanum1.dat", [1]), ("arcanum2.dat", [1]), ("modules/Arcanum.dat", [1]), ("Arcanum.exe", exe)];
            switch (arguments[0])
            {
                case "--gog-game-id": Emit("GOG.com game ID is " + GameFolderLocator.GogProductId); break;
                case "--list": foreach (var (name, bytes) in files) Emit($"{bytes.Length} {name}"); break;
                case "--test": foreach (var (name, _) in files) Emit($" - \"{name}\""); break;
                default:
                    if (blockExtract) { try { await Task.Delay(System.Threading.Timeout.Infinite, cancel); } catch (OperationCanceledException) { } return new(-1, lines); }
                    var target = arguments[arguments.ToList().IndexOf("--output-dir") + 1];
                    foreach (var (name, bytes) in files)
                    {
                        var path = System.IO.Path.Combine(target, name);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                        System.IO.File.WriteAllBytes(path, bytes);
                        Emit($" - \"{name}\"");
                    }
                    break;
            }
            return new(0, lines);
        };
        var options = new GogInstallOptions { ExpectedExeSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exe)).ToLowerInvariant(), FreeSpace = _ => long.MaxValue };
        _launcherUnpackerForSelfTest = () => new GogInstallerUnpacker("/stub/innoextract", stand, options);
        async Task Idle()
        {
            for (var frames = 0; _launcher!.Busy; frames++)
            {
                Require(frames < 1200, "the unpack did not end: " + string.Join(" | ", Lines()));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        var folderBefore = _launcher!.Folder?.Path;
        _launcherPanel.InstallForSelfTest(installer, unpacked);
        Require(_launcher is { Busy: true, CanPlay: false } && Lines()[7] == "Installer: unpacking, install off, cancel shown" && !_launcherPanel.PlayEnabledForSelfTest,
            "while unpacking Play and the install button are off: " + string.Join(" | ", Lines()));
        _launcherPanel.ChooseForSelfTest(game);
        await Play();
        Require(started is null && _launcher!.Folder?.Path == folderBefore, "no folder is chosen and Play does nothing while unpacking");
        await Idle();
        Require(_launcher is { Busy: false, CanPlay: true } && _launcher.Folder?.Path == unpacked
                && Lines()[5].StartsWith("Unpacked the GOG installer into", StringComparison.Ordinal) && Lines()[7] == "Installer: idle, install on",
            "the unpacked folder is chosen: " + string.Join(" | ", Lines()));
        await Play();
        Require(started?.GameRoot is { } unpackedRoot && System.IO.Path.GetFullPath(unpackedRoot) == unpacked && Stored().GameRoot == started.GameRoot,
            "Play opens and stores the unpacked folder: " + started?.GameRoot);
        // A folder with files in it is refused in words and nothing changes; a cancel stops the tool.
        await Start(true);
        _launcherPanel.InstallForSelfTest(installer, unpacked);
        await Idle();
        Require(Lines()[5].StartsWith("Choose a new or empty folder", StringComparison.Ordinal), "a used folder is refused: " + Lines()[5]);
        blockExtract = true;
        var cancelled = System.IO.Path.Combine(directory, "cancelled");
        _launcherPanel.InstallForSelfTest(installer, cancelled);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _launcherPanel.CancelInstallForSelfTest();
        await Idle();
        Require(Lines()[5].StartsWith("Unpacking was cancelled.", StringComparison.Ordinal) && _launcher!.Folder?.Path != cancelled, "a cancel is named: " + Lines()[5]);
        _launcherUnpackerForSelfTest = null;

        // ARC-DEV-069: an installation without prototype files is prepared on the first Play, off the main thread, with its progress on the screen.
        await Start(true);
        _launcherPanel!.ChooseForSelfTest(game);
        var cache = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "cache-stand-in")).FullName;
        var release = new ManualResetEventSlim();
        _launcherProvisionForSelfTest = (_, progress, cancel) =>
        {
            progress(new(PrototypeProvisionStage.Building));
            progress(new(PrototypeProvisionStage.Building, 312, PrototypeProvisioner.InstalledFileCount));
            try { release.Wait(cancel); }
            catch (OperationCanceledException) { return new(PrototypeProvisionStatus.Cancelled); }
            return new(PrototypeProvisionStatus.Built, cache, Files: PrototypeProvisioner.InstalledFileCount);
        };
        started = null; _prepareTask = null;
        _launcherPanel.PlayForSelfTest();
        for (var frames = 0; !(_launcher?.Notice?.Contains("312/1423", StringComparison.Ordinal) ?? false); frames++)
        {
            Require(frames < 1200, "the preparation's progress did not arrive: " + string.Join(" | ", Lines()));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Require(_launcher is { Busy: true, Preparing: true, CanPlay: false } && !_launcherPanel.PlayEnabledForSelfTest && started is null && _launcherPanel.IsOpen
                && Lines()[5].StartsWith("Preparing the game data from your Arcanum.exe (first start only", StringComparison.Ordinal)
                && Lines()[7] == "Installer: unpacking, install off, cancel shown" && _launcherPanel.CancelTextForSelfTest == "Cancel",
            "while the game data is prepared the screen is busy with the progress and a Cancel: " + string.Join(" | ", Lines()));
        Require(!Lines()[5].Contains("run Arcanum.exe", StringComparison.OrdinalIgnoreCase) && !Lines()[5].Contains("start Arcanum.exe", StringComparison.OrdinalIgnoreCase),
            "the screen never tells the player to run the original");
        _launcherPanel.CancelInstallForSelfTest();
        await _prepareTask!;
        Require(_launcher is { Busy: false, CanPlay: true } && Lines()[5].StartsWith("Preparing the game data was cancelled", StringComparison.Ordinal) && started is null
                && PrototypeCacheMount.Folder is null, "a cancel is named, the screen stays open and nothing is mounted: " + Lines()[5]);
        _launcherProvisionForSelfTest = (_, _, _) => new(PrototypeProvisionStatus.UnverifiedExecutable);
        await Play();
        Require(_launcher is { Busy: false, CanPlay: true } && started is null && Lines()[5].Contains("not the retail 1.0.7.4 one", StringComparison.Ordinal)
                && !Lines()[5].Contains("run Arcanum.exe", StringComparison.OrdinalIgnoreCase), "an executable that cannot be built from is named: " + Lines()[5]);
        _launcherProvisionForSelfTest = (_, progress, cancel) =>
        {
            progress(new(PrototypeProvisionStage.Storing, 1423, 1423));
            return new(PrototypeProvisionStatus.Cached, cache, Files: PrototypeProvisioner.InstalledFileCount);
        };
        await Play();
        Require(started?.GameRoot is { } preparedRoot && System.IO.Path.GetFullPath(preparedRoot) == System.IO.Path.GetFullPath(game) && !_launcherPanel.IsOpen
                && PrototypeCacheMount.Folder == cache, "a prepared folder starts the game with its cache mounted: " + started?.GameRoot);
        // A stored folder goes straight on through the same preparation; a failure opens the screen with the reason.
        _launcherProvisionForSelfTest = (_, _, _) => new(PrototypeProvisionStatus.NoExecutable);
        await Start(false);
        Require(_launcher is { Reason: LauncherShowReason.Requested, Busy: false } && started is null && Lines()[5].Contains("which was not found", StringComparison.Ordinal),
            "a stored folder that cannot be prepared opens the screen: " + string.Join(" | ", Lines()));
        _launcherProvisionForSelfTest = (_, _, _) => new(PrototypeProvisionStatus.Present);
        await Play();
        Require(started is not null && PrototypeCacheMount.Folder is null, "an installation with its own files mounts no cache");
        _launcherProvisionForSelfTest = null;

        _launcherProvisionForSelfTest = (_, _, _) => new(PrototypeProvisionStatus.Present);
        // Reset replaces even a newer build's file with the defaults.
        System.IO.File.WriteAllText(settingsPath, """{"schema":2,"gameRoot":"/x"}""");
        await Start(true);
        _launcherPanel.ChooseForSelfTest(game);
        await Play();
        Require(started is not null && System.IO.File.ReadAllText(settingsPath) == """{"schema":2,"gameRoot":"/x"}""", "Play never overwrites a newer build's file");
        await Start(true);
        Require(_launcher!.FileStatus == LauncherSettingsReadStatus.OtherSchema && Lines()[1].Contains("newer build's", StringComparison.Ordinal), "the newer file is named");
        _launcherPanel.ResetForSelfTest();
        Require(LauncherSettingsFile.Load(settingsPath) is { Status: LauncherSettingsReadStatus.Read } reset && reset.Settings == LauncherSettings.Defaults
                && Lines()[5] == "Settings reset to the defaults.", "Reset wrote the defaults over it: " + Lines()[5]);

        // ARC-UI-143: the HD lines of the options window on the launcher: defaults off/off/Auto, each choice saved at once in the same files the
        // options window and the start read, shown again by the next launcher, Custom's folder, and Reset leaving them alone.
        var hdFolder = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "hd")).FullName;
        var gameSettingsPath = System.IO.Path.Combine(hdFolder, "game_settings_v1.json");
        var visualPath = System.IO.Path.Combine(hdFolder, "visual_profile_v1.json");
        OS.SetEnvironment(GameSettingsStore.TestVariable, gameSettingsPath);
        OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, visualPath);
        (_gameSettingsStore, _visualStore, _gameSettings, _launcherHdProfile) = (null, null, AreaGameSettings.Defaults, VisualProfile.Legacy);
        try
        {
            string[] HdLines() => [.. Lines()[8..11]];
            await Start(true);
            Require(HdLines().SequenceEqual(["HD graphics: Off", "HD interface: Off", "HD pack: Auto"]) && _launcherPanel.HdEnabledForSelfTest, "the HD lines default to Off, Off, Auto: " + string.Join(" | ", HdLines()));
            _launcherPanel.HdGraphicsForSelfTest(true);
            _launcherPanel.HdInterfaceForSelfTest(1);
            _launcherPanel.HdPackForSelfTest(HdPackSourceMode.GameFolder);
            Require(HdLines().SequenceEqual(["HD graphics: On", "HD interface: On", "HD pack: Game"]), "the choices show: " + string.Join(" | ", HdLines()));
            Require(new VisualProfileSettingsStore(false).Load() == VisualProfile.Hd
                    && new GameSettingsStore(false).Load() is { HdInterface: true, GeneratedInterface: false, HdPackSource: HdPackSourceMode.GameFolder }, "each choice was saved at once in the options window's files");
            _launcherPanel.HdInterfaceForSelfTest(2);
            Require(Lines()[9] == "HD interface: Generated (experimental)" && new GameSettingsStore(false).Load() is { HdInterface: true, GeneratedInterface: true }, "the generated interface is the third choice");
            // The next launcher shows what was saved, and the start's own reading finds the same values.
            (_gameSettings, _launcherHdProfile) = (AreaGameSettings.Defaults, VisualProfile.Legacy);
            await Start(true);
            Require(HdLines().SequenceEqual(["HD graphics: On", "HD interface: Generated (experimental)", "HD pack: Game"]), "the next launcher shows the saved choices: " + string.Join(" | ", HdLines()));
            ConfigureGameSettings();
            Require(_gameSettings is { HdInterface: true, GeneratedInterface: true, HdPackSource: HdPackSourceMode.GameFolder } && new VisualProfileSettingsStore(false).Load() == VisualProfile.Hd, "the start reads the launcher's choices");
            // Custom asks for a folder: with no dialog (an automated run) the note names the setting; the chosen folder is saved with the source.
            _launcherPanel.HdPackForSelfTest(HdPackSourceMode.Custom);
            Require(Lines()[11].Contains("no folder dialog here", StringComparison.Ordinal), "Custom without a dialog names the setting: " + Lines()[11]);
            LauncherHdPackFolderForSelfTest(hdFolder);
            Require(new GameSettingsStore(false).Load() is { HdPackSource: HdPackSourceMode.Custom, HdPackPath: { } custom } && custom == hdFolder && Lines()[10] == "HD pack: Custom", "the chosen folder is saved with Custom");
            LauncherHdPackFolderForSelfTest(System.IO.Path.Combine(hdFolder, "missing"));
            Require(Lines()[11].EndsWith("| that folder cannot be used", StringComparison.Ordinal) && new GameSettingsStore(false).Load().HdPackPath == hdFolder, "a missing folder is refused and the saved one stays");
            _launcherPanel.HdGraphicsForSelfTest(false);
            Require(new VisualProfileSettingsStore(false).Load() == VisualProfile.Legacy && Lines()[8] == "HD graphics: Off", "HD graphics Off is saved");
            // Reset settings leaves the HD lines alone.
            var before = HdLines();
            _launcherPanel.ResetForSelfTest();
            Require(HdLines().SequenceEqual(before) && new GameSettingsStore(false).Load().HdPackSource == HdPackSourceMode.Custom, "Reset leaves the HD lines alone");
        }
        finally
        {
            OS.UnsetEnvironment(GameSettingsStore.TestVariable); OS.UnsetEnvironment(VisualProfileSettingsStore.TestVariable);
            (_gameSettingsStore, _visualStore, _gameSettings, _launcherHdProfile) = (null, null, AreaGameSettings.Defaults, VisualProfile.Legacy);
        }
        _launcher = null; _launcherPanel.CloseView();
        _launcherProvisionForSelfTest = null;
        PrototypeCacheMount.Use(null);
        return "first run without a folder, a non-game folder in words, Play storing folder/identity/Original/show at start, show at start "
               + "reopening it, a stored folder going straight on, --launcher, the Rules line writing the file, the GOG installer unpacked by a stand-in "
               + "tool (no tool, progress lock, chosen folder, used folder refused, cancel), the game data prepared on the first Play (progress, cancel, refusal, "
               + "cached, a stored folder), Reset over a newer build's file, the HD lines (saved at once in the options window's files, shown again, Custom's folder, Reset leaves them)";
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
            // ARC-UI-143: the HD lines are on the screen (their saved values are not touched in an automated run).
            var hdLines = panel.LinesForSelfTest.Skip(8).Take(3).ToArray();
            Require(hdLines.Length == 3 && hdLines[0].StartsWith("HD graphics: ", StringComparison.Ordinal) && hdLines[1].StartsWith("HD interface: ", StringComparison.Ordinal)
                    && hdLines[2].StartsWith("HD pack: ", StringComparison.Ordinal), "the HD lines: " + string.Join(" | ", hdLines));
            // The screenshot of the launcher itself, when the run asks for one (the end of the smoke has its own).
            if (_nativeSmokeScreenshot is { } shot)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var full = System.IO.Path.GetFullPath(System.IO.Path.ChangeExtension(shot, null) + "-launcher.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image is not null && image.SavePng(full) == Error.Ok, "the launcher screenshot was not written");
            }
            _prepareTask = null;
            panel.PlayForSelfTest();
            if (_prepareTask is { } preparing) await preparing;
            Require(!panel.IsOpen && _ruleMode == RuleMode.Original && _arguments?.GameRoot is not null, "Play started the main map under Original");
            var path = LauncherSettingsPath();
            if (path is not null)
                Require(LauncherSettingsFile.Load(path).Settings is { Rules: RuleMode.Original, ShowAtStart: false } stored && stored.GameRoot == _launcherSettings.GameRoot,
                    "Play stored the folder and Original");
            GD.Print("ARC-UI-014 native-smoke launcher PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Lines = lines[..3], Hd = hdLines, Rules = RuleModeNames.Name(_ruleMode), Stored = path is not null }));
        }
        catch (InvalidOperationException error)
        {
            GD.PrintErr("ARC-UI-014 native-smoke launcher FAILED: " + error.Message);
            GetTree().Quit(1);
        }
        finally { System.IO.Directory.Delete(empty, recursive: true); }
    }
}
