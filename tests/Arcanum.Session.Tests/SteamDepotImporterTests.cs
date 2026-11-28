using Arcanum.Data.Content;
using Arcanum.Session.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-3515: finding the downloaded Steam depot and copying it into a game folder, over synthetic folders (no original
/// content, no Steam).</summary>
[TestClass]
public sealed class SteamDepotImporterTests
{
    private string _root = null!;
    private string _depot = null!;
    private string _target = null!;

    private static readonly (string Name, int Bytes)[] Files =
        [("tig.dat", 3), ("arcanum1.dat", 5), ("arcanum2.dat", 7), ("modules/Arcanum.dat", 11), ("Arcanum.exe", 13), ("data/movies/intro.bik", 17)];
    private static readonly long Total = Files.Sum(file => file.Bytes);

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-steam-import-" + Guid.NewGuid().ToString("N")));
        _depot = Path.Combine(_root, "depot");
        _target = Path.Combine(_root, "game");
        WriteDepot(_depot);
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static void WriteDepot(string folder)
    {
        foreach (var (name, bytes) in Files)
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        }
    }

    private static SteamImportOptions Options(long? minimum = null, Func<string, long?>? space = null)
        => new() { MinimumBytes = minimum ?? Total, FreeSpace = space ?? (_ => long.MaxValue), OpenArchives = _ => null };

    private Task<SteamImportResult> Import(SteamImportOptions? options = null, string? target = null, Action<SteamImportProgress>? progress = null,
        CancellationToken cancel = default)
        => new SteamDepotImporter(options ?? Options()).ImportAsync(_depot, target ?? _target, progress, cancel);

    [TestMethod]
    public void TheWindowsCandidatesAreTheRegistryFolderThenProgramFilesOnceEach()
    {
        var steam = Path.Combine(_root, "Steam");
        var programs = Path.Combine(_root, "Program Files (x86)");
        var platform = new SteamPlatform(GameFolderPlatformKind.Windows, null, programs, () => steam);
        CollectionAssert.AreEqual(new[] { Depot(steam), Depot(Path.Combine(programs, "Steam")) }, SteamDepot.Candidates(platform).ToArray());
        var same = platform with { InstallPath = () => Path.Combine(programs, "Steam") };
        Assert.AreEqual(1, SteamDepot.Candidates(same).Count);
        // No registry value, or a relative one, leaves only the default folder.
        Assert.AreEqual(1, SteamDepot.Candidates(platform with { InstallPath = () => null }).Count);
        Assert.AreEqual(1, SteamDepot.Candidates(platform with { InstallPath = () => "Steam" }).Count);
    }

    [TestMethod]
    public void TheMacCandidatesAreTheClientBundleTheDataFolderAndTheCrossOverBottles()
    {
        var bottle = Path.Combine(_root, "Library", "Application Support", "CrossOver", "Bottles", "Games");
        Directory.CreateDirectory(bottle);
        var support = Path.Combine(_root, "Library", "Application Support", "Steam");
        var candidates = SteamDepot.Candidates(Mac());
        CollectionAssert.AreEqual(new[]
        {
            Depot(Path.Combine(support, "Steam.AppBundle", "Steam", "Contents", "MacOS")),
            Depot(support),
            Depot(Path.Combine(bottle, "drive_c", "Program Files (x86)", "Steam")),
        }, candidates.ToArray());
        Assert.AreEqual(0, SteamDepot.Candidates(Mac() with { Kind = GameFolderPlatformKind.Other }).Count);
    }

    [TestMethod]
    public void FindTakesTheReadyDepotBeforeAnEarlierUnfinishedOne()
    {
        Assert.IsNull(SteamDepot.Find(Mac()));
        var candidates = SteamDepot.Candidates(Mac());
        Directory.CreateDirectory(candidates[0]);
        Assert.AreEqual(candidates[0], SteamDepot.Find(Mac()));
        WriteDepot(candidates[1]);
        Assert.AreEqual(candidates[1], SteamDepot.Find(Mac()));
    }

    [TestMethod]
    public async Task AFinishedDepotIsCopiedAndChecked()
    {
        File.WriteAllBytes(Path.Combine(_depot, ".DS_Store"), [1]);
        var steps = new List<SteamImportProgress>();
        var result = await Import(progress: steps.Add);
        Assert.AreEqual(SteamImportStatus.Imported, result.Status, result.Detail);
        Assert.AreEqual(_target, result.Folder!.Path);
        Assert.IsTrue(result.Folder.Ready);
        foreach (var (name, _) in Files)
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(_depot, name)), File.ReadAllBytes(Path.Combine(_target, name)), name);
        Assert.IsFalse(File.Exists(Path.Combine(_target, ".DS_Store")));
        Assert.AreEqual(SteamImportStage.Checking, steps[0].Stage);
        var last = steps.Last(step => step.Stage == SteamImportStage.Copying);
        Assert.AreEqual((Files.Length, Files.Length, Total, Total), (last.FilesDone, last.FilesTotal, last.BytesDone, last.BytesTotal));
        Assert.AreEqual(SteamImportStage.Identifying, steps[^1].Stage);
    }

    [TestMethod]
    public async Task AnUnfinishedDownloadIsRefusedBeforeAnythingIsWritten()
    {
        // Too few bytes.
        var small = await Import(Options(minimum: Total + 1));
        Assert.AreEqual(SteamImportStatus.Incomplete, small.Status);
        // An archive that does not open: the default check over these synthetic bytes.
        var closed = await Import(new SteamImportOptions { MinimumBytes = Total, FreeSpace = _ => long.MaxValue });
        Assert.AreEqual(SteamImportStatus.Incomplete, closed.Status, closed.Detail);
        // A file of the game is not there yet.
        File.Delete(Path.Combine(_depot, "arcanum2.dat"));
        var missing = await Import();
        Assert.AreEqual(SteamImportStatus.Incomplete, missing.Status);
        StringAssert.Contains(missing.Detail, "arcanum2.dat");
        Assert.IsFalse(Directory.Exists(_target));
    }

    [TestMethod]
    public async Task AFolderChangedDuringTheCopyIsNotAccepted()
    {
        var added = false;
        var result = await Import(progress: step =>
        {
            if (step.Stage != SteamImportStage.Copying || added) return;
            added = true;
            File.WriteAllBytes(Path.Combine(_depot, "late.bin"), [1]);
        });
        Assert.AreEqual(SteamImportStatus.SourceChanged, result.Status);
        Assert.IsNull(result.Folder);
    }

    [TestMethod]
    public async Task TheTargetMustBeNewOrEmptyAndOutsideTheDownload()
    {
        Assert.AreEqual(SteamImportStatus.InvalidPath, (await Import(target: _depot)).Status);
        Assert.AreEqual(SteamImportStatus.InvalidPath, (await Import(target: Path.Combine(_depot, "copy"))).Status);
        Assert.AreEqual(SteamImportStatus.InvalidPath, (await Import(target: Path.Combine(_root, "no", "parent"))).Status);
        Directory.CreateDirectory(_target);
        File.WriteAllBytes(Path.Combine(_target, "kept.txt"), [7]);
        Assert.AreEqual(SteamImportStatus.TargetNotEmpty, (await Import()).Status);
        File.Delete(Path.Combine(_target, "kept.txt"));
        Assert.AreEqual(SteamImportStatus.Imported, (await Import()).Status);
    }

    [TestMethod]
    public async Task AMissingFolderAndTooLittleSpaceAreNamed()
    {
        var gone = await new SteamDepotImporter(Options()).ImportAsync(Path.Combine(_root, "nothing"), _target);
        Assert.AreEqual(SteamImportStatus.SourceMissing, gone.Status);
        var full = await Import(Options(space: _ => 0));
        Assert.AreEqual(SteamImportStatus.NotEnoughSpace, full.Status);
        Assert.IsFalse(Directory.Exists(_target));
    }

    [TestMethod]
    public async Task ALinkInTheDownloadIsRefused()
    {
        if (OperatingSystem.IsWindows()) return; // creating a link needs a privilege there
        File.CreateSymbolicLink(Path.Combine(_depot, "data", "outside"), Path.Combine(_root, "outside"));
        var result = await Import();
        Assert.AreEqual(SteamImportStatus.UnsupportedSource, result.Status);
        Assert.IsFalse(Directory.Exists(_target));
    }

    [TestMethod]
    public async Task CancelStopsTheCopy()
    {
        using var cancel = new CancellationTokenSource();
        var result = await Import(progress: step => { if (step.Stage == SteamImportStage.Copying) cancel.Cancel(); }, cancel: cancel.Token);
        Assert.AreEqual(SteamImportStatus.Cancelled, result.Status);
        Assert.IsNull(result.Folder);
    }

    private SteamPlatform Mac() => new(GameFolderPlatformKind.MacOS, _root, null, () => null);

    private static string Depot(string steam) => Path.Combine(steam, "steamapps", "content", "app_500810", "depot_500811");
}
