using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-350: the HD pack source (Auto, Game folder, Custom): which folder the active pack comes from, the completeness check of
/// a cloud folder, the fallback to the installed copy and the reasons it reports. Synthetic packs in a temporary folder; no player file.</summary>
[TestClass]
public sealed class HdPackSourceTests
{
    private string _root = "";

    [TestInitialize] public void Start() => _root = Directory.CreateTempSubdirectory("arc-dev-350-").FullName;

    [TestCleanup] public void Stop() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static HdNearestPack.Frame Frame(int seed)
    {
        var rgba = new byte[3 * 2 * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7 + seed);
        for (var i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        return new(HdFrameQueries.FromArt(new string("0123456789abcdef"[seed], 64), 4096, new string("fedcba9876543210"[seed], 64), 0, 0, 0, 3, 2, rgba), rgba);
    }

    // A collection folder with one pack in it.
    private string Collection(string name, string id)
    {
        var collection = Path.Combine(_root, name);
        HdNearestPack.Write(Path.Combine(collection, id), id, 4, [Frame(1), Frame(2)]);
        return collection;
    }

    private static string PackDirectory(string collection, string id) => Path.Combine(collection, id);

    private sealed class FakeProbe(Func<string, long, HdFileState> state) : IHdFileProbe
    {
        public HdFileState Probe(string path, long expectedBytes) => state(path, expectedBytes);
    }

    // Reads the disk, and calls the files under a "cloud" folder online-only (as a file provider does before a download).
    private static IHdFileProbe Cloud(string cloudFolder) => new FakeProbe((path, bytes) =>
    {
        var info = new FileInfo(path);
        if (!info.Exists) return HdFileState.Missing;
        if (bytes >= 0 && info.Length != bytes) return HdFileState.WrongSize;
        return path.StartsWith(cloudFolder, StringComparison.Ordinal) ? HdFileState.OnlineOnly : HdFileState.Local;
    });

    private static IHdFileProbe Disk() => new HdFileProbe(_ => null);

    [TestMethod]
    public void Auto_takes_the_default_folder_when_its_pack_is_whole_and_says_nothing_was_skipped()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.DefaultLabel, choice.Source);
        Assert.AreEqual(PackDirectory(icloud, "interface-redraw-4x"), choice.Chosen!.Directory);
        Assert.AreEqual(0, choice.Skipped.Count);
        Assert.IsTrue(choice.Availability!.Ready);
        Assert.AreEqual(3, choice.Availability.Files, "two frames and the recipe");
    }

    [TestMethod]
    public void Auto_without_a_default_folder_reads_the_installed_copy_as_before()
    {
        var game = Collection("game", "installed");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, null, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        Assert.AreEqual(0, choice.Skipped.Count, "a device with no cloud folder has nothing to explain");
        Assert.AreEqual("installed", choice.Chosen!.Result.Pack!.Id);
    }

    [TestMethod]
    public void A_missing_default_folder_is_skipped_with_its_reason()
    {
        var game = Collection("game", "installed");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, Path.Combine(_root, "nothing"), null), Disk());
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        CollectionAssert.AreEqual(new[] { "iCloud copy: no pack found" }, choice.Skipped.ToArray());
    }

    [TestMethod]
    public void An_online_only_cloud_pack_falls_back_to_the_installed_copy_and_names_the_count()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), Cloud(icloud));
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        Assert.AreEqual(PackDirectory(game, "interface-redraw-4x"), choice.Chosen!.Directory);
        Assert.AreEqual(1, choice.Skipped.Count);
        StringAssert.Contains(choice.Skipped[0], "iCloud copy skipped");
        StringAssert.Contains(choice.Skipped[0], "manifest.json is online-only", "the manifest is checked before it is opened, so nothing is read");
        StringAssert.Contains(choice.Describe(), "fallback");
        StringAssert.Contains(choice.Notice(), "game folder");
    }

    [TestMethod]
    public void Frame_files_that_are_online_only_name_their_count_once_the_manifest_is_here()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        var frames = Path.Combine(PackDirectory(icloud, "interface-redraw-4x"), "frames");
        var probe = new FakeProbe((path, bytes) => path.StartsWith(frames, StringComparison.Ordinal) ? HdFileState.OnlineOnly : new FileInfo(path).Exists ? HdFileState.Local : HdFileState.Missing);
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), probe);
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        StringAssert.Contains(choice.Skipped[0], "2 of 3 files are online-only");
    }

    [TestMethod]
    public void A_recipe_that_is_online_only_is_never_opened()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        var recipe = Path.Combine(PackDirectory(icloud, "interface-redraw-4x"), "recipe.json");
        var opened = new List<string>();
        var probe = new FakeProbe((path, bytes) => { opened.Add(path); return path == recipe ? HdFileState.OnlineOnly : HdFileState.Local; });
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), probe);
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        StringAssert.Contains(choice.Skipped[0], "recipe.json is online-only");
    }

    [TestMethod]
    public void A_whole_second_pack_in_the_same_folder_is_not_missed()
    {
        var folder = Collection("cloud", "a-first");
        HdNearestPack.Write(Path.Combine(folder, "b-second"), "b-second", 4, [Frame(3)]);
        File.Delete(Directory.GetFiles(Path.Combine(folder, "a-first", "frames"), "*.png", SearchOption.AllDirectories).First());
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(Path.Combine(_root, "none"), folder, null), Disk());
        Assert.AreEqual("b-second", choice.Chosen!.Result.Pack!.Id);
        StringAssert.Contains(choice.Skipped[0], "missing");
    }

    [TestMethod]
    public void A_short_file_means_a_sync_is_running_and_the_cloud_pack_is_skipped()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        var frame = Directory.GetFiles(Path.Combine(PackDirectory(icloud, "interface-redraw-4x"), "frames"), "*.png", SearchOption.AllDirectories).First();
        File.WriteAllBytes(frame, [1, 2, 3]);
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        StringAssert.Contains(choice.Skipped[0], "1 files have another size than the manifest");
    }

    [TestMethod]
    public void A_missing_frame_file_skips_the_pack_too()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        File.Delete(Directory.GetFiles(Path.Combine(PackDirectory(icloud, "interface-redraw-4x"), "frames"), "*.png", SearchOption.AllDirectories).First());
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        StringAssert.Contains(choice.Skipped[0], "1 files are missing");
    }

    [TestMethod]
    public void A_broken_manifest_is_skipped_and_the_installed_copy_used()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var game = Collection("game", "interface-redraw-4x");
        File.WriteAllText(Path.Combine(PackDirectory(icloud, "interface-redraw-4x"), "manifest.json"), "{ not json");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(game, icloud, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.GameLabel, choice.Source);
        StringAssert.Contains(choice.Skipped[0], "none opened (Malformed)");
    }

    [TestMethod]
    public void An_online_only_pack_is_never_used_even_when_nothing_else_is_left()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(Path.Combine(_root, "empty-game"), icloud, null), Cloud(icloud));
        Assert.IsNull(choice.Chosen, "using it would make the game wait for a download");
        StringAssert.Contains(choice.Describe(), "no pack");
        StringAssert.Contains(choice.Notice(), "online-only");
    }

    [TestMethod]
    public void An_incomplete_pack_that_is_all_here_is_used_when_nothing_whole_is_left()
    {
        var game = Collection("game", "installed");
        File.Delete(Directory.GetFiles(Path.Combine(PackDirectory(game, "installed"), "frames"), "*.png", SearchOption.AllDirectories).First());
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(game, null, null), Disk());
        Assert.IsNotNull(choice.Chosen, "frames that are gone draw Legacy; nothing waits for a download");
        StringAssert.Contains(choice.Source, "incomplete");
        StringAssert.Contains(choice.Skipped[0], "missing");
    }

    [TestMethod]
    public void Game_folder_ignores_the_default_and_the_checkout_folders()
    {
        var icloud = Collection("icloud", "from-cloud");
        var game = Collection("game", "installed");
        var editor = Collection("editor", "from-checkout");
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(game, icloud, editor), Disk());
        Assert.AreEqual("installed", choice.Chosen!.Result.Pack!.Id);
        Assert.AreEqual(0, choice.Skipped.Count);
        var none = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(Path.Combine(_root, "none"), icloud, editor), Disk());
        Assert.IsNull(none.Chosen, "Game folder never reaches for another folder");
    }

    [TestMethod]
    public void Custom_takes_a_pack_folder_or_a_folder_of_packs_and_falls_back_to_the_installed_copy()
    {
        var game = Collection("game", "installed");
        var chosen = Collection("mine", "mine-4x");
        var byPack = HdPackSourceResolver.Resolve(HdPackSourceMode.Custom, PackDirectory(chosen, "mine-4x"), new(game, null, null), Disk());
        Assert.AreEqual(HdPackSourceResolver.CustomLabel, byPack.Source);
        Assert.AreEqual("mine-4x", byPack.Chosen!.Result.Pack!.Id);
        var byCollection = HdPackSourceResolver.Resolve(HdPackSourceMode.Custom, chosen, new(game, null, null), Disk());
        Assert.AreEqual("mine-4x", byCollection.Chosen!.Result.Pack!.Id);

        var gone = HdPackSourceResolver.Resolve(HdPackSourceMode.Custom, Path.Combine(_root, "gone"), new(game, null, null), Disk());
        Assert.AreEqual("installed", gone.Chosen!.Result.Pack!.Id, "the installed copy takes over");
        CollectionAssert.AreEqual(new[] { "custom folder: no pack found" }, gone.Skipped.ToArray());
        var unset = HdPackSourceResolver.Resolve(HdPackSourceMode.Custom, null, new(game, null, null), Disk());
        Assert.AreEqual("installed", unset.Chosen!.Result.Pack!.Id);
        CollectionAssert.AreEqual(new[] { "no custom folder chosen" }, unset.Skipped.ToArray());
    }

    [TestMethod]
    public void A_folder_reached_twice_by_a_link_counts_once()
    {
        var icloud = Collection("icloud", "interface-redraw-4x");
        var link = Path.Combine(_root, "local-hd-packs");
        try { Directory.CreateSymbolicLink(link, icloud); }
        catch (IOException) { Assert.Inconclusive("no symbolic links here"); }
        var choice = HdPackSourceResolver.Resolve(HdPackSourceMode.Auto, null, new(Path.Combine(_root, "none"), icloud, link), Disk());
        Assert.AreEqual(1, choice.Found, "the checkout link and the iCloud folder are one folder");
    }

    [TestMethod]
    public void The_default_folder_exists_only_on_macos_and_only_when_the_folder_is_there()
    {
        var home = Path.Combine(_root, "home");
        var folder = Path.Combine(home, "Library", "Mobile Documents", "iCloud~md~obsidian", "Documents", "Arcanum Steamwoven", "hd_pack");
        Assert.IsNull(HdPackSourceFolders.DefaultIcloudFolder(home, true, Directory.Exists), "not created yet");
        Directory.CreateDirectory(folder);
        Assert.AreEqual(folder, HdPackSourceFolders.DefaultIcloudFolder(home, true, Directory.Exists));
        Assert.IsNull(HdPackSourceFolders.DefaultIcloudFolder(home, false, Directory.Exists), "another system has no such default");
        Assert.IsNull(HdPackSourceFolders.DefaultIcloudFolder("", true, _ => true));
    }

    [TestMethod]
    public void The_problem_check_tells_only_a_player_who_wants_hd_and_drops_a_pack_that_cannot_be_used()
    {
        var game = Collection("game", "installed");
        var whole = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(game, null, null), Disk());
        var pack = whole.Chosen!.Result.Pack;
        Assert.IsNull(HdPackProblem.Check(false, false, null, "none", whole).Message, "Legacy graphics and HD interface off: nothing to tell");
        Assert.IsNull(HdPackProblem.Check(true, false, pack, "Opened", whole).Message, "a whole pack is no problem");

        var none = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(Path.Combine(_root, "none"), null, null), Disk());
        var missing = HdPackProblem.Check(true, false, null, "none", none);
        Assert.IsTrue(missing.DropPack);
        StringAssert.Contains(missing.Message, "Legacy");
        StringAssert.Contains(missing.Message, "no pack");
        var environment = HdPackProblem.Check(true, true, null, "NotFound", null);
        StringAssert.Contains(environment.Message, "ARC_HD_PACK did not open (NotFound)");

        File.Delete(Directory.GetFiles(Path.Combine(PackDirectory(game, "installed"), "frames"), "*.png", SearchOption.AllDirectories).First());
        var incomplete = HdPackSourceResolver.Resolve(HdPackSourceMode.GameFolder, null, new(game, null, null), Disk());
        var partial = HdPackProblem.Check(true, false, incomplete.Chosen!.Result.Pack, "Opened", incomplete);
        Assert.IsFalse(partial.DropPack, "an incomplete pack that is all here is still used; its missing frames draw Legacy");
        StringAssert.Contains(partial.Message, "incomplete");
    }

    [TestMethod]
    public void The_real_probe_reads_length_placeholders_and_the_dataless_listing()
    {
        var folder = Path.Combine(_root, "files"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.png"), new byte[5]);
        File.WriteAllBytes(Path.Combine(folder, "b.png"), new byte[7]);
        File.WriteAllBytes(Path.Combine(folder, ".c.png.icloud"), new byte[1]);
        var listing = "total 2\n-rw-r--r--  1 u  staff  -        5 Nov 10 21:08 a.png\n-rw-r--r--@ 1 u  staff  dataless 7 Nov 10 21:08 b.png\n";
        var probe = new HdFileProbe(_ => listing);
        Assert.AreEqual(HdFileState.Local, probe.Probe(Path.Combine(folder, "a.png"), 5));
        Assert.AreEqual(HdFileState.WrongSize, probe.Probe(Path.Combine(folder, "a.png"), 6));
        Assert.AreEqual(HdFileState.OnlineOnly, probe.Probe(Path.Combine(folder, "b.png"), 7), "the file provider keeps it in the cloud");
        Assert.AreEqual(HdFileState.OnlineOnly, probe.Probe(Path.Combine(folder, "c.png"), 1), "an old-style placeholder stands for the file");
        Assert.AreEqual(HdFileState.Missing, probe.Probe(Path.Combine(folder, "d.png"), 1));
        Assert.AreEqual(HdFileState.Local, new HdFileProbe(_ => null).Probe(Path.Combine(folder, "b.png"), -1), "a local folder whose listing cannot run: the size check alone");
        var combined = new HdFileProbe(_ => "-rw-r--r--@ 1 u  staff  hidden,dataless 7 Nov 10 21:08 b.png\n");
        Assert.AreEqual(HdFileState.OnlineOnly, combined.Probe(Path.Combine(folder, "b.png"), 7), "flags are joined with commas");
        Assert.AreEqual(HdFileState.OnlineOnly, combined.Probe(Path.Combine(folder, "b.png"), 9), "dataless is read before the size: a cloud file may report another length");
        Assert.IsTrue(HdFileProbe.IsCloudPath("/Users/x/Library/Mobile Documents/iCloud~md~obsidian/Documents/a/b.png"));
        Assert.IsTrue(HdFileProbe.IsCloudPath("/Users/x/Library/CloudStorage/GoogleDrive-a/b.png"));
        Assert.IsFalse(HdFileProbe.IsCloudPath(folder));
    }
}
