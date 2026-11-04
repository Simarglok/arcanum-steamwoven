using System.Text.RegularExpressions;
using Arcanum.Data.Content;
using Arcanum.Executable.Prototypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>
/// ARC-DEV-068, opt-in: the prototype builder on the player's own <c>Arcanum.exe</c> and data. Needs <c>ARCANUM_EXE</c> (the retail
/// 1.0.7.4 executable) and <c>ARCANUM_GAME_ROOT</c> (the installation's game folder); without either the tests are inconclusive. <c>ARCANUM_ORIGINAL_PROTO</c> (a folder with the 1,423 files the original itself wrote on a finished
/// installation, such as its <c>data\proto</c>) adds the comparison with them; <c>ARCANUM_PROTO_OUT</c> writes the built files there
/// for a closer look (never inside the repository). Nothing of the executable, the data or the built files is kept.
/// </summary>
[TestClass]
public sealed class PrototypeBuilderRealDataTests
{
    private const byte OtherFill = 0xA5;

    public TestContext TestContext { get; set; } = null!;

    /// <summary>The data the original sees at start: base sources only (it builds before it mounts the module).</summary>
    private sealed class StackSource(ContentStack stack) : IOriginalFileSource
    {
        public IReadOnlyList<string> List(string directory)
        {
            var prefix = directory.Length == 0 ? "" : directory.Replace('\\', '/').ToLowerInvariant() + "/";
            return stack.Keys(prefix)
                .Where(key => ContentPhase.Candidate(stack, key, ContentLoadPhase.GameStart) is not null)
                .Select(key => key[prefix.Length..]).Where(rest => rest.Length > 0 && !rest.Contains('/')).ToList();
        }

        public byte[]? Read(string path)
        {
            if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } candidate) return null;
            var read = stack.Read(candidate);
            return read.Status == ContentReadStatus.Read ? read.Bytes : null;
        }
    }

    // The builds take half a minute each and the tests run in parallel: each distinct build runs once and the tests share it.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<PrototypeBuildResult>> Cache = [];

    private PrototypeBuildResult Build(byte fill, IReadOnlySet<string>? existing = null) =>
        Cache.GetOrAdd($"{fill}/{existing?.Count}", _ => new Lazy<PrototypeBuildResult>(() => BuildNow(fill, existing))).Value;

    private PrototypeBuildResult BuildNow(byte fill, IReadOnlySet<string>? existing)
    {
        var exe = Environment.GetEnvironmentVariable("ARCANUM_EXE");
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) Assert.Inconclusive("ARCANUM_EXE is not set");
        var root = Environment.GetEnvironmentVariable("ARCANUM_GAME_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) Assert.Inconclusive("ARCANUM_GAME_ROOT is not set");
        var discovery = ContentMountPlan.Discover(root);
        Assert.IsNotNull(discovery.Plan, $"the game folder {root}: {discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan);
        Assert.IsNotNull(opened.Stack, opened.Status.ToString());
        using var stack = opened.Stack;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = PrototypeBuilder.Build(File.ReadAllBytes(exe), new StackSource(stack), new PrototypeBuildOptions { UninitializedFill = fill, ExistingFiles = existing });
        TestContext.WriteLine($"fill {fill}, {existing?.Count ?? 0} existing: {result.Files.Count} files, {result.Steps} steps, {started.Elapsed.TotalSeconds:F1} s; failure: {result.Failure}");
        Assert.IsNull(result.Failure, result.Failure?.ToString());
        return result;
    }

    private static bool InsideARepository(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))) return true;
        return false;
    }

    private static string? OriginalFolder()
    {
        var folder = Environment.GetEnvironmentVariable("ARCANUM_ORIGINAL_PROTO");
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
    }

    [TestMethod, TestCategory(OriginalExecutableTests.Category)]
    public void BuildsAllPrototypesAndTheOnlyUninitializedValuesAreScriptWords()
    {
        var first = Build(0);
        var second = Build(OtherFill);
        Assert.AreEqual(1423, first.Files.Count);
        CollectionAssert.AreEqual(first.Files.Select(f => f.Name).ToList(), second.Files.Select(f => f.Name).ToList());
        Assert.AreEqual(first.Files.Count, first.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        TestContext.WriteLine("read: " + string.Join(", ", first.ReadFiles));
        TestContext.WriteLine("listed: " + string.Join(", ", first.ListedPatterns));
        TestContext.WriteLine("missed: " + string.Join(", ", first.MissedFiles));
        Assert.AreEqual(1, first.CallbackCalls.ViewChange, "the view-change callback is called once, by the location module's init");
        Assert.AreEqual(0, first.CallbackCalls.Redraw);

        // Output that depends on uninitialized memory or an address is found by building over other memory: nothing but word 0 of a script element differs.
        var dependent = new SortedDictionary<string, int>();
        for (var i = 0; i < first.Files.Count; i++)
            foreach (var key in RecordComparer.UninitializedValues(first.Files[i].Bytes, second.Files[i].Bytes))
                dependent[Regex.Replace(key, @"\.e\d+", ".e*")] = dependent.GetValueOrDefault(Regex.Replace(key, @"\.e\d+", ".e*")) + 1;
        TestContext.WriteLine("values that depend on uninitialized memory: " + string.Join(", ", dependent.Select(p => $"{p.Key} x{p.Value}")));
        CollectionAssert.AreEqual(new[] { "s31.e*.w0" }, dependent.Keys.ToArray());

        var output = Environment.GetEnvironmentVariable("ARCANUM_PROTO_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            Assert.IsFalse(InsideARepository(output), "ARCANUM_PROTO_OUT must be outside any git repository: the built files are the player's data, never committed");
            Directory.CreateDirectory(output);
            foreach (var file in first.Files) File.WriteAllBytes(Path.Combine(output, file.Name), file.Bytes);
        }
    }

    [TestMethod, TestCategory(OriginalExecutableTests.Category)]
    public void EveryBuiltFileEqualsTheFileTheOriginalWrote()
    {
        var folder = OriginalFolder() ?? throw new AssertInconclusiveException("ARCANUM_ORIGINAL_PROTO is not set");
        var first = Build(0);
        var second = Build(OtherFill);
        var expected = Directory.EnumerateFiles(folder, "*.pro").Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
        CollectionAssert.AreEqual(expected, first.Files.Select(f => f.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(), "the file names");
        CollectionAssert.AreEqual(first.Files.Select(f => f.Name).ToList(), second.Files.Select(f => f.Name).ToList(), "both builds write the files in the same order");
        var different = new List<string>();
        for (var i = 0; i < first.Files.Count; i++)
        {
            var original = File.ReadAllBytes(Path.Combine(folder, first.Files[i].Name));
            var differences = RecordComparer.Differences(first.Files[i].Bytes, original, second.Files[i].Bytes);
            if (differences.Count > 0) different.Add($"{first.Files[i].Name}: {string.Join("; ", differences)}");
        }
        TestContext.WriteLine($"equal {first.Files.Count - different.Count}, different {different.Count}");
        foreach (var line in different.Take(20)) TestContext.WriteLine(line);
        Assert.AreEqual(0, different.Count);
    }

    [TestMethod, TestCategory(OriginalExecutableTests.Category)]
    public void AFinishedInstallationRebuildsOnlyTheFilesTheCheckDoesNotFind()
    {
        var fresh = Build(0);
        var existing = fresh.Files.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var again = Build(0, existing);
        TestContext.WriteLine($"rebuilt {again.Files.Count} of {fresh.Files.Count}");
        var rebuilt = again.Files.Select(f => f.Name).ToList();
        Assert.AreEqual(307, rebuilt.Count, "the files whose name the check does not expect are rewritten at every start; the others are found and left alone");
        var byName = fresh.Files.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var file in again.Files)
            Assert.AreEqual(0, RecordComparer.Differences(file.Bytes, byName[file.Name].Bytes, Build(OtherFill).Files.First(f => f.Name == file.Name).Bytes).Count, file.Name);
        TestContext.WriteLine("numbers by group: " + string.Join(", ", again.Files.GroupBy(f => f.Number / 1000).Select(g => $"{g.Key}xxx:{g.Count()}")));
    }
}
