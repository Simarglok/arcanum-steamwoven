using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-014: the launcher screen's rules (when it shows, the folder, rules, show-at-start, Play and Reset) on synthetic folders.</summary>
[TestClass]
public sealed class LauncherScreenTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-launcher-screen-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static string Game(string folder, int terrainBytes = 7)
    {
        foreach (var (name, bytes) in new[] { ("tig.dat", 3), ("arcanum1.dat", 5), ("arcanum2.dat", terrainBytes), ("modules/Arcanum.dat", 11) })
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }
        return folder;
    }

    private GameFolderPlatform Mac() => new(GameFolderPlatformKind.MacOS, Path.Combine(_root, "home"), Path.Combine(_root, "Applications"), null, null, _ => null);

    private static LauncherSettingsRead Read(LauncherSettings settings) => LauncherSettingsCodec.Deserialize(LauncherSettingsCodec.Serialize(settings));

    private LauncherShowReason Decide(LauncherSettings settings, bool requested = false)
    {
        var startup = GameFolderStartup.Resolve(settings, Mac());
        var reason = LauncherScreen.Decide(settings, startup, requested);
        if (!requested) Assert.AreEqual(startup.ShowLauncher, reason != LauncherShowReason.None, "the reason agrees with the start's signal: " + reason);
        return reason;
    }

    [TestMethod]
    public void The_screen_shows_on_the_first_run_a_lost_or_changed_folder_show_at_start_and_on_request()
    {
        Assert.AreEqual(LauncherShowReason.FirstRun, Decide(LauncherSettings.Defaults), "nothing stored, nothing detected");
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        Assert.AreEqual(LauncherShowReason.FirstRun, Decide(LauncherSettings.Defaults), "a detected folder is confirmed on the screen");
        var stored = LauncherSettings.Defaults.WithGameFolder(GameFolderLocator.Check(detected));
        Assert.AreEqual(LauncherShowReason.None, Decide(stored), "the stored folder, unchanged: straight to the main menu");
        Assert.AreEqual(LauncherShowReason.Requested, Decide(stored, requested: true));
        Assert.AreEqual(LauncherShowReason.ShowAtStart, Decide(stored with { ShowAtStart = true }));
        Assert.AreEqual(LauncherShowReason.FolderGone, Decide(stored with { GameRoot = Path.Combine(_root, "moved") }));
        Game(detected, terrainBytes: 9);
        Assert.AreEqual(LauncherShowReason.InstallationChanged, Decide(stored));
        Assert.AreEqual(LauncherShowReason.InstallationChanged, Decide(stored with { ShowAtStart = true }), "the change is named first");
    }

    [TestMethod]
    public void Play_confirms_the_detected_folder_with_the_rules_and_show_at_start()
    {
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        var startup = GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac());
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), startup, LauncherShowReason.FirstRun);
        Assert.IsTrue(screen.CanPlay);
        Assert.AreEqual(detected, screen.Folder!.Path);
        CollectionAssert.AreEqual(new[] { detected }, screen.Candidates.Select(c => c.Path).ToArray(), "ready candidates only");
        StringAssert.StartsWith(screen.FolderText, $"Game folder: {detected} (ready: installation, not the verified 1.0.7.4 executable");
        Assert.AreEqual("First start: confirm the game folder and the rules.", screen.ReasonText);
        screen.SelectRules(RuleMode.Original);
        screen.SetShowAtStart(true);
        Assert.IsFalse(screen.Settings.DebugMode, "ARC-DEV-050: the debug mode is off by default");
        screen.SetDebugMode(true);
        Assert.IsTrue(screen.Settings.HoldToQuit, "ARC-DEV-2270: holding the quit key quits at once by default");
        screen.SetHoldToQuit(false);
        Assert.IsFalse(screen.Play()!.HoldToQuit, "Play stores the hold-to-quit choice");
        Assert.IsFalse(screen.Settings.SeparateRenderThread, "ARC-RENDER-3420: the separate rendering thread is off by default");
        screen.SetSeparateRenderThread(true);
        Assert.IsTrue(screen.Play()!.SeparateRenderThread, "Play stores the render thread choice");
        Assert.IsFalse(screen.Settings.SkipStartupMovies, "ARC-UI-3453: the start-up movies play by default");
        screen.SetSkipStartupMovies(true);
        Assert.IsTrue(screen.Play()!.SkipStartupMovies, "Play stores the start-up movies choice");
        var played = screen.Play()!;
        Assert.AreEqual((detected, RuleMode.Original, true, GameSource.Other, true), (played.GameRoot, played.Rules, played.ShowAtStart, played.GameSource, played.DebugMode));
        Assert.IsTrue(played.GameIdentity!.SameAs(startup.Folder!.Identity), "Play stores the identity, so a later change is noticed");
        Assert.AreEqual(LauncherSettings.Defaults.Display, played.Display, "the display at start is kept as stored");
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.SelectRules((RuleMode)7));
    }

    [TestMethod]
    public void A_chosen_folder_is_shown_in_words_and_Play_waits_for_a_ready_one()
    {
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        Assert.IsFalse(screen.CanPlay);
        Assert.IsNull(screen.Play());
        Assert.AreEqual("Choose the game folder first.", screen.Notice);
        Assert.AreEqual("Game folder: none found. Choose the folder of the installed game.", screen.FolderText);
        var partial = Path.Combine(_root, "partial");
        Game(partial);
        File.Delete(Path.Combine(partial, "arcanum2.dat"));
        screen.UseFolder(GameFolderLocator.Choose(partial));
        Assert.AreEqual("Not a game folder the game can open: no arcanum2.dat here", screen.Notice);
        Assert.IsNull(screen.Play());
        screen.UseFolder(GameFolderLocator.Choose(Path.Combine(_root, "nowhere")));
        Assert.AreEqual("Not a game folder the game can open: the folder does not exist", screen.Notice);
        var game = Game(Path.Combine(_root, "chosen"));
        screen.UseFolder(GameFolderLocator.Choose(game));
        Assert.IsNull(screen.Notice);
        Assert.AreEqual(game, screen.Play()!.GameRoot);
        Assert.IsFalse(screen.UseCandidate(0), "no candidate was found");
    }

    [TestMethod]
    public void Reset_gives_the_defaults_and_names_a_file_this_build_cannot_use()
    {
        var newer = LauncherSettingsCodec.Deserialize("""{"schema":2}""");
        var screen = new LauncherScreen(newer, GameFolderStartup.Resolve(newer.Settings, Mac()), LauncherShowReason.FirstRun);
        Assert.IsFalse(screen.FileUsable);
        Assert.AreEqual("The settings file is a newer build's; it is kept until Reset settings replaces it.", screen.FileText);
        screen.SelectRules(RuleMode.Original);
        Assert.AreEqual(LauncherSettings.Defaults, screen.Reset());
        screen.ResetDone(written: true);
        Assert.IsTrue(screen.FileUsable);
        Assert.AreEqual(RuleMode.Steamwoven, screen.Settings.Rules);
        Assert.AreEqual("Settings reset to the defaults.", screen.Notice);
        Assert.IsTrue(new LauncherScreen(LauncherSettingsCodec.Deserialize("not json"), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()),
            LauncherShowReason.FirstRun).FileUsable, "a damaged file is replaced by the next save");
    }
    /// <summary>ARC-DEV-074: the installer unpack's state on the screen.</summary>
    [TestMethod]
    public void While_a_GOG_installer_unpacks_the_folder_and_Play_are_off_and_the_result_is_chosen_or_named()
    {
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        Assert.IsTrue(screen.CanPlay);
        Assert.IsTrue(screen.BeginInstall("setup.exe", "target"));
        Assert.IsFalse(screen.BeginInstall("again.exe", "other"), "one unpack at a time");
        Assert.IsTrue(screen.Busy);
        Assert.IsFalse(screen.CanPlay);
        Assert.IsNull(screen.Play());
        Assert.AreEqual("Wait for the unpacking to finish, or cancel it.", screen.Notice);
        var other = Game(Path.Combine(_root, "other"));
        screen.UseFolder(GameFolderLocator.Choose(other));
        Assert.IsFalse(screen.UseCandidate(0));
        Assert.AreEqual(detected, screen.Folder!.Path, "the folder does not change while unpacking");
        screen.InstallProgress(new(GogInstallStage.Unpacking, 3, 74, 5 * 1024 * 1024, 100 * 1024 * 1024, "arcanum1.dat"));
        Assert.AreEqual("Unpacking the game files… 3/74 files, 5/100 MB", screen.Notice);

        var unpacked = Game(Path.Combine(_root, "unpacked"));
        screen.InstallDone(new(GogInstallStatus.Unpacked, null, GameFolderLocator.Check(unpacked)));
        Assert.IsFalse(screen.Busy);
        Assert.IsTrue(screen.CanPlay);
        Assert.AreEqual(unpacked, screen.Folder!.Path, "the unpacked folder is the folder Play opens");
        Assert.AreEqual($"Unpacked the GOG installer into {unpacked}; the folder is chosen. Press Play to start.", screen.Notice);
        Assert.AreEqual(unpacked, screen.Play()!.GameRoot);

        screen.BeginInstall("setup.exe", "target2");
        screen.InstallDone(new(GogInstallStatus.FingerprintMismatch, "Arcanum.exe is not the verified 1.0.7.4 executable"));
        Assert.AreEqual(unpacked, screen.Folder!.Path, "a failed unpack leaves the folder as it was");
        StringAssert.Contains(screen.Notice!, "not the verified 1.0.7.4 executable");
        StringAssert.Contains(screen.Notice!, "Files may be left in target2");
        Assert.IsTrue(screen.CanPlay);
    }

    /// <summary>ARC-DEV-3515: the Steam steps and the copy's state on the screen.</summary>
    [TestMethod]
    public void The_Steam_steps_show_and_while_the_download_is_copied_Play_is_off_and_the_result_is_chosen_or_named()
    {
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        Assert.IsFalse(screen.SteamGuideOpen);
        screen.ToggleSteamGuide();
        Assert.IsTrue(screen.SteamGuideOpen);
        StringAssert.Contains(LauncherScreen.SteamGuideText, "Depot download complete");
        screen.SteamCommandCopied();
        StringAssert.Contains(screen.Notice!, "download_depot 500810 500811");
        screen.SteamConsoleOpened(false);
        StringAssert.Contains(screen.Notice!, "steam://open/console");
        screen.SteamDownloadNotFound(["first", "second"]);
        StringAssert.Contains(screen.Notice!, "first; second");

        Assert.IsTrue(screen.BeginSteamImport("depot", "target"));
        Assert.IsFalse(screen.BeginSteamImport("depot", "other"), "one copy at a time");
        Assert.IsFalse(screen.BeginInstall("setup.exe", "other"));
        Assert.IsFalse(screen.CanPlay);
        Assert.AreEqual("Cancel copying", screen.CancelText);
        Assert.IsNull(screen.Play());
        Assert.AreEqual("Wait for the copy to finish, or cancel it.", screen.Notice);
        screen.ToggleSteamGuide();
        Assert.IsTrue(screen.SteamGuideOpen, "the steps stay while copying");
        screen.SteamImportProgress(new(SteamImportStage.Copying, 3, 74, 5 * 1024 * 1024, 100 * 1024 * 1024, "arcanum1.dat"));
        Assert.AreEqual("Copying the game files… 3/74 files, 5/100 MB", screen.Notice);

        screen.SteamImportDone(new(SteamImportStatus.Incomplete, "5 of about 100 MB are there"));
        Assert.AreEqual(detected, screen.Folder!.Path, "an unfinished download leaves the folder as it was");
        StringAssert.Contains(screen.Notice!, "The download is not complete (5 of about 100 MB are there)");
        Assert.IsTrue(screen.SteamGuideOpen && screen.CanPlay);

        var copied = Game(Path.Combine(_root, "copied"));
        screen.BeginSteamImport("depot", copied);
        screen.SteamImportDone(new(SteamImportStatus.Imported, null, GameFolderLocator.Check(copied)));
        Assert.IsFalse(screen.Busy);
        Assert.IsFalse(screen.SteamGuideOpen);
        Assert.AreEqual($"Copied the Steam download into {copied}; the folder is chosen. Press Play to start.", screen.Notice);
        Assert.AreEqual(copied, screen.Play()!.GameRoot);
    }

    /// <summary>ARC-DEV-069: the game data (prototype files) being prepared on the first Play: the screen is busy like during an unpack, shows the
    /// progress, and a failure is named in words that never tell the player to run Arcanum.exe.</summary>
    [TestMethod]
    public void While_the_game_data_is_prepared_the_folder_and_Play_are_off_and_the_progress_and_failures_are_named()
    {
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        Assert.AreEqual("Cancel unpacking", screen.CancelText);
        Assert.IsTrue(screen.BeginPrototypes());
        Assert.IsFalse(screen.BeginPrototypes(), "one preparation at a time");
        Assert.IsFalse(screen.BeginInstall("setup.exe", "target"), "no unpack while the data is prepared");
        Assert.IsTrue(screen.Busy && screen.Preparing);
        Assert.IsFalse(screen.CanPlay);
        Assert.AreEqual("Cancel", screen.CancelText);
        Assert.IsNull(screen.Play());
        Assert.AreEqual("Wait for the game data to be prepared, or cancel it.", screen.Notice);
        screen.UseFolder(GameFolderLocator.Choose(Game(Path.Combine(_root, "other"))));
        Assert.AreEqual(detected, screen.Folder!.Path, "the folder does not change while the data is prepared");

        screen.PrototypesProgress(new(PrototypeProvisionStage.Building));
        StringAssert.StartsWith(screen.Notice!, "Preparing the game data from your Arcanum.exe (first start only");
        screen.PrototypesProgress(new(PrototypeProvisionStage.Building, 312, 1423));
        StringAssert.EndsWith(screen.Notice!, " 312/1423");
        screen.PrototypesProgress(new(PrototypeProvisionStage.Storing, 1423, 1423));
        Assert.AreEqual("Saving the prepared game data…", screen.Notice);

        screen.PrototypesDone(new(PrototypeProvisionStatus.Built, Path.Combine(_root, "cache"), Files: 1423));
        Assert.IsFalse(screen.Busy || screen.Preparing);
        Assert.IsNull(screen.Notice, "a success says nothing: the game starts");
        Assert.IsTrue(screen.CanPlay);
        screen.InstallProgress(new(GogInstallStage.Unpacking, 1, 2, 1, 2, "x"));
        Assert.IsNull(screen.Notice, "an unpack's progress does not reach an idle screen");

        // A failure leaves the screen open with Play still offered, in words.
        screen.BeginPrototypes();
        screen.PrototypesDone(new(PrototypeProvisionStatus.UnverifiedExecutable));
        Assert.IsFalse(screen.Busy);
        Assert.IsTrue(screen.CanPlay);
        StringAssert.Contains(screen.Notice!, "not the retail 1.0.7.4 one");
        foreach (var status in Enum.GetValues<PrototypeProvisionStatus>())
        {
            var text = LauncherScreen.Describe(new PrototypeProvisionResult(status, Detail: "a detail"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), status.ToString());
            Assert.IsFalse(text.Contains("run Arcanum.exe", StringComparison.OrdinalIgnoreCase) || text.Contains("start Arcanum.exe", StringComparison.OrdinalIgnoreCase),
                $"{status}: the player is never told to run the original: {text}");
            if (status is not (PrototypeProvisionStatus.Present or PrototypeProvisionStatus.Cached or PrototypeProvisionStatus.Built or PrototypeProvisionStatus.Cancelled))
                StringAssert.Contains(text, "a detail", status.ToString());
        }
        foreach (var stage in Enum.GetValues<PrototypeProvisionStage>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(LauncherScreen.Describe(new PrototypeProvisionProgress(stage))), stage.ToString());
    }

    [TestMethod]
    public void Every_unpack_outcome_has_words_and_a_missing_tool_names_the_way_to_get_it()
    {
        foreach (var status in Enum.GetValues<GogInstallStatus>())
        {
            var text = LauncherScreen.Describe(new GogInstallResult(status, "detail"), "target");
            Assert.IsTrue(text.Length > 8 && !text.StartsWith(status + " ", StringComparison.Ordinal), status + ": " + text);
        }
        Assert.AreEqual("Unpacking was cancelled. Files may be left in T; empty it or choose another folder before trying again.",
            LauncherScreen.Describe(new GogInstallResult(GogInstallStatus.Cancelled), "T"));
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        screen.InstallToolMissing();
        StringAssert.Contains(screen.Notice!, "innoextract");
        Assert.IsFalse(screen.Busy);
        Assert.AreEqual("Checking the installer…", LauncherScreen.Describe(new GogInstallProgress(GogInstallStage.Checking, 0, 0, 0, 0, null)));
    }
}
