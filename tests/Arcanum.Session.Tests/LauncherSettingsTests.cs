using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data.Content;
using Arcanum.Session.Launcher;
using Arcanum.Session.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-046: the launcher settings codec, its file and the start's game folder, on synthetic folders.</summary>
[TestClass]
public sealed class LauncherSettingsTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-launcher-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static readonly GameFolderIdentity Identity = new("1.0.7.4", 13, new string('a', 64), true,
        [new("tig.dat", 3), new("arcanum1.dat", 5), new("modules/Arcanum.dat", 11)]);

    private static readonly LauncherSettings Full = new(Path.Combine(Path.GetTempPath(), "Arcanum"), GameSource.Gog, Identity, true, RuleMode.Original,
        new(LauncherWindowMode.Fullscreen, 1, 150)) { DebugMode = true };

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

    private static GameFolderPlatform Mac(string root) => new(GameFolderPlatformKind.MacOS, Path.Combine(root, "home"), Path.Combine(root, "Applications"), null, null, _ => null);

    private static LauncherSettings Read(string json)
    {
        var read = LauncherSettingsCodec.Deserialize(json);
        Assert.AreEqual(LauncherSettingsReadStatus.Read, read.Status, json);
        return read.Settings;
    }

    [TestMethod]
    public void Settings_round_trip_through_the_file_text()
    {
        var text = LauncherSettingsCodec.Serialize(Full);
        var read = LauncherSettingsCodec.Deserialize(text);
        Assert.AreEqual(LauncherSettingsReadStatus.Read, read.Status);
        Assert.AreEqual(0, read.Defaulted.Count);
        Assert.AreEqual(0, read.UnknownKeys.Count);
        Assert.AreEqual(Full with { GameIdentity = null }, read.Settings with { GameIdentity = null });
        Assert.IsTrue(Identity.SameAs(read.Settings.GameIdentity));
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.AreEqual(1, (int)root["schema"]!);
        Assert.AreEqual("original", (string)root["rules"]!["active"]!);
        Assert.AreEqual("fullscreen", (string)root["display"]!["window"]!);
        Assert.IsTrue((bool)root["debugMode"]!, "ARC-DEV-050: the debug mode is stored");
        Assert.IsFalse((bool)JsonNode.Parse(LauncherSettingsCodec.Serialize(LauncherSettings.Defaults))!["debugMode"]!, "and off by default");
        Assert.IsTrue((bool)JsonNode.Parse(LauncherSettingsCodec.Serialize(LauncherSettings.Defaults))!["holdToQuit"]!, "ARC-DEV-2270: holding the quit key quits at once by default");
        var noHold = LauncherSettings.Defaults with { HoldToQuit = false };
        Assert.AreEqual(noHold, Read(LauncherSettingsCodec.Serialize(noHold)), "the hold-to-quit choice round-trips");
        Assert.IsTrue(Read("""{"schema":1}""").HoldToQuit, "an older file without the key reads as on");
        Assert.IsTrue(LauncherSettingsCodec.Deserialize("""{"schema":1,"holdToQuit":"no"}""").Defaulted.Contains("holdToQuit"), "a bad value is defaulted and named");

        // ARC-UI-2771: a first start (no file, or a file without a display line) is fullscreen; a stored choice is kept.
        Assert.AreEqual(LauncherWindowMode.Fullscreen, LauncherSettings.Defaults.Display.WindowMode);
        Assert.AreEqual(LauncherWindowMode.Fullscreen, LauncherSettingsCodec.Deserialize(null).Settings.Display.WindowMode, "no file");
        Assert.AreEqual(LauncherWindowMode.Fullscreen, Read("""{"schema":1}""").Display.WindowMode, "no display line");
        Assert.AreEqual("fullscreen", (string)JsonNode.Parse(LauncherSettingsCodec.Serialize(LauncherSettings.Defaults))!["display"]!["window"]!);
        var windowed = LauncherSettings.Defaults with { Display = LauncherDisplay.Default with { WindowMode = LauncherWindowMode.Windowed } };
        Assert.AreEqual(LauncherWindowMode.Windowed, Read(LauncherSettingsCodec.Serialize(windowed)).Display.WindowMode, "a stored window is kept");
        Assert.AreEqual(LauncherWindowMode.Windowed, Read("""{"schema":1,"display":{"window":"windowed","monitor":-1,"uiScale":100}}""").Display.WindowMode);
        Assert.AreEqual(LauncherSettings.Defaults, Read(LauncherSettingsCodec.Serialize(LauncherSettings.Defaults)));
        Assert.AreEqual(LauncherSettings.Defaults, Read("""{"schema":1}"""), "missing keys take their defaults");
        var bare = new GameFolderIdentity(null, null, null, false, []);
        Assert.IsTrue(bare.SameAs(Read(LauncherSettingsCodec.Serialize(Full with { GameIdentity = bare })).GameIdentity), "an identity without version or executable");
    }

    [TestMethod]
    public void Text_that_is_not_a_settings_file_reads_as_the_defaults()
    {
        Assert.AreEqual(LauncherSettingsReadStatus.Missing, LauncherSettingsCodec.Deserialize(null).Status);
        foreach (var text in new[] { "", "{", "[]", "1", "null", """{"rules":{"active":"original"}}""", """{"schema":1,"schema":1}""", """{"schema":1,"gameRoot":"\uD800"}""", "{\"schema\":1,\"gameRoot\":\"\uD800\"}",
                     "{\"schema\":1,\"x\":\"" + new string('a', LauncherSettingsCodec.MaxBytes) + "\"}" })
        {
            var read = LauncherSettingsCodec.Deserialize(text);
            Assert.AreEqual(LauncherSettingsReadStatus.Invalid, read.Status, text.Length > 80 ? "too large" : text);
            Assert.AreEqual(LauncherSettings.Defaults, read.Settings);
        }
        foreach (var text in new[] { """{"schema":2,"gameRoot":"/x"}""", """{"schema":"1"}""", """{"schema":1.5}""", """{"schema":1e10}""", """{"schema":null}""", """{"schema":{}}""" })
        {
            var newer = LauncherSettingsCodec.Deserialize(text);
            Assert.AreEqual((LauncherSettingsReadStatus.OtherSchema, LauncherSettings.Defaults), (newer.Status, newer.Settings), text);
        }
    }

    [TestMethod]
    public void A_value_this_build_cannot_use_takes_its_default_and_is_listed()
    {
        var read = LauncherSettingsCodec.Deserialize("""
            {"schema":1,"gameRoot":"a\u0007b","gameSource":"steam","showAtStart":"yes","rules":{"active":"my-mix"},"debugMode":1,
             "display":{"window":"windowed","monitor":16,"uiScale":100},
             "gameIdentity":{"version":"1.0","exeBytes":1,"exeSha256":"ABC","gog":true,"archives":[]}}
            """);
        Assert.AreEqual(LauncherSettingsReadStatus.Read, read.Status);
        Assert.AreEqual(LauncherSettings.Defaults, read.Settings);
        CollectionAssert.AreEqual(new[] { "gameRoot", "gameSource", "gameIdentity", "showAtStart", "debugMode", "rules", "display" }, read.Defaulted.ToArray());

        Assert.AreEqual(LauncherSettings.Defaults, Read("""{"schema":1,"gameRoot":null,"gameIdentity":null}"""));
        foreach (var root in new[] { "relative/Arcanum", "Arcanum", OperatingSystem.IsWindows() ? "/Arcanum" : @"C:\GOG Games\Arcanum" })
            CollectionAssert.AreEqual(new[] { "gameRoot" }, LauncherSettingsCodec.Deserialize($$"""{"schema":1,"gameRoot":{{System.Text.Json.JsonSerializer.Serialize(root)}}}""").Defaulted.ToArray(), "not a full path here: " + root);
        foreach (var identity in new[] { """{"gog":true}""", """{"gog":1,"archives":[]}""", """{"gog":true,"archives":[{"name":"","bytes":1}]}""",
                     """{"gog":true,"archives":[{"name":"a","bytes":-1}]}""", """{"gog":true,"exeBytes":"1","archives":[]}""", """{"gog":true,"version":"","archives":[]}""",
                     "{\"gog\":true,\"archives\":[" + string.Join(',', Enumerable.Repeat("""{"name":"a","bytes":1}""", LauncherSettingsCodec.MaxArchives + 1)) + "]}" })
            CollectionAssert.AreEqual(new[] { "gameIdentity" }, LauncherSettingsCodec.Deserialize($$"""{"schema":1,"gameIdentity":{{identity}}}""").Defaulted.ToArray(), identity);
        foreach (var display in new[] { """{"window":"windowed","monitor":-2,"uiScale":100}""", """{"window":"windowed","monitor":0,"uiScale":49}""",
                     """{"window":"windowed","monitor":0,"uiScale":301}""", """{"window":"borderless","monitor":0,"uiScale":100}""", """{"window":"windowed","monitor":0}""" })
            CollectionAssert.AreEqual(new[] { "display" }, LauncherSettingsCodec.Deserialize($$"""{"schema":1,"display":{{display}}}""").Defaulted.ToArray(), display);
    }

    [TestMethod]
    public void A_rewrite_keeps_the_keys_this_build_does_not_know()
    {
        var read = LauncherSettingsCodec.Deserialize("""{"schema":1,"rules":{"active":"steamwoven","custom":[{"name":"mix"}]},"future":{"a":1},"display":{"window":"windowed","monitor":0,"uiScale":100,"hdr":true}}""");
        CollectionAssert.AreEqual(new[] { "future", "rules.custom", "display.hdr" }, read.UnknownKeys.ToArray());
        var text = LauncherSettingsCodec.Serialize(read.Settings with { Rules = RuleMode.Original }, read);
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.AreEqual(1, (int)root["future"]!["a"]!);
        Assert.AreEqual("original", (string)root["rules"]!["active"]!);
        Assert.AreEqual("mix", (string)root["rules"]!["custom"]![0]!["name"]!);
        Assert.IsTrue((bool)root["display"]!["hdr"]!);
        Assert.AreEqual(100, (int)root["display"]!["uiScale"]!);
        Assert.IsFalse(JsonNode.Parse(LauncherSettingsCodec.Serialize(read.Settings))!.AsObject().ContainsKey("future"), "only a read passed in is kept");
    }

    [TestMethod]
    public void Settings_that_cannot_be_written_back_throw()
    {
        foreach (var settings in new[]
                 {
                     Full with { GameRoot = "a\nb" }, Full with { GameRoot = " " }, Full with { GameRoot = "relative" }, Full with { Display = Full.Display with { UiScalePercent = 10 } },
                     Full with { GameSource = (GameSource)7 }, Full with { GameIdentity = Identity with { ExeSha256 = "x" } },
                     Full with { GameIdentity = Identity with { Archives = Enumerable.Repeat(new GameArchiveSize("a.dat", 1), GameFolderLocator.MaxArchives + 1).ToArray() } },
                 })
            Assert.Throws<ArgumentException>(() => LauncherSettingsCodec.Serialize(settings));
        var most = Identity with { Archives = Enumerable.Range(0, GameFolderLocator.MaxArchives).Select(i => new GameArchiveSize(new string('a', 200) + i + ".dat", i)).ToArray() };
        Assert.IsTrue(most.SameAs(Read(LauncherSettingsCodec.Serialize(Full with { GameIdentity = most })).GameIdentity), "every identity the locator makes is stored");
    }

    [TestMethod]
    public void The_launcher_line_is_replaced_alone_and_a_newer_schema_is_never_overwritten()
    {
        var path = Path.Combine(_root, "deep", "folder", LauncherSettingsFile.FileName);
        Assert.AreEqual(LauncherSettingsReadStatus.Missing, LauncherSettingsFile.Load(path).Status);
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, Full));
        Assert.AreEqual(Full.GameRoot, LauncherSettingsFile.Load(path).Settings.GameRoot);
        CollectionAssert.AreEqual(new[] { LauncherSettingsFile.FileName }, Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray(), "no temporary file is left");
        Assert.AreEqual(1, File.ReadAllLines(path).Length, "the launcher object is one line");

        // A write changes the launcher line and no other setting of the one file.
        File.WriteAllText(path, "always run=1\nlauncher={\"schema\":1,\"future\":true,\"showAtStart\":true}\nmusic volume=3\n");
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, LauncherSettings.Defaults));
        var lines = File.ReadAllLines(path);
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("always run=1", lines[0]);
        Assert.IsTrue(lines[1].StartsWith("launcher=", StringComparison.Ordinal) && lines[1].Contains("\"future\":true", StringComparison.Ordinal), "the launcher object's unknown keys are kept");
        Assert.AreEqual("music volume=3", lines[2]);
        Assert.IsFalse(LauncherSettingsFile.Load(path).Settings.ShowAtStart);

        File.WriteAllText(path, "launcher={\"schema\":2,\"gameRoot\":\"/x\"}\nmusic volume=3\n");
        Assert.AreEqual(LauncherSettingsSaveStatus.RefusedOtherSchema, LauncherSettingsFile.Save(path, Full));
        Assert.AreEqual("launcher={\"schema\":2,\"gameRoot\":\"/x\"}\nmusic volume=3\n", File.ReadAllText(path));

        File.WriteAllBytes(path, [(byte)'l', 0xFF, (byte)'=', (byte)'1']);
        Assert.AreEqual(LauncherSettingsReadStatus.Missing, LauncherSettingsFile.Load(path).Status, "a line with a bad byte is dropped alone");
        File.WriteAllText(path, new string(' ', ArcanumConfig.MaxFileBytes + 1), Encoding.ASCII);
        Assert.AreEqual(LauncherSettingsReadStatus.Invalid, LauncherSettingsFile.Load(path).Status, "too large");
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, Full), "an invalid file is replaced");
        File.WriteAllText(path, "launcher=not json\n");
        Assert.AreEqual(LauncherSettingsReadStatus.Invalid, LauncherSettingsFile.Load(path).Status, "a launcher line that is not a settings object");
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, Full));

        Directory.CreateDirectory(Path.Combine(_root, "dir.cfg"));
        Assert.AreEqual(LauncherSettingsReadStatus.Missing, LauncherSettingsFile.Load(Path.Combine(_root, "dir.cfg")).Status);
        Assert.AreEqual(LauncherSettingsSaveStatus.Failed, LauncherSettingsFile.Save(Path.Combine(_root, "dir.cfg"), Full));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
            try
            {
                if (LauncherSettingsFile.Load(path).Status == LauncherSettingsReadStatus.Unreadable) // not when the tests run as root
                    Assert.AreEqual(LauncherSettingsSaveStatus.RefusedUnreadable, LauncherSettingsFile.Save(path, Full));
            }
            finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        }
    }

    [TestMethod]
    public void Reset_replaces_the_launcher_line_with_the_defaults_and_keeps_the_other_settings()
    {
        // ARC-UI-014: the player's reset is the one write that replaces a newer build's or a damaged launcher object.
        var path = Path.Combine(_root, "reset", LauncherSettingsFile.FileName);
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Reset(path), "no file yet");
        Assert.AreEqual((LauncherSettingsReadStatus.Read, LauncherSettings.Defaults), (LauncherSettingsFile.Load(path).Status, LauncherSettingsFile.Load(path).Settings));
        foreach (var text in new[] { "launcher={\"schema\":2,\"gameRoot\":\"/x\"}", "launcher=not json", "launcher={\"schema\":1,\"future\":true,\"rules\":{\"active\":\"original\"}}" })
        {
            File.WriteAllText(path, "visual profile=hd\n" + text + "\n");
            Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Reset(path), text);
            var read = LauncherSettingsFile.Load(path);
            Assert.AreEqual((LauncherSettingsReadStatus.Read, LauncherSettings.Defaults), (read.Status, read.Settings), text);
            Assert.AreEqual(0, read.UnknownKeys.Count, "a reset keeps none of the launcher object's keys");
            Assert.AreEqual("hd", ArcanumConfig.Read(path).Get("visual profile"), "a reset leaves the other settings");
        }
        CollectionAssert.AreEqual(new[] { LauncherSettingsFile.FileName }, Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray());
        Directory.CreateDirectory(Path.Combine(_root, "dir.cfg"));
        Assert.AreEqual(LauncherSettingsSaveStatus.Failed, LauncherSettingsFile.Reset(Path.Combine(_root, "dir.cfg")));
    }

    [TestMethod]
    public void A_start_opens_the_stored_folder_while_it_is_a_game_folder()
    {
        var game = Game(Path.Combine(_root, "Arcanum"));
        var stored = LauncherSettings.Defaults.WithGameFolder(GameFolderLocator.Check(game));
        Assert.AreEqual((game, GameSource.Other), (stored.GameRoot, stored.GameSource));

        var start = GameFolderStartup.Resolve(stored, Mac(_root));
        Assert.AreEqual((GameFolderStartupStatus.Ready, GameFolderStartupSource.Stored, game), (start.Status, start.Source, start.Folder!.Path));
        Assert.IsFalse(start.IdentityChanged || start.ShowLauncher || start.SettingsChanged);
        Assert.IsNull(start.Detection);
        Assert.IsTrue(GameFolderStartup.Resolve(stored with { ShowAtStart = true }, Mac(_root)).ShowLauncher);

        Game(game, terrainBytes: 9);
        var changed = GameFolderStartup.Resolve(stored, Mac(_root));
        Assert.IsTrue(changed.Ready && changed.IdentityChanged && changed.ShowLauncher);
        Assert.IsFalse(changed.SettingsChanged, "the launcher confirms a changed installation; until then each start says so");
        Assert.AreSame(stored, changed.Settings);
        var first = GameFolderStartup.Resolve(stored with { GameIdentity = null }, Mac(_root));
        Assert.IsTrue(first.Ready && !first.IdentityChanged && first.SettingsChanged, "a folder stored without an identity gains one");
        Assert.IsTrue(first.Settings.GameIdentity!.SameAs(GameFolderLocator.Check(game).Identity));
    }

    [TestMethod]
    public void A_start_detects_a_folder_when_none_is_stored_or_the_stored_one_is_gone()
    {
        var none = GameFolderStartup.Resolve(LauncherSettings.Defaults, Mac(_root));
        Assert.AreEqual(GameFolderStartupStatus.NotFound, none.Status);
        Assert.IsTrue(none.ShowLauncher);
        Assert.IsFalse(none.SettingsChanged);
        Assert.AreEqual(2, none.Detection!.Checked.Count);

        var detected = Game(Path.Combine(_root, "home", "Applications", "Arcanum"));
        var first = GameFolderStartup.Resolve(LauncherSettings.Defaults with { Rules = RuleMode.Original }, Mac(_root));
        Assert.AreEqual((GameFolderStartupStatus.Ready, GameFolderStartupSource.Detected, detected), (first.Status, first.Source, first.Folder!.Path));
        Assert.IsTrue(first.ShowLauncher);
        Assert.IsFalse(first.SettingsChanged, "the launcher confirms a detected folder");
        Assert.IsNull(first.Settings.GameRoot);

        var moved = LauncherSettings.Defaults with { GameRoot = Path.Combine(_root, "moved") };
        var gone = GameFolderStartup.Resolve(moved, Mac(_root));
        Assert.AreEqual((GameFolderStatus.MissingFolder, GameFolderStartupSource.Detected, detected), (gone.StoredCheck!.Status, gone.Source, gone.Folder!.Path));
        Assert.IsTrue(gone.ShowLauncher);
        Assert.IsFalse(gone.SettingsChanged, "the player's stored folder is never replaced by a detected one");
        Assert.AreSame(moved, gone.Settings);
        Assert.Throws<ArgumentException>(() => LauncherSettings.Defaults.WithGameFolder(gone.StoredCheck));
    }
}
