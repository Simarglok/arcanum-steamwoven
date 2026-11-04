using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Executable.Maps;
using Arcanum.Executable.Prototypes;
using Arcanum.Session.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-069: the first-start prototype provision over synthetic installations and a stand-in for the builder (no original
/// content, no interpreter); the real executable runs only in the opt-in test at the end.</summary>
[TestClass]
public sealed class PrototypeProvisionerTests
{
    private static readonly byte[] ExeBytes = Enumerable.Range(0, 21).Select(i => (byte)(i * 3 + 1)).ToArray();
    private static readonly string ExeSha = Convert.ToHexString(SHA256.HashData(ExeBytes)).ToLowerInvariant();
    private static readonly byte[] Rules = Encoding.ASCII.GetBytes("rules v1");

    private string _root = null!;
    private string _game = null!;
    private string _cache = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-protoprov-" + Guid.NewGuid().ToString("N")));
        _game = Path.Combine(_root, "game");
        _cache = Path.Combine(_root, "settings", "prototypes");
        Directory.CreateDirectory(Path.Combine(_game, "modules"));
        foreach (var name in new[] { "tig.dat", "arcanum1.dat", "arcanum2.dat", "modules/Arcanum.dat" }) File.WriteAllBytes(Path.Combine(_game, name), EmptyDat());
        File.WriteAllBytes(Path.Combine(_game, "Arcanum.exe"), ExeBytes);
        Write("data/rules/npc.txt", Rules);
        PrototypeCacheMount.Use(null);
    }

    [TestCleanup]
    public void Remove()
    {
        PrototypeCacheMount.Use(null);
        Directory.Delete(_root, recursive: true);
    }

    private static byte[] EmptyDat()
    {
        // The index (a zero count), 16 bytes of padding, the "1TAD" tag, a zero and the index length plus 28.
        using var stream = new MemoryStream();
        void I32(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); stream.Write(bytes); }
        I32(0); stream.Write(new byte[16]); stream.Write("1TAD"u8); I32(0); I32(4 + 28);
        return stream.ToArray();
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_game, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private GameFolderCheck Folder()
    {
        var check = GameFolderLocator.Check(_game);
        Assert.IsTrue(check.Ready, check.Status + " " + check.Detail);
        return check;
    }

    private static byte[] Bytes(int number) => Encoding.ASCII.GetBytes("prototype " + number);

    /// <summary>A stand-in for the builder: three files, the data file the executable read and one it looked for.</summary>
    private sealed class FakeBuild
    {
        public int Calls;
        public string[] Names { get; init; } = ["001001 - Wall.pro", "001002 - Wall.pro", "005678 - Auto Loading Mechanism.pro"];
        public PrototypeBuildFailure? Failure { get; init; }
        public byte[]? SeenExe;

        public PrototypeBuildResult Run(byte[] exe, IOriginalFileSource files, Action<int, int>? progress)
        {
            Calls++;
            SeenExe = exe;
            Assert.IsNotNull(files.Read("rules\\npc.txt"), "the executable's data files come from the installation");
            var built = new List<BuiltPrototype>();
            for (var i = 0; i < Names.Length; i++)
            {
                progress?.Invoke(i + 1, Names.Length);
                built.Add(new(int.TryParse(Names[i].AsSpan(0, Math.Min(6, Names[i].Length)), out var number) ? number - 20 : 0, Names[i], Bytes(i)));
            }
            return new(built, Failure, 5) { ReadFiles = ["rules\\npc.txt"], MissedFiles = ["rules\\absent.mes"], ListedPatterns = ["art\\tile\\*.art", "proto\\*.pro"] };
        }
    }

    private PrototypeProvisionOptions Options(FakeBuild build, int installed = 3) => new() { InstalledFileCount = installed, ExpectedExeSha256 = ExeSha, Build = build.Run };

    [TestMethod]
    public void A_fresh_installation_builds_stores_and_later_starts_reuse_the_files()
    {
        var build = new FakeBuild();
        var reports = new List<PrototypeProvisionProgress>();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, reports.Add, options: Options(build));

        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);
        Assert.IsTrue(first.Succeeded);
        Assert.AreEqual(3, first.Files);
        Assert.AreEqual(1, build.Calls);
        CollectionAssert.AreEqual(ExeBytes, build.SeenExe, "the player's executable bytes");
        Assert.AreEqual(PrototypeProvisionStage.Checking, reports[0].Stage);
        CollectionAssert.AreEqual(new[] { (1, 3), (2, 3), (3, 3) }, reports.Where(r => r.Stage == PrototypeProvisionStage.Building && r.Done > 0).Select(r => (r.Done, r.Total)).ToArray());
        Assert.AreEqual(PrototypeProvisionStage.Storing, reports[^1].Stage);
        Assert.IsTrue(first.MountFolder!.StartsWith(Path.GetFullPath(_cache), StringComparison.Ordinal), "inside the settings folder, never the game folder");
        Assert.IsFalse(Directory.Exists(Path.Combine(_game, "data", "proto")), "the game folder is only read");
        CollectionAssert.AreEqual(Bytes(0), File.ReadAllBytes(Path.Combine(first.MountFolder, "proto", "001001 - Wall.pro")));

        var again = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Cached, again.Status, again.Detail);
        Assert.AreEqual(first.MountFolder, again.MountFolder);
        Assert.AreEqual(1, build.Calls, "a later start does not build again");
    }

    [TestMethod]
    public void The_mounted_cache_sits_over_the_archives_and_under_data_so_a_real_data_proto_file_wins()
    {
        var build = new FakeBuild();
        var result = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        PrototypeCacheMount.Use(result.MountFolder);
        Write("data/proto/001001 - Wall.pro", Encoding.ASCII.GetBytes("the original's own"));

        var discovery = ContentMountPlan.Discover(_game);
        var roles = discovery.Plan!.SearchOrder.Select(source => source.Role).ToArray();
        CollectionAssert.AreEqual(new[] { ContentSourceRole.DataDirectory, ContentSourceRole.PrototypeCache }, roles.Where(role => role is ContentSourceRole.DataDirectory or ContentSourceRole.PrototypeCache).ToArray(),
            "highest priority first: data/ then the cache");
        var opened = ContentStack.Open(discovery.Plan);
        using var stack = opened.Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        CollectionAssert.AreEqual(new[] { 1001, 1002, 5678 }, prototypes.Numbers.ToArray());
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("the original's own"), prototypes.Read(1001).Bytes, "data/proto wins");
        CollectionAssert.AreEqual(Bytes(1), prototypes.Read(1002).Bytes, "the cache holds the rest");
        Assert.AreEqual(ContentSourceRole.PrototypeCache, prototypes.Candidate(1002)!.Source.Role);

        // A cache that was mounted before is not part of what the installation has: the next provision still sees none of its own.
        var folder = GameFolderLocator.Check(_game);
        Assert.IsTrue(folder.Plan!.SearchOrder.Any(source => source.Role == ContentSourceRole.PrototypeCache));
        File.Delete(Path.Combine(_game, "data", "proto", "001001 - Wall.pro"));
        var again = PrototypeProvisioner.Provision(folder, _cache, options: Options(build, installed: 3));
        Assert.AreEqual(PrototypeProvisionStatus.Cached, again.Status, again.Detail);
    }

    [TestMethod]
    public void An_installation_with_its_own_prototype_files_is_left_alone()
    {
        for (var i = 1; i <= 3; i++) Write($"data/proto/00100{i} - Wall.pro", Bytes(i));
        var build = new FakeBuild();
        var result = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Present, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.MountFolder);
        Assert.AreEqual(0, build.Calls);
        Assert.IsFalse(Directory.Exists(_cache), "nothing is written");
    }

    [TestMethod]
    public void Changed_data_a_damaged_or_extra_file_or_a_new_executable_build_again()
    {
        var build = new FakeBuild();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);

        Write("data/rules/npc.txt", Encoding.ASCII.GetBytes("rules v2"));
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a changed data file");
        Assert.AreEqual(2, build.Calls);
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status);

        Write("data/rules/absent.mes", [1]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a file the executable looked for appeared");
        File.Delete(Path.Combine(_game, "data", "rules", "absent.mes"));
        Assert.AreEqual(3, build.Calls);

        var proto = Path.Combine(first.MountFolder!, "proto");
        File.WriteAllBytes(Path.Combine(proto, "001001 - Wall.pro"), [9, 9, 9]);
        var calls = build.Calls;
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a damaged file");
        Assert.AreEqual(calls + 1, build.Calls);

        File.WriteAllBytes(Path.Combine(proto, "009999 - Extra.pro"), [1]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "an extra file would be mounted too");
        File.Delete(Path.Combine(proto, "001002 - Wall.pro"));
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a missing file");
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status);

        // Another executable has its own cache folder; the first one stays.
        var other = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(Path.Combine(_game, "Arcanum.exe"), other);
        var otherSha = Convert.ToHexString(SHA256.HashData(other)).ToLowerInvariant();
        var second = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build) with { ExpectedExeSha256 = otherSha });
        Assert.AreEqual(PrototypeProvisionStatus.Built, second.Status, second.Detail);
        Assert.AreNotEqual(first.MountFolder, second.MountFolder);
        Assert.IsTrue(Directory.Exists(first.MountFolder), "the earlier executable's cache is kept");
    }

    [TestMethod]
    public void A_file_added_to_a_folder_the_executable_searched_or_a_system_file_in_the_cache_is_judged_right()
    {
        Write("data/art/tile/a.art", [1]);
        var build = new FakeBuild();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);

        // A name the system adds (Finder, a copy tool) is no file of the cache.
        File.WriteAllBytes(Path.Combine(first.MountFolder!, ".DS_Store"), [1]);
        File.WriteAllBytes(Path.Combine(first.MountFolder!, "proto", "._001001 - Wall.pro"), [1]);
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status);
        Assert.AreEqual(1, build.Calls);

        Write("data/art/tile/b.art", [2]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a new file in a searched folder");
        Assert.AreEqual(2, build.Calls);
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status);
    }

    [TestMethod]
    public void A_cancel_during_the_check_of_a_stored_cache_or_after_the_build_still_cancels()
    {
        var build = new FakeBuild();
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status);
        using var cancel = new CancellationTokenSource();
        var cached = PrototypeProvisioner.Provision(Folder(), _cache, progress => { if (progress.Stage == PrototypeProvisionStage.Checking) cancel.Cancel(); }, cancel.Token, Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Cancelled, cached.Status, "cancelled before the cache is verified");
        using var late = new CancellationTokenSource();
        Write("data/rules/npc.txt", Encoding.ASCII.GetBytes("rules v9"));
        var stored = PrototypeProvisioner.Provision(Folder(), _cache, progress => { if (progress.Stage == PrototypeProvisionStage.Storing) late.Cancel(); }, late.Token, Options(build));
        Assert.AreEqual(PrototypeProvisionStatus.Cancelled, stored.Status, "a cancel while saving is not ignored");
    }

    [TestMethod]
    public void An_executable_that_is_not_the_verified_one_or_missing_is_refused_without_building()
    {
        var build = new FakeBuild();
        var wrong = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build) with { ExpectedExeSha256 = new string('a', 64) });
        Assert.AreEqual(PrototypeProvisionStatus.UnverifiedExecutable, wrong.Status);
        Assert.IsFalse(wrong.Succeeded);
        File.Delete(Path.Combine(_game, "Arcanum.exe"));
        Assert.AreEqual(PrototypeProvisionStatus.NoExecutable, PrototypeProvisioner.Provision(GameFolderLocator.Check(_game), _cache, options: Options(build)).Status);
        Assert.AreEqual(0, build.Calls);
        Assert.IsFalse(Directory.Exists(_cache));
        Assert.AreEqual(PrototypeProvisionStatus.SourceFailed, PrototypeProvisioner.Provision(GameFolderLocator.Check(Path.Combine(_root, "none")), _cache, options: Options(build)).Status);
    }

    [TestMethod]
    public void A_cancelled_build_leaves_no_cache()
    {
        using var cancel = new CancellationTokenSource();
        var result = PrototypeProvisioner.Provision(Folder(), _cache, progress => { if (progress is { Stage: PrototypeProvisionStage.Building, Done: 2 }) cancel.Cancel(); }, cancel.Token, Options(new FakeBuild()));
        Assert.AreEqual(PrototypeProvisionStatus.Cancelled, result.Status);
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(!Directory.Exists(_cache) || !Directory.EnumerateFileSystemEntries(_cache).Any(), "nothing stored");
    }

    [TestMethod]
    public void A_failed_build_or_unstorable_files_are_named_and_store_nothing()
    {
        var failed = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(new FakeBuild { Failure = new(PrototypeBuildFailureKind.BuildFault, "at 0x468930") }));
        Assert.AreEqual(PrototypeProvisionStatus.BuildFailed, failed.Status);
        StringAssert.Contains(failed.Detail, "0x468930");
        var missing = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(new FakeBuild { Failure = new(PrototypeBuildFailureKind.MissingSourceFile, "rules\\npc.txt") }));
        Assert.AreEqual(PrototypeProvisionStatus.SourceFailed, missing.Status);
        foreach (var name in new[] { "1001 - Wall.pro", "001001 - ..\\x.pro", "001001 - a/b.pro", "001001 - Wall.txt", "001001 - Wall.pro\0" })
            Assert.AreEqual(PrototypeProvisionStatus.CacheWriteFailed, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(new FakeBuild { Names = [name] }, 1)).Status, name);
        Assert.AreEqual(PrototypeProvisionStatus.CacheWriteFailed, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(new FakeBuild { Names = ["001001 - Wall.pro", "001001 - WALL.pro"] })).Status, "two names that differ only in case");
        Assert.IsTrue(!Directory.Exists(_cache) || !Directory.EnumerateFileSystemEntries(_cache).Any());
    }

    [TestMethod]
    public void A_settings_folder_that_cannot_be_written_is_a_typed_failure()
    {
        File.WriteAllText(Path.Combine(_root, "blocker"), "a file where the folder should be");
        var result = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "blocker", "prototypes"), options: Options(new FakeBuild()));
        Assert.AreEqual(PrototypeProvisionStatus.CacheWriteFailed, result.Status);
        Assert.IsNotNull(result.Detail);
    }

    [TestMethod]
    public void Leftovers_of_a_start_that_died_are_removed_after_a_while_and_a_manifest_alone_is_no_cache()
    {
        var build = new FakeBuild();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        var tmp = first.MountFolder![..first.MountFolder!.LastIndexOf(Path.DirectorySeparatorChar)] + ".tmp-dead";
        var recent = first.MountFolder![..first.MountFolder!.LastIndexOf(Path.DirectorySeparatorChar)] + ".old-recent";
        Directory.CreateDirectory(tmp); Directory.CreateDirectory(recent);
        Directory.SetLastWriteTimeUtc(tmp, DateTime.UtcNow - TimeSpan.FromDays(2));
        PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build));
        Assert.IsFalse(Directory.Exists(tmp), "an old temporary folder is removed");
        Assert.IsTrue(Directory.Exists(recent), "a recent one may belong to a running start");

        Directory.Delete(first.MountFolder, recursive: true);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: Options(build)).Status, "a manifest without its files");
    }

    [TestMethod]
    public void The_mount_holder_takes_full_paths_only()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PrototypeCacheMount.Use("relative/folder"));
        Assert.ThrowsExactly<ArgumentException>(() => PrototypeCacheMount.Use(Path.Combine(_root, "a\nb")));
        PrototypeCacheMount.Use(_root + Path.DirectorySeparatorChar);
        Assert.AreEqual(_root, PrototypeCacheMount.Folder);
        PrototypeCacheMount.Use(null);
        Assert.IsNull(PrototypeCacheMount.Folder);
        Assert.IsFalse(ContentMountPlan.Discover(_game).Plan!.SearchOrder.Any(source => source.Role == ContentSourceRole.PrototypeCache));
        PrototypeCacheMount.Use(Path.Combine(_root, "not-there"));
        Assert.IsFalse(ContentMountPlan.Discover(_game).Plan!.SearchOrder.Any(source => source.Role == ContentSourceRole.PrototypeCache), "a folder that does not exist is not mounted");
    }

    // --- ARC-DEV-082: the map aggregates ---------------------------------------------------------------------------------------

    /// <summary>A stand-in for the aggregate build: one file per map folder the source lists, named by the original's rule and holding the map's name.</summary>
    private sealed class FakeMaps
    {
        public int Calls;
        public byte[]? SeenGuid, SeenExe;
        public string[] SeenFolders = [];
        public PrototypeBuildFailure? Failure { get; init; }
        public Func<IOriginalFileSource, bool>? Check { get; init; }
        public bool Rename { get; init; }

        public MapAggregateRunResult Run(byte[] exe, IOriginalFileSource files, byte[] guid, Action<int, int>? progress)
        {
            Calls++;
            SeenExe = exe;
            SeenGuid = guid;
            SeenFolders = [.. files.ListDirectories("maps")];
            if (Check is not null) Assert.IsTrue(Check(files));
            var built = new List<BuiltMapAggregate>();
            for (var i = 0; i < SeenFolders.Length; i++)
            {
                progress?.Invoke(i, SeenFolders.Length);
                built.Add(new(SeenFolders[i], Rename ? "wrong" : WorldCatalog.AggregateName(SeenFolders[i]), [.. guid, .. Encoding.ASCII.GetBytes(SeenFolders[i])]));
            }
            progress?.Invoke(SeenFolders.Length, SeenFolders.Length);
            return new(built, Failure, 7);
        }
    }

    private PrototypeProvisionOptions MapOptions(FakeBuild build, FakeMaps maps) => Options(build) with { BuildMaps = maps.Run };

    private void Maps(params string[] names)
    {
        foreach (var name in names) Write($"modules/Arcanum/maps/{name}/map.prp", new byte[24]);
    }

    [TestMethod]
    public void A_fresh_installation_gets_its_map_aggregates_built_stored_beside_the_prototypes_and_reused()
    {
        Maps("Arcanum1-024", "Shop Map");
        var build = new FakeBuild(); var maps = new FakeMaps();
        var reports = new List<PrototypeProvisionProgress>();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, reports.Add, options: MapOptions(build, maps));

        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);
        Assert.AreEqual(3, first.Files);
        Assert.AreEqual(2, first.MapFiles);
        Assert.AreEqual(1, build.Calls); Assert.AreEqual(1, maps.Calls);
        CollectionAssert.AreEqual(ExeBytes, maps.SeenExe);
        CollectionAssert.AreEqual(new byte[12].Concat("1TAD"u8.ToArray()).ToArray(), maps.SeenGuid, "the 16 bytes the original reads from 24 bytes before the module archive's end (the last four are the tag)");
        CollectionAssert.AreEqual(new[] { "Arcanum1-024", "Shop Map" }, maps.SeenFolders, "the folders with the case their files spell");
        Assert.IsTrue(reports.Any(r => r.Stage == PrototypeProvisionStage.BuildingMaps && r.Done == 2 && r.Total == 2));
        var mapsFolder = Path.Combine(first.MountFolder!, "maps");
        CollectionAssert.AreEquivalent(new[] { WorldCatalog.AggregateName("Arcanum1-024"), WorldCatalog.AggregateName("Shop Map") }, Directory.EnumerateFiles(mapsFolder).Select(Path.GetFileName).ToArray());
        Assert.IsTrue(Directory.Exists(Path.Combine(first.MountFolder!, "proto")), "the prototypes are in the same cache");
        Assert.IsFalse(File.Exists(Path.Combine(_game, "modules", "Arcanum", "maps", WorldCatalog.AggregateName("Shop Map"))), "the game folder is only read");

        var again = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Cached, again.Status, again.Detail);
        Assert.AreEqual(2, again.MapFiles);
        Assert.AreEqual(1, build.Calls); Assert.AreEqual(1, maps.Calls, "a later start builds neither");

        // The stored aggregates are found by the world catalogue's own name rule once the cache is mounted: the stack sees them.
        PrototypeCacheMount.Use(again.MountFolder);
        var opened = ContentStack.Open(ContentMountPlan.Discover(_game).Plan!);
        using var stack = opened.Stack!;
        Assert.AreEqual(0, PrototypeProvisioner.MissingAggregates(stack), "with the cache mounted no aggregate is missing");
        Assert.IsNotNull(ContentPhase.Candidate(stack, "maps/" + WorldCatalog.AggregateName("Shop Map"), ContentLoadPhase.ModuleLoaded));
    }

    [TestMethod]
    public void An_installation_whose_aggregates_exist_builds_none_and_one_with_its_own_prototypes_builds_only_the_maps()
    {
        Maps("One", "Two");
        Write("modules/Arcanum/maps/" + WorldCatalog.AggregateName("One"), [1]);
        Write("modules/Arcanum/maps/" + WorldCatalog.AggregateName("Two"), [2]);
        for (var i = 1; i <= 3; i++) Write($"data/proto/00100{i} - Wall.pro", Bytes(i));
        var build = new FakeBuild(); var maps = new FakeMaps();
        var present = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Present, present.Status);
        Assert.IsNull(present.MountFolder);
        Assert.AreEqual(0, maps.Calls);
        Assert.IsFalse(Directory.Exists(_cache), "nothing is written");

        File.Delete(Path.Combine(_game, "modules", "Arcanum", "maps", WorldCatalog.AggregateName("Two")));
        var part = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Built, part.Status, part.Detail);
        Assert.AreEqual(0, build.Calls, "the installation's prototypes are used");
        Assert.AreEqual(1, maps.Calls);
        Assert.AreEqual(2, part.MapFiles, "every map is built; the installation's own file still wins where there is one");
        Assert.IsFalse(Directory.Exists(Path.Combine(part.MountFolder!, "proto")));
        var again = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Cached, again.Status, again.Detail);
        Assert.AreEqual(1, maps.Calls);
    }

    [TestMethod]
    public void The_map_build_sees_the_prototypes_of_the_cache_the_same_start_made_and_a_changed_module_or_a_damaged_file_builds_again()
    {
        Maps("One");
        var build = new FakeBuild();
        var maps = new FakeMaps { Check = files => files.Read("proto\\001001 - Wall.pro") is not null && files.List("proto").Count == 3 };
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);
        Assert.AreEqual(1, maps.Calls);

        var file = Path.Combine(first.MountFolder!, "maps", WorldCatalog.AggregateName("One"));
        File.WriteAllBytes(file, [9, 9]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "a damaged file");
        Assert.AreEqual(2, maps.Calls); Assert.AreEqual(1, build.Calls, "the prototypes verified");
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status);

        File.WriteAllBytes(Path.Combine(first.MountFolder!, "maps", "extra"), [1]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "an extra file would be mounted too");
        File.WriteAllBytes(Path.Combine(first.MountFolder!, "maps", ".DS_Store"), [1]);
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "a system file is not one");
        Assert.AreEqual(3, maps.Calls);

        // Another module archive (here a longer file) is another module: its aggregates are built again.
        File.WriteAllBytes(Path.Combine(_game, "modules", "Arcanum.dat"), [.. EmptyDat(), .. new byte[8]]);
        Assert.IsNull(PrototypeProvisioner.ModuleGuid(Folder().Plan!), "an archive that does not end with the footer is not read");
        File.WriteAllBytes(Path.Combine(_game, "modules", "Arcanum.dat"), EmptyDat());
        File.WriteAllBytes(Path.Combine(_game, "modules", "Arcanum.PATCH0"), EmptyDat());
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "a patch archive appeared");
        Assert.AreEqual(4, maps.Calls);
    }

    [TestMethod]
    public void A_replaced_prototype_cache_drops_the_aggregates_and_a_failed_map_build_is_named_and_stores_nothing()
    {
        Maps("One");
        var build = new FakeBuild(); var maps = new FakeMaps();
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status);
        Write("data/rules/npc.txt", Encoding.ASCII.GetBytes("rules v2"));
        var second = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Built, second.Status, second.Detail);
        Assert.AreEqual(2, build.Calls); Assert.AreEqual(2, maps.Calls, "new prototypes: the maps are built over them again");

        var failed = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "settings", "failed"), options: MapOptions(new FakeBuild(), new FakeMaps { Failure = new(PrototypeBuildFailureKind.BuildFault, "at 0x4102F0") }));
        Assert.AreEqual(PrototypeProvisionStatus.BuildFailed, failed.Status);
        StringAssert.Contains(failed.Detail, "0x4102F0");
        StringAssert.Contains(failed.Detail, "map files");
        var missing = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "settings", "failed2"), options: MapOptions(new FakeBuild(), new FakeMaps { Failure = new(PrototypeBuildFailureKind.MissingSourceFile, "mes\\x.mes") }));
        Assert.AreEqual(PrototypeProvisionStatus.SourceFailed, missing.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "settings", "failed", "prototypes-" + ExeSha[..16], "files", "maps")), "a failed build stores no map files");

        using var cancel = new CancellationTokenSource();
        var cancelled = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "settings", "cancel"), progress => { if (progress.Stage == PrototypeProvisionStage.BuildingMaps) cancel.Cancel(); },
            cancel.Token, MapOptions(new FakeBuild(), new FakeMaps()));
        Assert.AreEqual(PrototypeProvisionStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    public void The_map_build_needs_the_verified_executable_even_when_the_installation_has_its_own_prototypes()
    {
        Maps("One");
        for (var i = 1; i <= 3; i++) Write($"data/proto/00100{i} - Wall.pro", Bytes(i));
        var maps = new FakeMaps();
        var wrong = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(new FakeBuild(), maps) with { ExpectedExeSha256 = new string('a', 64) });
        Assert.AreEqual(PrototypeProvisionStatus.UnverifiedExecutable, wrong.Status, "own prototypes do not make the executable unnecessary: the maps are built from it");
        Assert.AreEqual(0, maps.Calls);
    }

    [TestMethod]
    public void A_failed_map_build_of_an_installation_with_prototypes_is_named_and_a_misnamed_file_is_refused()
    {
        Maps("One");
        for (var i = 1; i <= 3; i++) Write($"data/proto/00100{i} - Wall.pro", Bytes(i));
        var failed = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(new FakeBuild(), new FakeMaps { Failure = new(PrototypeBuildFailureKind.BuildFault, "at 0x4102F0") }));
        Assert.AreEqual(PrototypeProvisionStatus.Present, failed.Status, failed.Detail);
        Assert.AreEqual(3, failed.Files);
        StringAssert.Contains(failed.Detail, "0x4102F0");

        var misnamed = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "settings", "misnamed"), options: MapOptions(new FakeBuild(), new FakeMaps { Rename = true }));
        Assert.AreEqual(PrototypeProvisionStatus.Present, misnamed.Status, "own prototypes: not fatal");
        StringAssert.Contains(misnamed.Detail, "another name");
        var fresh = PrototypeProvisioner.Provision(Folder(), Path.Combine(_root, "settings", "misnamed2"), options: MapOptions(new FakeBuild(), new FakeMaps { Rename = true }) with { InstalledFileCount = 99 });
        Assert.AreEqual(PrototypeProvisionStatus.BuildFailed, fresh.Status, "a fresh installation is refused");
        StringAssert.Contains(fresh.Detail, "another name");
    }

    [TestMethod]
    public void A_prototype_copy_in_the_cache_is_dropped_once_the_installation_has_its_own_and_loose_map_files_change_the_identity()
    {
        Maps("One");
        var build = new FakeBuild(); var maps = new FakeMaps();
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Built, first.Status, first.Detail);
        Assert.IsTrue(Directory.Exists(Path.Combine(first.MountFolder!, "proto")));

        for (var i = 1; i <= 3; i++) Write($"data/proto/00100{i} - Wall.pro", Bytes(i));
        var own = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps));
        Assert.AreEqual(PrototypeProvisionStatus.Cached, own.Status, own.Detail);
        Assert.IsFalse(Directory.Exists(Path.Combine(own.MountFolder!, "proto")), "an older copy would shadow the installation's own files");

        Write("modules/Arcanum/maps/One/extra.mob", new byte[4]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "a loose map file the build read changed");
        Write("data/maps/One/loose.mob", new byte[4]);
        Assert.AreEqual(PrototypeProvisionStatus.Built, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(build, maps)).Status, "a loose map file under data/");
    }

    [TestMethod]
    public void Aggregate_names_that_are_windows_device_names_are_refused_and_dead_map_temporaries_are_removed()
    {
        Assert.IsTrue(PrototypeCacheStore.ValidAggregateName("Abc"));
        foreach (var bad in new[] { "CON", "nul", "Aux.txt", "COM1", "lpt9.x", "", ".x", "a/b" }) Assert.IsFalse(PrototypeCacheStore.ValidAggregateName(bad), bad);
        Assert.IsTrue(PrototypeCacheStore.ValidAggregateName("COMX"));

        Maps("One");
        var first = PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(new FakeBuild(), new FakeMaps()));
        var folder = Path.GetDirectoryName(first.MountFolder!)!;
        var deadMaps = Path.Combine(folder, "maps.tmp-dead"); var deadManifest = Path.Combine(folder, "aggregates.json.tmp-dead");
        Directory.CreateDirectory(deadMaps); File.WriteAllBytes(deadManifest, [1]);
        var old = DateTime.UtcNow - PrototypeCacheStore.LeftoverAge - TimeSpan.FromHours(1);
        Directory.SetLastWriteTimeUtc(deadMaps, old); File.SetLastWriteTimeUtc(deadManifest, old);
        Assert.AreEqual(PrototypeProvisionStatus.Cached, PrototypeProvisioner.Provision(Folder(), _cache, options: MapOptions(new FakeBuild(), new FakeMaps())).Status);
        Assert.IsFalse(Directory.Exists(deadMaps)); Assert.IsFalse(File.Exists(deadManifest));
    }

    /// <summary>Opt-in (<c>ARCANUM_GAME_ROOT</c>, an installation the original ran; <c>ARCANUM_EXE</c> is not needed: its own <c>Arcanum.exe</c> is read):
    /// a copy of the installation made of links, without <c>data/proto</c>, is a fresh one: the provision builds the 1,423 files into a cache in a
    /// temporary settings folder, a second one verifies and reuses them, and the cache equals the installation's own files by name and length.
    /// The installation is only read.</summary>
    [TestMethod, TestCategory("OriginalExe")]
    public void A_fresh_copy_of_the_players_installation_builds_the_files_the_original_wrote()
    {
        var real = Environment.GetEnvironmentVariable("ARCANUM_GAME_ROOT");
        if (string.IsNullOrEmpty(real) || !Directory.Exists(real)) Assert.Inconclusive("ARCANUM_GAME_ROOT is not set");
        var farm = Path.Combine(_root, "farm");
        Directory.CreateDirectory(farm);
        foreach (var entry in Directory.EnumerateFileSystemEntries(real))
            if (!string.Equals(Path.GetFileName(entry), "data", StringComparison.OrdinalIgnoreCase)) Directory.CreateSymbolicLink(Path.Combine(farm, Path.GetFileName(entry)), entry);
        var realData = Path.Combine(real, "data");
        Directory.CreateDirectory(Path.Combine(farm, "data"));
        if (Directory.Exists(realData))
            foreach (var entry in Directory.EnumerateFileSystemEntries(realData))
                if (!string.Equals(Path.GetFileName(entry), "proto", StringComparison.OrdinalIgnoreCase)) Directory.CreateSymbolicLink(Path.Combine(farm, "data", Path.GetFileName(entry)), entry);
        // The module folder without its maps: the aggregates are made at the first start too.
        var realModules = Path.Combine(real, "modules");
        var farmModules = Path.Combine(farm, "modules");
        Directory.Delete(farmModules); // a link to the whole folder, replaced by a folder of links
        Directory.CreateDirectory(farmModules);
        foreach (var entry in Directory.EnumerateFileSystemEntries(realModules))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue;
            if (!Directory.Exists(entry) || !string.Equals(name, "Arcanum", StringComparison.OrdinalIgnoreCase)) { Directory.CreateSymbolicLink(Path.Combine(farmModules, name), entry); continue; }
            Directory.CreateDirectory(Path.Combine(farmModules, name));
            foreach (var inner in Directory.EnumerateFileSystemEntries(entry))
                if (!string.Equals(Path.GetFileName(inner), "maps", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(inner).StartsWith('.'))
                    Directory.CreateSymbolicLink(Path.Combine(farmModules, name, Path.GetFileName(inner)), inner);
        }
        var check = GameFolderLocator.Check(farm);
        Assert.IsTrue(check.Ready, check.Status + " " + check.Detail);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = (PrototypeProvisionProgress?)null; var stages = new List<PrototypeProvisionStage>();
        var built = PrototypeProvisioner.Provision(check, _cache, report => { last = report; if (stages.Count == 0 || stages[^1] != report.Stage) stages.Add(report.Stage); });
        Console.WriteLine($"provision: {built.Status} {built.Files} files and {built.MapFiles} map files in {clock.Elapsed.TotalSeconds:F1} s, stages {string.Join(">", stages)}, last {last}");
        Assert.AreEqual(PrototypeProvisionStatus.Built, built.Status, built.Detail);
        Assert.AreEqual(PrototypeProvisioner.InstalledFileCount, built.Files);
        Assert.IsTrue(built.MapFiles > 0, "the module's map aggregates are built too");
        clock.Restart();
        var cached = PrototypeProvisioner.Provision(check, _cache);
        Console.WriteLine($"second start: {cached.Status} in {clock.Elapsed.TotalSeconds:F2} s");
        Assert.AreEqual(PrototypeProvisionStatus.Cached, cached.Status, cached.Detail);

        var realMaps = Path.Combine(realModules, "Arcanum", "maps");
        if (Directory.Exists(realMaps))
        {
            var theirMaps = Directory.EnumerateFiles(realMaps).Where(path => !Path.GetFileName(path).StartsWith('.')).ToDictionary(path => Path.GetFileName(path), path => new FileInfo(path).Length, StringComparer.Ordinal);
            var ourMaps = Directory.EnumerateFiles(Path.Combine(built.MountFolder!, "maps")).ToDictionary(path => Path.GetFileName(path), path => new FileInfo(path).Length, StringComparer.Ordinal);
            CollectionAssert.AreEquivalent(theirMaps.Keys.ToArray(), ourMaps.Keys.ToArray(), "the same map file names as the original wrote");
            Assert.AreEqual(0, theirMaps.Count(pair => ourMaps[pair.Key] != pair.Value), "the same map file lengths");
            Assert.AreEqual(theirMaps.Count, built.MapFiles);
        }

        var original = Path.Combine(realData, "proto");
        if (!Directory.Exists(original)) return;
        var ours = Directory.EnumerateFiles(Path.Combine(built.MountFolder!, "proto")).ToDictionary(path => Path.GetFileName(path), path => new FileInfo(path).Length, StringComparer.OrdinalIgnoreCase);
        var theirs = Directory.EnumerateFiles(original, "*.pro").Where(path => !Path.GetFileName(path).StartsWith('.')).ToDictionary(path => Path.GetFileName(path), path => new FileInfo(path).Length, StringComparer.OrdinalIgnoreCase);
        CollectionAssert.AreEquivalent(theirs.Keys.ToArray(), ours.Keys.ToArray(), "the same names as the original wrote");
        Assert.AreEqual(0, theirs.Count(pair => ours[pair.Key] != pair.Value), "the same lengths");
    }
}
