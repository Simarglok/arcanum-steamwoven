using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>
/// ARC-DEV-067: the interpreter on the player's own retail 1.0.7.4 <c>Arcanum.exe</c>, opt-in. Without <c>ARCANUM_EXE</c>
/// the tests are inconclusive. The replay tests run two functions of the executable on the seeded inputs the function
/// oracle (ARC-DEV-007, Unicorn) wrote to <c>ARCANUM_ORACLE_DIR</c> (default <c>research-private/oracle</c>) and require the
/// same result for every input; without the files they are inconclusive (failed with <c>ARCANUM_ORACLE_REQUIRED=1</c>).
/// Nothing of the executable or the files is kept: they are read in place.
/// </summary>
[TestClass]
public sealed class OriginalExecutableTests
{
    public const string Category = "OriginalExe";
    public const string ExeSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";
    private const uint StatGetter = 0x4B0490;      // stat value of an object: (handle low, handle high, stat) -> value
    private const uint XpTablePointer = 0x5F5C94;  // pointer to the experience thresholds
    private const uint HandleLow = 0x5EED_0001, HandleHigh = 0x0000_0042; // the oracle's one fictitious object
    private const uint Heap = 0x1000_0000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod, TestCategory(Category)]
    public void ReadsThePlayersExecutable()
    {
        var image = LoadExecutable();
        Assert.AreEqual(0x0040_0000u, image.ImageBase);
        var text = image.Sections.Single(s => s.Name == ".text");
        Assert.AreEqual(MemoryAccess.ReadExecute, text.Access);
        Assert.IsTrue(image.Imports.Count > 0, "the executable imports functions");
        var machine = new X86Machine();
        image.LoadInto(machine.Memory);
        machine.BindImports(image);
        Assert.IsTrue(machine.Memory.CanExecute(0x0046_8890), "the prototype build (PX2) is code");
        TestContext.WriteLine($"{image.Sections.Count} sections, {image.Imports.Count} imports, entry 0x{image.EntryPoint:X8}");
    }

    [TestMethod, TestCategory(Category)]
    public void LevelBonusPointsMatchTheOracle()
    {
        var image = LoadExecutable();
        var (cases, _) = Oracle("4A6980", "level-bonus-points", ["from", "to", "result"]);
        var machine = new X86Machine();
        image.LoadInto(machine.Memory);
        machine.BindImports(image);
        machine.Census = new MachineCensus();
        foreach (var row in cases)
        {
            var result = machine.Call(0x4A6980, [(uint)row[0], (uint)row[1]], 100_000);
            Assert.IsTrue(result.Completed, $"({row[0]}, {row[1]}): {result.Fault}");
            Assert.AreEqual(row[2], (int)result.Eax, $"level bonus points ({row[0]}, {row[1]})");
        }
        TestContext.WriteLine($"{cases.Count} inputs equal; census: {machine.Census.InstructionKinds.Count} kinds, {machine.Census.DistinctInstructionAddresses} addresses");
    }

    [TestMethod, TestCategory(Category)]
    public void ExperienceToNextLevelMatchesTheOracleThroughAHook()
    {
        var image = LoadExecutable();
        var (cases, root) = Oracle("4A68B0", "experience-to-next-level", ["table", "level", "experience", "result"]);
        var tables = root.GetProperty("tables").EnumerateArray().Select(t => t.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
        var machine = new X86Machine();
        image.LoadInto(machine.Memory);
        machine.BindImports(image);
        var stride = (uint)(4 * tables[0].Length);
        machine.Memory.Map(Heap, (uint)tables.Length * stride, MemoryAccess.ReadWrite, "tables");
        for (var t = 0; t < tables.Length; t++)
            for (var i = 0; i < tables[t].Length; i++) machine.Memory.WriteU32(Heap + (uint)t * stride + 4 * (uint)i, (uint)tables[t][i]);
        int level = 0, experience = 0;
        machine.Hook(StatGetter, "stat getter", m =>
        {
            if (m.Argument(0) != HandleLow || m.Argument(1) != HandleHigh) return HookResult.Stop("not the world model's object");
            var stat = m.Argument(2);
            if (stat is not (17 or 18)) return HookResult.Stop($"stat {stat} is not in the world model");
            m.Eax = (uint)(stat == 17 ? level : experience);
            return HookResult.Return();
        });
        machine.Census = new MachineCensus();
        Span<byte> pointer = stackalloc byte[4];
        foreach (var row in cases)
        {
            (level, experience) = (row[1], row[2]);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(pointer, Heap + (uint)row[0] * stride);
            machine.Memory.Load(XpTablePointer, pointer);
            var result = machine.Call(0x4A68B0, [HandleLow, HandleHigh], 100_000);
            Assert.IsTrue(result.Completed, $"table {row[0]}, level {row[1]}, experience {row[2]}: {result.Fault}");
            Assert.AreEqual(row[3], (int)result.Eax, $"to next level: table {row[0]}, level {row[1]}, experience {row[2]}");
        }
        Assert.IsTrue(machine.Census.Hooks["stat getter"] >= cases.Count, "the stat getter answered every input");
        TestContext.WriteLine($"{cases.Count} inputs equal; {machine.Census.Hooks["stat getter"]} stat-getter calls");
    }

    private PeImage LoadExecutable()
    {
        var path = Environment.GetEnvironmentVariable("ARCANUM_EXE");
        if (string.IsNullOrEmpty(path)) Missing("ARCANUM_EXE is not set.");
        Assert.IsTrue(File.Exists(path), $"ARCANUM_EXE {path} does not exist.");
        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual(ExeSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "ARCANUM_EXE is not the retail 1.0.7.4 executable.");
        var result = PeImage.Read(bytes);
        Assert.IsTrue(result.Succeeded, result.Failure?.ToString());
        return result.Image!;
    }

    private (List<int[]> Cases, JsonElement Root) Oracle(string key, string name, string[] fields)
    {
        var folder = Environment.GetEnvironmentVariable("ARCANUM_ORACLE_DIR");
        if (string.IsNullOrEmpty(folder))
        {
            var root = RepositoryRoot();
            if (root is null) Missing("ARCANUM_ORACLE_DIR is not set and the repository root was not found.");
            folder = Path.Combine(root, "research-private", "oracle");
        }
        var path = Path.Combine(folder, key + ".json");
        if (!File.Exists(path)) Missing($"no oracle file {path}; run ./arcanum oracle first.");
        var document = JsonDocument.Parse(File.ReadAllBytes(path)).RootElement;
        Assert.AreEqual(1, document.GetProperty("format").GetInt32(), "oracle file format");
        Assert.AreEqual(name, document.GetProperty("name").GetString());
        Assert.AreEqual(ExeSha256, document.GetProperty("exeSha256").GetString(), "the oracle file was not written from the retail executable");
        CollectionAssert.AreEqual(fields, document.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
        var cases = document.GetProperty("cases").EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList();
        Assert.AreEqual(document.GetProperty("count").GetInt32(), cases.Count);
        Assert.IsTrue(cases.Count >= 10_000, $"{cases.Count} inputs");
        TestContext.WriteLine($"oracle file {path}");
        return (cases, document);
    }

    [DoesNotReturn]
    private static void Missing(string message)
    {
        if (Environment.GetEnvironmentVariable("ARCANUM_ORACLE_REQUIRED") == "1") Assert.Fail(message);
        Assert.Inconclusive(message);
        throw new System.Diagnostics.UnreachableException();
    }

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Arcanum.Steamwoven.sln"))) return directory.FullName;
        return null;
    }
}
