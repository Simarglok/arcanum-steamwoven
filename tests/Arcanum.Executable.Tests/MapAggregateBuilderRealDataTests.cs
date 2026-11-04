using System.Text;
using Arcanum.Data.Content;
using Arcanum.Executable.Maps;
using Arcanum.Executable.Prototypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>
/// ARC-DEV-082, opt-in: the map aggregate builder on the player's own <c>Arcanum.exe</c> and data. Needs <c>ARCANUM_EXE</c> (the retail 1.0.7.4
/// executable) and <c>ARCANUM_GAME_ROOT</c> (a game folder whose sources hold the module and the prototype files: an installation the original ran, or
/// a folder with the E4 cache mounted); the aggregates the original itself wrote are read from <c>ARCANUM_ORIGINAL_MAPS</c> (a folder such as
/// <c>modules/Arcanum/maps</c> of such an installation) for the byte comparison. <c>ARCANUM_AGGREGATE_OUT</c> writes the built files
/// there (never inside the repository). Nothing of the executable, the data or the built files is kept.
/// </summary>
[TestClass]
public sealed class MapAggregateBuilderRealDataTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The data the original sees once the module is mounted: the content stack's winner for every path, folders under a directory with the case the source holds.</summary>
    private sealed class ModuleSource(ContentStack stack) : IOriginalFileSource
    {
        public IReadOnlyList<string> List(string directory)
        {
            var prefix = directory.Length == 0 ? "" : directory.Replace('\\', '/').ToLowerInvariant() + "/";
            return stack.Keys(prefix)
                .Where(key => ContentPhase.Candidate(stack, key, ContentLoadPhase.ModuleLoaded) is not null)
                .Select(key => key[prefix.Length..]).Where(rest => rest.Length > 0 && !rest.Contains('/')).ToList();
        }

        public IReadOnlyList<string> ListDirectories(string directory) => ContentPhase.Folders(stack, directory, ContentLoadPhase.ModuleLoaded);

        public byte[]? Read(string path)
        {
            if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } candidate) return null;
            var read = stack.Read(candidate);
            return read.Status == ContentReadStatus.Read ? read.Bytes : null;
        }
    }

    // A build takes several seconds and the tests run in parallel: each distinct build runs once and the tests share it.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, Lazy<(MapAggregateRunResult Result, string Detail)>> Shared = [];

    private static (MapAggregateRunResult Result, string Detail) Build(byte fill) => Shared.GetOrAdd(fill, _ => new(() => BuildNow(fill))).Value;

    private static (MapAggregateRunResult, string) BuildNow(byte fill)
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
        var dat = discovery.Plan.SearchOrder.Single(source => source.Role == ContentSourceRole.ModuleArchive);
        var footer = new byte[MapAggregateBuilder.ArchiveFooterBytes];
        using (var file = File.OpenRead(dat.Path))
        {
            file.Seek(-footer.Length, SeekOrigin.End);
            file.ReadExactly(footer);
        }
        var guid = MapAggregateBuilder.ModuleGuidFromArchiveFooter(footer);
        Assert.IsNotNull(guid, "the module archive's footer");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = MapAggregateBuilder.Build(File.ReadAllBytes(exe), new ModuleSource(stack), guid, new MapAggregateBuildOptions { UninitializedFill = fill });
        return (result, $"{result.Files.Count} aggregates, {result.Steps} steps, {started.Elapsed.TotalSeconds:F1} s; failure: {result.Failure}; log: {string.Join(" | ", result.Log)}");
    }

    private static string? OriginalFolder()
    {
        var folder = Environment.GetEnvironmentVariable("ARCANUM_ORIGINAL_MAPS");
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
    }

    [TestMethod, TestCategory(OriginalExecutableTests.Category)]
    public void BuildsTheAggregatesTheOriginalWrote()
    {
        var (result, detail) = Build(0);
        TestContext.WriteLine(detail);
        Assert.IsNull(result.Failure, detail);
        TestContext.WriteLine("inits returning 0: " + string.Join(", ", result.InitsReturningZero));
        TestContext.WriteLine("listed: " + string.Join(", ", result.ListedPatterns));
        TestContext.WriteLine("missed: " + string.Join(", ", result.MissedFiles.Take(40)));
        if (Environment.GetEnvironmentVariable("ARCANUM_AGGREGATE_OUT") is { Length: > 0 } outFolder)
        {
            Directory.CreateDirectory(outFolder);
            foreach (var file in result.Files) File.WriteAllBytes(Path.Combine(outFolder, file.FileName), file.Bytes);
        }
        var original = OriginalFolder();
        if (original is null) { Assert.Inconclusive("ARCANUM_ORIGINAL_MAPS is not set"); return; }
        var originals = Directory.EnumerateFiles(original).Where(path => !Path.GetFileName(path).StartsWith('.')).ToDictionary(path => Path.GetFileName(path), StringComparer.Ordinal);
        var different = new List<string>();
        var identical = 0;
        foreach (var file in result.Files)
        {
            if (!originals.TryGetValue(file.FileName, out var path)) { different.Add($"{file.FileName}: the original has no such file"); continue; }
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != file.Bytes.Length) different.Add($"{file.FileName}: {bytes.Length} bytes against {file.Bytes.Length}");
            else if (RecordComparer.AggregateDifferences(file.Bytes, bytes) is { Count: > 0 } fields) different.Add($"{file.FileName}: {fields.Count} values differ, first: {fields[0]}");
            else if (!bytes.AsSpan().SequenceEqual(file.Bytes)) identical++;
        }
        foreach (var name in originals.Keys.Where(name => !result.Files.Any(file => string.Equals(file.FileName, name, StringComparison.Ordinal)))) different.Add($"{name}: not built");
        TestContext.WriteLine($"{result.Files.Count - different.Count} aggregates equal field by field ({identical} of them differ in bytes the reader does not return)");
        foreach (var line in different.Take(30)) TestContext.WriteLine(line);
        Assert.AreEqual(0, different.Count, $"{different.Count} of {result.Files.Count} aggregates differ from the original's");
        Assert.AreEqual(originals.Count, result.Files.Count);
    }

    /// <summary>
    /// The bytes in which the build differs from the original's are the 16-bit word the original leaves in the unused bytes of an object ID (an ID
    /// of a kind that does not use bytes 2 to 7 keeps what was in memory there; the repository's reader leaves them out): every run of differing
    /// bytes is exactly one 16-bit word, and the original's word is the build's plus one constant, in every file. The cause of the offset is not
    /// identified (it does not move with the fill byte, the stack or the heap, so it is no address); it is no data an aggregate holds.
    /// </summary>
    [TestMethod, TestCategory(OriginalExecutableTests.Category)]
    public void TheOnlyBytesThatDifferFromTheOriginalAreOneConstantOffsetSixteenBitWord()
    {
        var first = Build(0).Result;
        Assert.IsNull(first.Failure, first.Failure?.ToString());
        var original = OriginalFolder();
        if (original is null) { Assert.Inconclusive("ARCANUM_ORIGINAL_MAPS is not set"); return; }
        var offsets = new HashSet<int>();
        var runs = 0;
        foreach (var file in first.Files)
        {
            var theirs = File.ReadAllBytes(Path.Combine(original, file.FileName));
            Assert.AreEqual(theirs.Length, file.Bytes.Length, file.FileName);
            for (var i = 0; i < theirs.Length;)
            {
                if (theirs[i] == file.Bytes[i]) { i++; continue; }
                var end = i;
                while (end < theirs.Length && theirs[end] != file.Bytes[end]) end++;
                Assert.AreEqual(2, end - i, $"{file.FileName} bytes {i}..{end}: a difference is one 16-bit word");
                offsets.Add((ushort)(BitConverter.ToUInt16(theirs, i) - BitConverter.ToUInt16(file.Bytes, i)));
                runs++;
                i = end;
            }
        }
        TestContext.WriteLine($"{runs} words differ from the original's, with {offsets.Count} different offsets: {string.Join(", ", offsets.Take(3))}");
        Assert.IsTrue(offsets.Count <= 1, "one constant offset");
    }
}
