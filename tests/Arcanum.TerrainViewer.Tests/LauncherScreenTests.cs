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
        var proto = Path.Combine(folder, "data", "proto");
        Directory.CreateDirectory(proto);
        File.WriteAllBytes(Path.Combine(proto, "000001 - Synthetic.pro"), [0]);
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
        var played = screen.Play()!;
        Assert.AreEqual((detected, RuleMode.Original, true, GameSource.Other, true), (played.GameRoot, played.Rules, played.ShowAtStart, played.GameSource, played.DebugMode));
        Assert.IsTrue(played.GameIdentity!.SameAs(startup.Folder!.Identity), "Play stores the identity, so a later change is noticed");
        Assert.AreEqual(LauncherSettings.Defaults.Display, played.Display, "the display at start is kept as stored");
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.SelectRules((RuleMode)7));
    }

    [TestMethod]
    public void A_detected_installation_the_original_never_ran_says_to_start_it_and_Play_checks_again()
    {
        // ARC-DEV-066: a fresh installation has no data/proto until Arcanum.exe's first start writes it.
        var detected = Game(Path.Combine(_root, "Applications", "Arcanum"));
        var proto = Path.Combine(detected, "data", "proto");
        Directory.Delete(proto, recursive: true);
        var screen = new LauncherScreen(Read(LauncherSettings.Defaults), GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac()), LauncherShowReason.FirstRun);
        Assert.AreEqual((GameFolderStatus.MissingPrototypes, detected), (screen.Folder!.Status, screen.Folder.Path), "shown, not hidden as none found");
        Assert.AreEqual((false, true), (screen.CanPlay, screen.PlayOffered));
        Assert.AreEqual(0, screen.Candidates.Count, "ready candidates only");
        StringAssert.Contains(screen.FolderText, "start Arcanum.exe here once until its main menu");
        StringAssert.Contains(screen.FolderText, GameFolderLocator.PrototypeFolder);
        var rechecks = 0;
        GameFolderCheck Recheck(GameFolderCheck stale) { rechecks++; return GameFolderLocator.Check(stale.Path, stale.Origin); }
        Assert.IsNull(screen.Play(Recheck));
        Assert.AreEqual(1, rechecks);
        StringAssert.StartsWith(screen.Notice, "Not a game folder the game can open: the original game was never started from this folder");
        Directory.CreateDirectory(proto);
        File.WriteAllBytes(Path.Combine(proto, "000001 - Synthetic.pro"), [0]);
        Assert.AreEqual(detected, screen.Play(Recheck)!.GameRoot, "Play checks the folder again once the original wrote its prototypes");
        Assert.AreEqual(2, rechecks);
        screen.Play(Recheck);
        Assert.AreEqual(2, rechecks, "a ready folder is not checked again");
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
}
