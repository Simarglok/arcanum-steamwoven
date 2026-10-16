using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Content;

[TestClass]
public sealed class ContentStackTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-content-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (!OperatingSystem.IsWindows())
            foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Prepend(_root))
                File.SetUnixFileMode(directory, (UnixFileMode)0b111_000_000);
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Discover_mounts_sources_in_original_order_with_later_mounts_on_top()
    {
        Dat("tig.dat");
        Dat("arcanum1.dat"); Dat("ARCANUM2.dat"); Dat("arcanum10.dat"); Dat("arcanum.dat");
        Dat("arcanumX.txt"); Dat("other.dat");
        Directory.CreateDirectory(Path.Combine(_root, "Data"));
        Dat("modules/Mod.dat"); Dat("modules/Mod.PATCH0"); Dat("modules/mod.patch3"); Dat("modules/Mod.patch10");
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));

        var discovery = ContentMountPlan.Discover(_root, "Mod");

        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var order = discovery.Plan!.SearchOrder.Select(s => $"{s.Role}:{s.Number}:{Path.GetRelativePath(_root, s.Path).Replace('\\', '/')}").ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "ModuleDirectory:0:modules/Mod", "ModulePatch:3:modules/mod.patch3", "ModulePatch:0:modules/Mod.PATCH0", "ModuleArchive:0:modules/Mod.dat",
            "DataDirectory:0:Data", "BaseArchive:3:ARCANUM2.dat", "BaseArchive:2:arcanum10.dat", "BaseArchive:1:arcanum1.dat", "BaseArchive:0:arcanum.dat",
            "GameRoot:0:.", "Tig:0:tig.dat"
        }, order);
        Assert.AreEqual(ContentSourceKind.Directory, discovery.Plan.SearchOrder[0].Kind);
        Assert.AreEqual(ContentSourceKind.Archive, discovery.Plan.SearchOrder[1].Kind);
    }

    [TestMethod]
    public void Discover_mounts_patches_only_with_the_module_archive_and_refuses_missing_inputs()
    {
        Dat("modules/Mod.patch0");
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
        Assert.AreEqual(ContentMountStatus.MissingTig, ContentMountPlan.Discover(_root, "Mod").Status);
        Dat("tig.dat");
        var folderOnly = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, folderOnly.Status);
        CollectionAssert.AreEqual(new[] { ContentSourceRole.ModuleDirectory, ContentSourceRole.GameRoot, ContentSourceRole.Tig },
            folderOnly.Plan!.SearchOrder.Select(s => s.Role).ToArray());
        Assert.AreEqual(ContentMountStatus.MissingModule, ContentMountPlan.Discover(_root, "Other").Status);
        Assert.AreEqual(ContentMountStatus.MissingGameRoot, ContentMountPlan.Discover(Path.Combine(_root, "absent")).Status);
        foreach (var name in new[] { "", ".", "..", "a/b", "a\\b", "c:x", "x\0", "é" })
            Assert.AreEqual(ContentMountStatus.InvalidModuleName, ContentMountPlan.Discover(_root, name).Status, name);
        Directory.CreateDirectory(Path.Combine(_root, "arcanum5.dat"));
        Assert.AreEqual(ContentMountStatus.UnsupportedLayout, ContentMountPlan.Discover(_root, "Mod").Status);
        Directory.Delete(Path.Combine(_root, "arcanum5.dat"));
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod.dat"));
        Assert.AreEqual(ContentMountStatus.UnsupportedLayout, ContentMountPlan.Discover(_root, "Mod").Status);
        Directory.Delete(Path.Combine(_root, "modules", "Mod.dat"));
        Assert.AreEqual(ContentMountStatus.Ready, ContentMountPlan.Discover(_root, "Mod").Status);

        var plan = ContentMountPlan.Discover(_root, "Mod").Plan!;
        Assert.ThrowsExactly<ArgumentException>(() => new ContentMountPlan(_root, "Mod", [plan.SearchOrder[0], plan.SearchOrder[0]]));
        Assert.ThrowsExactly<ArgumentException>(() => new ContentMountPlan(_root, "Mod", [plan.SearchOrder[0], plan.SearchOrder[0] with { Number = 1 }]));
    }

    [TestMethod]
    public void Discover_refuses_names_that_differ_only_in_case()
    {
        Dat("tig.dat"); Dat("arcanum1.dat");
        if (File.Exists(Path.Combine(_root, "ARCANUM1.DAT"))) Assert.Inconclusive("Case-insensitive file system: the layout cannot arise here.");
        Dat("ARCANUM1.DAT");
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
        Assert.AreEqual(ContentMountStatus.AmbiguousName, ContentMountPlan.Discover(_root, "Mod").Status);
        File.Delete(Path.Combine(_root, "ARCANUM1.DAT"));
        Loose("modules/Mod/a.mes", "x");
        Loose("modules/Mod/A.MES", "y");
        Assert.AreEqual(ContentStackStatus.AmbiguousLooseName, OpenResult("Mod").Status);
    }

    [TestMethod]
    public void Resolve_orders_every_copy_by_search_order_and_the_first_one_wins()
    {
        const string key = "rules/x.mes";
        Dat("tig.dat", (key, "tig"));
        Loose(key, "root");
        Dat("arcanum1.dat", (key, "base0"));
        Dat("arcanum2.dat", (key, "base1"), ("rules/y.mes", "only"));
        Loose("data/" + key, "data");
        Dat("modules/Mod.dat", (key, "module"));
        Dat("modules/Mod.patch0", (key, "patch0"));
        Dat("modules/Mod.patch1", (key, "patch1"));
        Loose("modules/Mod/" + key, "folder");
        using var stack = Open("Mod");

        var resolution = stack.Resolve("RULES\\X.MES");

        Assert.AreEqual(ContentResolutionStatus.Resolved, resolution.Status);
        Assert.AreEqual(key, resolution.Key);
        CollectionAssert.AreEqual(new[] { "folder", "patch1", "patch0", "module", "data", "base1", "base0", "root", "tig" },
            resolution.Candidates.Select(c => Text(stack.Read(c))).ToArray());
        Assert.AreEqual("folder", Text(stack.Read(resolution.Winner!)));
        Assert.AreEqual(8, resolution.Shadowed.Count());
        Assert.AreEqual(ContentSourceRole.ModuleDirectory, resolution.Winner!.Source.Role);
        Assert.IsNotNull(resolution.Candidates[1].Entry);
        Assert.AreEqual(6, resolution.Candidates[1].Length);

        var single = stack.Resolve("rules/y.mes");
        Assert.AreEqual(1, single.Candidates.Count);
        Assert.AreEqual(ContentSourceRole.BaseArchive, single.Winner!.Source.Role);
        Assert.AreEqual(1, single.Winner.Source.Number);

        var overrides = stack.Overrides();
        Assert.AreEqual(1, overrides.Count);
        Assert.AreEqual(key, overrides[0].Key);
    }

    [TestMethod]
    public void Resolve_rejects_paths_outside_the_virtual_space()
    {
        Dat("tig.dat", ("a/b", "x"));
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
        using var stack = Open("Mod");
        foreach (var path in new[] { "", ".\\a\\b", "./a/b", "/a/b", "\\a\\b", "c:a", "a//b", "a/../b", "a/b/", "a\0b", "a/é" })
            Assert.AreEqual(ContentResolutionStatus.InvalidPath, stack.Resolve(path).Status, path);
        Assert.AreEqual(ContentResolutionStatus.NotFound, stack.Resolve("a/c").Status);
        Assert.AreEqual(ContentResolutionStatus.Resolved, stack.Resolve("A/B").Status);
    }

    [TestMethod]
    public void Directory_and_ignored_records_cache_folders_and_non_ascii_names_are_not_candidates()
    {
        Dat("tig.dat");
        DatRaw("arcanum1.dat", ("a", 0x400u, ""), ("a\\dir", 0x400u, ""), ("a\\gone", 0x801u, "x"), ("a\\kept", 1u, "k"));
        Loose("modules/Mod/TIGCache/a/kept", "cache");
        Loose("modules/Mod/tigcache2/a/kept", "real");
        Loose("data/café.mes", "skip");
        using var stack = Open("Mod");

        Assert.AreEqual(ContentResolutionStatus.NotFound, stack.Resolve("a").Status);
        Assert.AreEqual(ContentResolutionStatus.NotFound, stack.Resolve("a/dir").Status);
        Assert.AreEqual(ContentResolutionStatus.NotFound, stack.Resolve("a/gone").Status);
        Assert.AreEqual(1, stack.Resolve("a/kept").Candidates.Count);
        Assert.AreEqual(1, stack.ExcludedCacheFiles);
        // Deleted on mount, so not a candidate through the enclosing game folder either.
        Assert.AreEqual(ContentResolutionStatus.NotFound, stack.Resolve("modules/Mod/TIGCache/a/kept").Status);
        Assert.AreEqual(1, stack.Resolve("modules/Mod/tigcache2/a/kept").Candidates.Count);
        Assert.AreEqual("real", Text(stack.Read(stack.Resolve("tigcache2/a/kept").Winner!)));
        // Once under data/ and once under the game folder, which is itself a source.
        Assert.AreEqual(2, stack.SkippedLooseFiles);
    }

    [TestMethod]
    public void Open_refuses_indexes_the_original_binary_search_could_not_model()
    {
        Dat("tig.dat");
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
        DatRaw("arcanum1.dat", ("b", 1u, "x"), ("a", 1u, "y"));
        Assert.AreEqual(ContentStackStatus.UnsortedArchiveIndex, OpenResult("Mod").Status);
        // Sorted with backslashes, unsorted once they become slashes ('1' > '/').
        DatRaw("arcanum1.dat", ("a1", 1u, "x"), ("a\\b", 1u, "y"));
        Assert.AreEqual(ContentStackStatus.UnsortedArchiveIndex, OpenResult("Mod").Status);
        DatRaw("arcanum1.dat", ("A", 1u, "x"), ("a", 1u, "y"));
        Assert.AreEqual(ContentStackStatus.UnsortedArchiveIndex, OpenResult("Mod").Status);
        DatRaw("arcanum1.dat", ("aé", 1u, "x"));
        Assert.AreEqual(ContentStackStatus.UnsupportedEntryName, OpenResult("Mod").Status);
        DatRaw("arcanum1.dat", ("a", 1u, "x"));
        DatRaw("arcanum2.dat", ("a", 0x400u, ""));
        var collision = OpenResult("Mod");
        Assert.AreEqual(ContentStackStatus.DirectoryRecordCollision, collision.Status);
        Assert.AreEqual("a", collision.Detail);
        File.Delete(Path.Combine(_root, "arcanum2.dat"));
        File.WriteAllText(Path.Combine(_root, "ARCANUM1.DAT.IGNORE"), "a\n");
        Assert.AreEqual(ContentStackStatus.IgnoreListUnsupported, OpenResult("Mod").Status);
        File.Delete(Path.Combine(_root, "ARCANUM1.DAT.IGNORE"));
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), new byte[40]);
        var unreadable = OpenResult("Mod");
        Assert.AreEqual(ContentStackStatus.ArchiveUnreadable, unreadable.Status);
        Assert.IsNull(unreadable.Stack);
        StringAssert.EndsWith(unreadable.Detail, "arcanum1.dat");
    }

    [TestMethod]
    public void Open_bounds_loose_files_and_reads_stay_within_limits()
    {
        Dat("tig.dat");
        Loose("modules/Mod/a", "12345");
        Loose("modules/Mod/b", "1");
        Assert.AreEqual(ContentStackStatus.LooseLimitExceeded, ContentStack.Open(Plan("Mod"), new() { MaxLooseFiles = 3 }).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ContentStack.Open(Plan("Mod"), new() { MaxLooseFiles = 0 }));

        var opened = ContentStack.Open(Plan("Mod"), new() { MaxLooseFileBytes = 4 });
        using var stack = opened.Stack!;
        Assert.AreEqual(ContentReadStatus.TooLarge, stack.Read(stack.Resolve("a").Winner!).Status);
        Assert.AreEqual("1", Text(stack.Read(stack.Resolve("b").Winner!)));

        using var other = Open("Mod");
        Assert.ThrowsExactly<ArgumentException>(() => stack.Read(other.Resolve("b").Winner!));
        var issued = stack.Resolve("b").Winner!;
        Assert.ThrowsExactly<ArgumentException>(() => stack.Read(issued with { FilePath = Path.Combine(_root, "tig.dat") }));
        Assert.ThrowsExactly<ArgumentException>(() => stack.Read(issued with { }));
        var winner = other.Resolve("b").Winner!;
        other.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => other.Read(winner));
        Assert.ThrowsExactly<ObjectDisposedException>(() => other.Resolve("b"));
    }

    private ContentStack Open(string module)
    {
        var result = OpenResult(module);
        Assert.AreEqual(ContentStackStatus.Ready, result.Status, result.Detail);
        return result.Stack!;
    }

    private ContentStackOpenResult OpenResult(string module) => ContentStack.Open(Plan(module));

    private ContentMountPlan Plan(string module)
    {
        var discovery = ContentMountPlan.Discover(_root, module);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return discovery.Plan!;
    }

    private static string Text(ContentReadResult result)
    {
        Assert.AreEqual(ContentReadStatus.Read, result.Status, result.Detail);
        return Encoding.ASCII.GetString(result.Bytes!);
    }

    private void Loose(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void Dat(string relative, params (string Name, string Text)[] entries)
        => DatRaw(relative, entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => (e.Name.Replace('/', '\\'), 1u, e.Text)).ToArray());

    /// <summary>A synthetic 1TAD archive with plain payloads, in the given index order.</summary>
    private void DatRaw(string relative, params (string Name, uint Flags, string Text)[] entries)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = new MemoryStream();
        var offsets = new List<uint>();
        foreach (var entry in entries)
        {
            offsets.Add((uint)output.Position);
            output.Write(Encoding.ASCII.GetBytes(entry.Text));
        }
        var index = new MemoryStream();
        U32(index, (uint)entries.Length);
        var nameBytes = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Encoding.Latin1.GetBytes(entries[i].Name + "\0");
            nameBytes += name.Length;
            U32(index, (uint)name.Length);
            index.Write(name);
            index.Write(new byte[4]);
            U32(index, entries[i].Flags);
            var length = (uint)entries[i].Text.Length;
            U32(index, length);
            U32(index, length);
            U32(index, offsets[i]);
        }
        index.Position = 0;
        index.CopyTo(output);
        output.Write(new byte[16]);
        output.Write("1TAD"u8);
        U32(output, (uint)nameBytes);
        U32(output, (uint)(index.Length + 28));
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void U32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    [TestMethod]
    public void Archive_reads_report_limits_and_corrupt_payloads_as_typed_results()
    {
        Dat("tig.dat");
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
        DatRaw("arcanum1.dat", ("bad", 2u, "zzzz"), ("big", 1u, "123456"));
        var opened = ContentStack.Open(Plan("Mod"), new() { Dat = new DatReaderOptions { MaxDecodedEntryBytes = 5 } });
        using var stack = opened.Stack!;
        Assert.AreEqual(ContentReadStatus.TooLarge, stack.Read(stack.Resolve("big").Winner!).Status);
        Assert.AreEqual(ContentReadStatus.Unreadable, stack.Read(stack.Resolve("bad").Winner!).Status);
    }

    [TestMethod]
    public void Open_reports_an_unreadable_loose_folder()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permissions only.");
            return;
        }
        Dat("tig.dat");
        Loose("modules/Mod/locked/a", "x");
        File.SetUnixFileMode(Path.Combine(_root, "modules", "Mod", "locked"), UnixFileMode.None);
        try
        {
            _ = Directory.EnumerateFileSystemEntries(Path.Combine(_root, "modules", "Mod", "locked")).Count();
            Assert.Inconclusive("Running with permissions that ignore the folder mode.");
        }
        catch (UnauthorizedAccessException) { }
        Assert.AreEqual(ContentStackStatus.LooseUnreadable, OpenResult("Mod").Status);
    }
}
