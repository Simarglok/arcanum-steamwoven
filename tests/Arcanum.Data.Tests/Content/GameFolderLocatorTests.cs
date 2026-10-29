using System.Security.Cryptography;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Content;

/// <summary>ARC-DEV-046: the game folder locator on synthetic folders (no original content).</summary>
[TestClass]
public sealed class GameFolderLocatorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-game-folder-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    /// <summary>A synthetic game folder with the inputs map mode needs (each file a few placeholder bytes).</summary>
    internal static string Game(string folder, bool gog = true, string? version = "1.0.7.4")
    {
        void File(string relative, int bytes)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        }
        File("tig.dat", 3); File("arcanum1.dat", 5); File("arcanum2.dat", 7); File("modules/Arcanum.dat", 11); File("Arcanum.exe", 13);
        File("data/proto/000001 - Synthetic.pro", 1);
        if (gog) File(GameFolderLocator.GogMarkerFile, 1);
        if (version is not null) System.IO.File.WriteAllText(Path.Combine(folder, "Version.txt"), version + "\r\n");
        return folder;
    }

    private static GameFolderPlatform Mac(string root) => new(GameFolderPlatformKind.MacOS, Path.Combine(root, "home"), Path.Combine(root, "Applications"), null, null, _ => null);

    [TestMethod]
    public void A_game_folder_is_ready_with_its_map_inputs_and_identity()
    {
        var game = Game(Path.Combine(_root, "Arcanum"));
        var check = GameFolderLocator.Check(game + Path.DirectorySeparatorChar);

        Assert.AreEqual(GameFolderStatus.Ready, check.Status, check.Detail);
        Assert.AreEqual(game, check.Path);
        Assert.AreEqual(new GameFolderInputs(game, Path.Combine(game, "modules", "Arcanum.dat"), Path.Combine(game, "arcanum1.dat"), Path.Combine(game, "arcanum2.dat")), check.Inputs);
        var identity = check.Identity!;
        Assert.AreEqual("1.0.7.4", identity.Version);
        Assert.AreEqual(13L, identity.ExeBytes);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(game, "Arcanum.exe")))).ToLowerInvariant(), identity.ExeSha256);
        Assert.IsTrue(identity.GogMarker);
        Assert.IsFalse(identity.VerifiedRelease, "only the retail 1.0.7.4 executable is the verified release");
        CollectionAssert.AreEqual(new[] { "tig.dat:3", "arcanum1.dat:5", "arcanum2.dat:7", "modules/Arcanum.dat:11" },
            identity.Archives.Select(archive => $"{archive.Name}:{archive.Bytes}").ToArray(), "archives in mount order");
        Assert.IsTrue(identity.SameAs(GameFolderLocator.Check(game).Identity));
    }

    [TestMethod]
    public void An_installation_the_original_never_ran_has_no_prototypes()
    {
        // ARC-DEV-066: Arcanum.exe writes data/proto at its first start; a fresh installation holds no prototype file.
        var game = Game(Path.Combine(_root, "Arcanum"));
        var proto = Path.Combine(game, "data", "proto");
        Directory.Delete(proto, recursive: true);
        Assert.AreEqual((GameFolderStatus.MissingPrototypes, GameFolderLocator.PrototypeFolder), Pair(GameFolderLocator.Check(game)));
        Assert.AreEqual(game, GameFolderLocator.Check(game).Path);
        Assert.IsNull(GameFolderLocator.Check(game).Identity);

        // Only NNNNNN - label.pro names count; a module folder's copy is never read at game start.
        Directory.CreateDirectory(proto);
        File.WriteAllBytes(Path.Combine(proto, "readme.pro"), [0]);
        File.WriteAllBytes(Path.Combine(proto, "000001 - Synthetic.txt"), [0]);
        var module = Directory.CreateDirectory(Path.Combine(game, "modules", "Arcanum", "proto")).FullName;
        File.WriteAllBytes(Path.Combine(module, "000001 - Synthetic.pro"), [0]);
        Assert.AreEqual(GameFolderStatus.MissingPrototypes, GameFolderLocator.Check(game).Status);

        // The game folder's own proto/ is a base source too; data/proto is where the original writes them.
        var root = Directory.CreateDirectory(Path.Combine(game, "proto")).FullName;
        File.WriteAllBytes(Path.Combine(root, "000001 - Synthetic.pro"), [0]);
        Assert.AreEqual(GameFolderStatus.Ready, GameFolderLocator.Check(game).Status);
        Directory.Delete(root, recursive: true);
        File.WriteAllBytes(Path.Combine(proto, "000001 - Synthetic.pro"), [0]);
        Assert.AreEqual(GameFolderStatus.Ready, GameFolderLocator.Check(game).Status);
    }

    [TestMethod]
    public void A_changed_installation_has_another_identity()
    {
        var game = Game(Path.Combine(_root, "Arcanum"));
        var before = GameFolderLocator.Check(game).Identity!;
        File.WriteAllBytes(Path.Combine(game, "arcanum2.dat"), new byte[8]);
        Assert.IsFalse(before.SameAs(GameFolderLocator.Check(game).Identity));
        File.Delete(Path.Combine(game, GameFolderLocator.GogMarkerFile));
        Assert.IsFalse(GameFolderLocator.Check(game).Identity!.GogMarker);
        Assert.IsFalse(before.SameAs(null));
    }

    [TestMethod]
    public void Names_match_case_insensitively_and_an_odd_version_file_is_no_version()
    {
        var game = Path.Combine(_root, "Arcanum");
        Directory.CreateDirectory(Path.Combine(game, "MODULES"));
        foreach (var (name, bytes) in new[] { ("TIG.DAT", 1), ("ARCANUM1.DAT", 2), ("Arcanum2.Dat", 3), ("MODULES/arcanum.DAT", 4), ("ARCANUM.EXE", 5) })
            File.WriteAllBytes(Path.Combine(game, name), new byte[bytes]);
        Directory.CreateDirectory(Path.Combine(game, "DATA", "PROTO"));
        File.WriteAllBytes(Path.Combine(game, "DATA", "PROTO", "000002 - Upper.PRO"), [0]);
        File.WriteAllText(Path.Combine(game, "VERSION.TXT"), "1.0\u0001");
        var check = GameFolderLocator.Check(game);
        Assert.AreEqual(GameFolderStatus.Ready, check.Status, check.Detail);
        Assert.IsNull(check.Identity!.Version);
        Assert.AreEqual(5L, check.Identity.ExeBytes);
        File.WriteAllBytes(Path.Combine(game, "VERSION.TXT"), [(byte)'1', 0xE9]);
        Assert.IsNull(GameFolderLocator.Check(game).Identity!.Version, "a byte outside ASCII");
        File.WriteAllText(Path.Combine(game, "VERSION.TXT"), new string('1', GameFolderLocator.MaxVersionBytes + 1));
        Assert.IsNull(GameFolderLocator.Check(game).Identity!.Version);
        File.Delete(Path.Combine(game, "VERSION.TXT")); File.Delete(Path.Combine(game, "ARCANUM.EXE"));
        var bare = GameFolderLocator.Check(game).Identity!;
        Assert.IsNull(bare.Version); Assert.IsNull(bare.ExeBytes); Assert.IsNull(bare.ExeSha256); Assert.IsFalse(bare.GogMarker);
    }

    [TestMethod]
    public void A_folder_without_a_game_input_is_refused_with_the_missing_input()
    {
        Assert.AreEqual(GameFolderStatus.MissingFolder, GameFolderLocator.Check(Path.Combine(_root, "none")).Status);
        Assert.AreEqual(GameFolderStatus.MissingFolder, GameFolderLocator.Check("").Status);
        Assert.AreEqual(GameFolderStatus.MissingFolder, GameFolderLocator.Check(_root + "\n").Status);
        Assert.AreEqual(GameFolderStatus.MissingFolder, GameFolderLocator.Check(new string('a', GameFolderLocator.MaxPathLength + 1)).Status);
        Assert.AreEqual(GameFolderStatus.MissingTig, GameFolderLocator.Check(_root).Status);

        var game = Game(Path.Combine(_root, "Arcanum"));
        File.Delete(Path.Combine(game, "arcanum2.dat"));
        Assert.AreEqual((GameFolderStatus.MissingTerrainArchive, "arcanum2.dat"), Pair(GameFolderLocator.Check(game)));
        File.Delete(Path.Combine(game, "arcanum1.dat"));
        Assert.AreEqual((GameFolderStatus.MissingActorArchive, "arcanum1.dat"), Pair(GameFolderLocator.Check(game)));
        File.Delete(Path.Combine(game, "modules", "Arcanum.dat"));
        Assert.AreEqual(GameFolderStatus.MissingModule, GameFolderLocator.Check(game).Status);
        Directory.CreateDirectory(Path.Combine(game, "modules", "Arcanum"));
        Assert.AreEqual((GameFolderStatus.MissingModuleArchive, "modules/Arcanum.dat"), Pair(GameFolderLocator.Check(game)));
        Assert.IsNull(GameFolderLocator.Check(game).Inputs);
        File.Delete(Path.Combine(game, "tig.dat"));
        Assert.AreEqual(GameFolderStatus.MissingTig, GameFolderLocator.Check(game).Status);
    }

    [TestMethod]
    public void A_folder_mounting_more_archives_than_the_limit_is_an_unsupported_layout()
    {
        var game = Game(Path.Combine(_root, "Arcanum"));
        for (var i = 10; i < 10 + GameFolderLocator.MaxArchives - 4; i++) File.WriteAllBytes(Path.Combine(game, $"arcanum{i}.dat"), [1]);
        var most = GameFolderLocator.Check(game);
        Assert.AreEqual(GameFolderStatus.Ready, most.Status);
        Assert.AreEqual(GameFolderLocator.MaxArchives, most.Identity!.Archives.Count);
        File.WriteAllBytes(Path.Combine(game, "arcanum99.dat"), [1]);
        Assert.AreEqual((GameFolderStatus.UnsupportedLayout, "archives"), Pair(GameFolderLocator.Check(game)));
    }

    [TestMethod]
    public void An_archive_name_with_a_control_character_is_an_unsupported_layout()
    {
        if (OperatingSystem.IsWindows()) return; // Windows file names cannot hold one.
        var game = Game(Path.Combine(_root, "Arcanum"));
        File.WriteAllBytes(Path.Combine(game, "arcanum\u0001.dat"), [1]);
        Assert.AreEqual((GameFolderStatus.UnsupportedLayout, "archive name"), Pair(GameFolderLocator.Check(game)));
    }

    private static (GameFolderStatus, string?) Pair(GameFolderCheck check) => (check.Status, check.Detail);

    [TestMethod]
    public void Windows_tries_the_gog_registry_path_then_the_default_folders()
    {
        var drive = Path.Combine(_root, "C");
        var programs = Path.Combine(drive, "Program Files (x86)");
        var installed = Path.Combine(_root, "Games", "Arcanum");
        var keys = new List<string>();
        var platform = new GameFolderPlatform(GameFolderPlatformKind.Windows, null, null, drive, programs,
            key => { keys.Add(key); return key.Contains("WOW6432Node", StringComparison.Ordinal) ? installed : installed + Path.DirectorySeparatorChar; });

        var candidates = GameFolderLocator.Candidates(platform);
        CollectionAssert.AreEqual(new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games\1207658829", @"SOFTWARE\GOG.com\Games\1207658829" }, keys);
        CollectionAssert.AreEqual(new[]
        {
            $"Registry:{installed}", $"DefaultFolder:{Path.Combine(drive, "GOG Games", "Arcanum")}",
            $"DefaultFolder:{Path.Combine(programs, "GOG Galaxy", "Games", "Arcanum")}",
        }, candidates.Select(candidate => $"{candidate.Origin}:{candidate.Path}").ToArray(), "each path once, in order");

        Assert.IsNull(GameFolderLocator.Detect(platform).Found);
        var galaxy = Game(Path.Combine(programs, "GOG Galaxy", "Games", "Arcanum"));
        var detection = GameFolderLocator.Detect(platform);
        Assert.AreEqual(3, detection.Checked.Count);
        Assert.AreEqual((galaxy, GameFolderOrigin.DefaultFolder), (detection.Found!.Path, detection.Found.Origin));
        Game(installed);
        Assert.AreEqual((installed, GameFolderOrigin.Registry), (GameFolderLocator.Detect(platform).Found!.Path, GameFolderLocator.Detect(platform).Found!.Origin));
    }

    [TestMethod]
    public void macOS_tries_the_applications_folders_then_crossover_bottles()
    {
        var platform = Mac(_root);
        var bottles = Path.Combine(_root, "home", "Library", "Application Support", "CrossOver", "Bottles");
        Directory.CreateDirectory(Path.Combine(bottles, "b"));
        Directory.CreateDirectory(Path.Combine(bottles, "a"));
        CollectionAssert.AreEqual(new[]
        {
            $"DefaultFolder:{Path.Combine(_root, "Applications", "Arcanum")}", $"DefaultFolder:{Path.Combine(_root, "home", "Applications", "Arcanum")}",
            $"CrossOverBottle:{Path.Combine(bottles, "a", "drive_c", "GOG Games", "Arcanum")}",
            $"CrossOverBottle:{Path.Combine(bottles, "a", "drive_c", "Program Files (x86)", "GOG Galaxy", "Games", "Arcanum")}",
            $"CrossOverBottle:{Path.Combine(bottles, "b", "drive_c", "GOG Games", "Arcanum")}",
            $"CrossOverBottle:{Path.Combine(bottles, "b", "drive_c", "Program Files (x86)", "GOG Galaxy", "Games", "Arcanum")}",
        }, GameFolderLocator.Candidates(platform).Select(candidate => $"{candidate.Origin}:{candidate.Path}").ToArray());

        var bottle = Game(Path.Combine(bottles, "b", "drive_c", "GOG Games", "Arcanum"));
        Assert.AreEqual((bottle, GameFolderOrigin.CrossOverBottle), (GameFolderLocator.Detect(platform).Found!.Path, GameFolderLocator.Detect(platform).Found!.Origin));
        var copied = Game(Path.Combine(_root, "home", "Applications", "Arcanum"), gog: false);
        Assert.AreEqual(copied, GameFolderLocator.Detect(platform).Found!.Path);

        Assert.AreEqual(2, GameFolderLocator.Candidates(platform with { Home = Path.Combine(_root, "no home") }).Count, "no bottles folder");
        Assert.AreEqual(1, GameFolderLocator.Candidates(platform with { Home = null }).Count);
        Assert.AreEqual(0, GameFolderLocator.Candidates(new GameFolderPlatform(GameFolderPlatformKind.Other, _root, _root, _root, _root, _ => _root)).Count);
    }

    [TestMethod]
    public void At_most_the_bottle_limit_is_looked_into()
    {
        var bottles = Path.Combine(_root, "home", "Library", "Application Support", "CrossOver", "Bottles");
        for (var i = 0; i < GameFolderLocator.MaxBottles + 3; i++) Directory.CreateDirectory(Path.Combine(bottles, $"b{i:D3}"));
        Assert.AreEqual(2 + 2 * GameFolderLocator.MaxBottles, GameFolderLocator.Candidates(Mac(_root)).Count);
    }

    [TestMethod]
    public void A_chosen_wrapper_or_bottle_opens_the_gog_folder_inside_it()
    {
        var wrapper = Path.Combine(_root, "Arcanum.app");
        var inner = Game(Path.Combine(wrapper, "Contents", "SharedSupport", "prefix", "drive_c", "Program Files (x86)", "GOG Galaxy", "Games", "Arcanum"));
        Assert.AreEqual((GameFolderStatus.Ready, inner, GameFolderOrigin.Wrapper), Triple(GameFolderLocator.Choose(wrapper)));
        var resources = Path.Combine(_root, "Other.APP");
        var second = Game(Path.Combine(resources, "Contents", "Resources", "drive_c", "GOG Games", "Arcanum"));
        Assert.AreEqual((GameFolderStatus.Ready, second, GameFolderOrigin.Wrapper), Triple(GameFolderLocator.Choose(resources)));

        var bottle = Path.Combine(_root, "Bottle");
        var third = Game(Path.Combine(bottle, "drive_c", "GOG Games", "Arcanum"));
        Assert.AreEqual((GameFolderStatus.Ready, third, GameFolderOrigin.Wrapper), Triple(GameFolderLocator.Choose(bottle)));
        Assert.AreEqual((GameFolderStatus.Ready, third, GameFolderOrigin.Wrapper), Triple(GameFolderLocator.Choose(Path.Combine(bottle, "drive_c"))));
        Assert.AreEqual((GameFolderStatus.Ready, third, GameFolderOrigin.Chosen), Triple(GameFolderLocator.Choose(third)));

        var empty = Directory.CreateDirectory(Path.Combine(_root, "Empty.app")).FullName;
        Assert.AreEqual((GameFolderStatus.MissingTig, empty, GameFolderOrigin.Chosen), Triple(GameFolderLocator.Choose(empty)));
        Assert.AreEqual(GameFolderStatus.MissingFolder, GameFolderLocator.Choose(Path.Combine(_root, "none.app")).Status);
    }

    private static (GameFolderStatus, string, GameFolderOrigin) Triple(GameFolderCheck check) => (check.Status, check.Path, check.Origin);
}
