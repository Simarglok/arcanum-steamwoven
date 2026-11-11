using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-392: a map's transitions kept on the device for the next process. Synthetic content only; the store's folder is a temporary one the test owns,
/// and the static settings it touches (the folder, the code stamp) are put back, so no other test or tool sees them.</summary>
[TestClass, DoNotParallelize]
public sealed class AreaMapTransitionsStoreTests
{
    private DirectoryInfo _game = null!, _store = null!;
    private string? _savedRoot;

    [TestInitialize]
    public void Create()
    {
        _savedRoot = AreaMapTransitionsStore.Root;
        _game = Directory.CreateTempSubdirectory("arc-dev-392-game-"); _store = Directory.CreateTempSubdirectory("arc-dev-392-store-");
        File.WriteAllBytes(Path.Combine(_game.FullName, "tig.dat"), AreaOwnerFixture.Dat());
        Directory.CreateDirectory(Path.Combine(_game.FullName, "modules"));
        File.WriteAllBytes(Path.Combine(_game.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
        AreaMapTransitionsStore.Root = _store.FullName;
        AreaMapTransitionsStore.CodeStampOverride = null;
    }

    [TestCleanup]
    public void Remove()
    {
        AreaMapTransitionsStore.Root = _savedRoot; AreaMapTransitionsStore.CodeStampOverride = null;
        _game.Delete(recursive: true); _store.Delete(recursive: true);
    }

    private static readonly WorldMap Map = new("alpha", new(2, 0, 64, 64), null, null, null, [], [], [], null, []);

    private ContentStack Open()
    {
        var discovery = ContentMountPlan.Discover(_game.FullName, "Syn");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private static MapTransitionSet Set(int script = 12) => new(Map, new(5000, "alpha", new(1, 2)),
        [new(MapTransitionTrigger.Use, new(10, 20), new(new(1, 1), 3, "ab", 3, null), script, [script], 2, false, true, 5001, 2, new(7, 8), MapTransitionGuard.None)],
        [new(WorldIssueCode.Missing, "maps/alpha/x.sec")], [new(MapTransitionTrigger.SectorEntry, new(1, 1), -1, new(64, 64), 0, 0, 3)]) { IsStartMap = true };

    private static readonly IReadOnlyDictionary<int, AreaMapDestination> Destinations = new Dictionary<int, AreaMapDestination> { [1] = new(1, 5000, "alpha"), [2] = new(2, 5001, "beta") };

    private string[] Files() => Directory.GetFiles(_store.FullName, "*" + AreaMapTransitionsStore.Extension);

    [TestMethod]
    public void A_kept_read_is_given_back_to_the_next_process_equal_to_what_was_written()
    {
        using var stack = Open();
        Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out var key), "nothing kept yet");
        Assert.IsNotNull(key);
        Assert.IsTrue(AreaMapTransitionsStore.Write(key, Set(), Destinations));
        Assert.AreEqual(1, Files().Length);
        var kept = AreaMapTransitionsStore.TryRead(stack, Map, out _);
        Assert.IsNotNull(kept);
        Assert.AreSame(Map, kept.Transitions.Map);
        Assert.AreEqual(Set().Sites.Single() with { Chain = [] }, kept.Transitions.Sites.Single() with { Chain = [] });
        CollectionAssert.AreEqual(new[] { 12 }, kept.Transitions.Sites.Single().Chain.ToArray());
        Assert.AreEqual(Set().Entry, kept.Transitions.Entry); Assert.IsTrue(kept.Transitions.IsStartMap);
        CollectionAssert.AreEquivalent(Destinations.ToArray(), kept.Destinations.ToArray());
        Assert.AreEqual(1, kept.Transitions.Issues.Count); Assert.AreEqual(1, kept.Transitions.Placed.Count);
    }

    [TestMethod]
    public void Another_stamp_finds_nothing_a_changed_archive_a_loose_edit_another_map_another_build()
    {
        string KeyOf(Action? change = null, WorldMap? map = null) { change?.Invoke(); using var stack = Open(); AreaMapTransitionsStore.TryRead(stack, map ?? Map, out var key); return key!; }
        using (var stack = Open()) { AreaMapTransitionsStore.TryRead(stack, Map, out var key); AreaMapTransitionsStore.Write(key, Set(), Destinations); }
        Assert.IsNotNull(Read(), "the same content finds it");
        Assert.IsNull(Read(map: new("beta", new(2, 0, 64, 64), null, null, null, [], [], [], null, [])), "another map has its own file");
        AreaMapTransitionsStore.CodeStampOverride = "another build";
        Assert.IsNull(Read(), "another build of the reader finds no file of an older one");
        AreaMapTransitionsStore.CodeStampOverride = null;
        Assert.IsNotNull(Read(), "the old build's file is still valid for the old build");
        // A loose file edited in place, with the same name count.
        var loose = Path.Combine(_game.FullName, "rules.txt");
        File.WriteAllText(loose, "one");
        Assert.AreNotEqual(KeyOf(), KeyOf(() => File.WriteAllText(loose, "one!")), "a loose file's size is part of the key");
        var before = KeyOf();
        File.SetLastWriteTimeUtc(loose, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.AreNotEqual(before, KeyOf(), "so is its last write time");
        // An archive that changed in size.
        var identity = KeyOf();
        File.WriteAllBytes(Path.Combine(_game.FullName, "tig.dat"), AreaOwnerFixture.Dat(("art/x.art", new byte[] { 1, 2, 3 })));
        Assert.AreNotEqual(identity, KeyOf(), "a changed archive is another stack");

        MapTransitionSet? Read(WorldMap? map = null)
        {
            using var stack = Open();
            return AreaMapTransitionsStore.TryRead(stack, map ?? Map, out _)?.Transitions;
        }
    }

    [TestMethod]
    public void A_damaged_or_foreign_file_is_a_miss_never_an_error_and_the_next_write_replaces_it()
    {
        using var stack = Open();
        AreaMapTransitionsStore.TryRead(stack, Map, out var key);
        Assert.IsTrue(AreaMapTransitionsStore.Write(key, Set(), Destinations));
        var path = Files().Single();
        var good = File.ReadAllBytes(path);
        // A file cut at any point, one with a flipped payload byte, one with trailing bytes and an empty one.
        foreach (var length in new[] { 0, 3, 8, 20, good.Length / 2, good.Length - 1 })
        {
            File.WriteAllBytes(path, good.AsSpan(0, length).ToArray());
            Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out _), $"cut at {length}");
        }
        var flipped = (byte[])good.Clone(); flipped[^3] ^= 0x40; File.WriteAllBytes(path, flipped);
        Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out _), "a changed payload byte fails its digest");
        File.WriteAllBytes(path, [.. good, 1]); Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out _), "bytes after the file");
        // A file of another key under this key's name (a hash collision, or a copied file) is refused by the key it holds.
        File.WriteAllBytes(path, AreaMapTransitionsStore.Encode(key + "x", Set(), Destinations));
        Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out _), "the key inside must equal");
        Assert.IsTrue(AreaMapTransitionsStore.Write(key, Set(), Destinations), "the next write replaces it");
        Assert.IsNotNull(AreaMapTransitionsStore.TryRead(stack, Map, out _));
    }

    [TestMethod]
    public void The_store_is_off_without_a_folder_and_a_folder_that_cannot_be_written_is_a_miss()
    {
        using var stack = Open();
        AreaMapTransitionsStore.Root = null;
        Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out var key));
        Assert.IsNull(key, "off: no key is made, so nothing is hashed");
        Assert.IsFalse(AreaMapTransitionsStore.Write("anything", Set(), Destinations));
        // A path whose parent is a file cannot be a folder: every call answers, none throws.
        var blocked = Path.Combine(_game.FullName, "tig.dat", "map-cache");
        AreaMapTransitionsStore.Root = blocked;
        Assert.IsNull(AreaMapTransitionsStore.TryRead(stack, Map, out key));
        Assert.IsFalse(AreaMapTransitionsStore.Write(key, Set(), Destinations));
    }

    [TestMethod]
    public void The_folder_keeps_the_newest_files_and_two_writers_never_leave_a_half_file()
    {
        for (var n = 0; n < AreaMapTransitionsStore.MaxFiles + 6; n++)
        {
            Assert.IsTrue(AreaMapTransitionsStore.Write($"key-{n}", Set(n + 1), Destinations));
            File.SetLastWriteTimeUtc(AreaMapTransitionsStore.PathFor(_store.FullName, $"key-{n}"), DateTime.UtcNow.AddMinutes(n - 100));
        }
        Assert.AreEqual(AreaMapTransitionsStore.MaxFiles, Files().Length, "the oldest beyond the limit were removed");
        Assert.IsTrue(File.Exists(AreaMapTransitionsStore.PathFor(_store.FullName, $"key-{AreaMapTransitionsStore.MaxFiles + 5}")), "the newest stays");
        // The same key from many threads at once: every call succeeds, the file is whole, no temporary file is left.
        Parallel.For(0, 16, _ => Assert.IsTrue(AreaMapTransitionsStore.Write("shared", Set(), Destinations)));
        using var stack = Open();
        Assert.IsNotNull(AreaMapTransitionsStore.Decode(File.ReadAllBytes(AreaMapTransitionsStore.PathFor(_store.FullName, "shared")), "shared", Map));
        Assert.AreEqual(0, Directory.GetFiles(_store.FullName, "*.tmp").Length);
    }

    [TestMethod]
    public void The_device_folder_is_the_steamwoven_folder_not_the_godot_user_folder()
    {
        var path = AreaMapTransitionsStore.DeviceFolder();
        StringAssert.EndsWith(path, Path.Combine("Arcanum Steamwoven Game", "map-cache"));
        Assert.IsTrue(Path.IsPathRooted(path));
    }
}
