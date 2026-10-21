using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>
/// ARC-WORLD-054: <see cref="FateRules"/> against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>, model <c>fate</c>): one PC with its fate points (stat 20) and armed fates (field 0xFE), and
/// recording stubs of the stat, field, hit-point and fatigue writes. The stat setter itself is a stub, so a written fate point
/// count is compared after the setter's clamp 0..100 (ledger FT7). Opt-in like <see cref="ExperienceOriginalExeTests"/>:
/// inconclusive without <c>ARCANUM_EXE</c> or the oracle's files; planted mutants of the C# side must each be caught.
/// </summary>
[TestClass]
public sealed class FateOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] Fields =
        ["kind", "points", "armed", "result", "flagsWritten", "pointsWritten", "poisonWritten", "hitPointDamageWritten", "fatigueDamageWritten"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<FateCase, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void ArmedMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F5090", "fate-armed", TestContext);
        Run(file, c => FateRules.IsArmed(State(c), Kind(c)) ? "1" : "0", c => c.Result != 0 ? "1" : "0",
            new Mutant("kind k reads bit k", c => c.Kind < 11 && (c.Armed & (1 << c.Kind)) != 0 ? "1" : "0"),
            new("the full heal reads bit 0", c => (c.Armed & (c.Kind == 0 ? 1 : 1 << (c.Kind - 1))) != 0 ? "1" : "0"));
    }

    [TestMethod, TestCategory(Category)]
    public void ArmMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F50C0", "fate-arm", TestContext);
        Run(file, c => Writes(FateRules.Arm(State(c), Kind(c)), pointsWritten: true, fieldWritten: true), Original,
            new Mutant("a point spent on an armed kind", c => Variant(c, spendWhenArmed: true, healSpends: true, minimum: 1)),
            new("the full heal spends nothing", c => Variant(c, spendWhenArmed: false, healSpends: false, minimum: 1)),
            new("two points needed", c => Variant(c, spendWhenArmed: false, healSpends: true, minimum: 2)));
    }

    [TestMethod, TestCategory(Category)]
    public void UndoMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F51B0", "fate-undo", TestContext);
        Run(file, c => Writes(FateRules.Undo(State(c), Kind(c)), pointsWritten: true, fieldWritten: true), Original,
            new Mutant("no refund", c => Writes(FateRules.Use(State(c), Kind(c)), pointsWritten: false, fieldWritten: true)),
            new("no refund cap", c => Writes(FateRules.Undo(State(c), Kind(c)), pointsWritten: true, fieldWritten: true, refundCap: false)));
    }

    [TestMethod, TestCategory(Category)]
    public void UseMatchesTheOriginal()
    {
        var file = OracleFile.Load("4F5270", "fate-use", TestContext);
        Run(file, c => Writes(FateRules.Use(State(c), Kind(c)), pointsWritten: false, fieldWritten: true), Original,
            new Mutant("a point refunded", c => Writes(FateRules.Undo(State(c), Kind(c)), pointsWritten: true, fieldWritten: true)),
            new("the bit kept", c => FateRules.IsArmed(State(c), Kind(c)) ? "1 -1 -1 -1 -1 -1" : "0 -1 -1 -1 -1 -1"));
    }

    private static FateState State(FateCase c) => new(c.Points, c.Armed);

    private static FateKind Kind(FateCase c) => (FateKind)c.Kind;

    // The original's answer and writes: a written point count passes the stat setter's clamp 0..100 (FT7).
    private static string Original(FateCase c) =>
        $"{c.Result} {c.FlagsWritten} {(c.PointsWritten == -1 ? -1 : Math.Clamp(c.PointsWritten, 0, FateRules.MaxPoints))} " +
        $"{c.PoisonWritten} {c.HitPointDamageWritten} {c.FatigueDamageWritten}";

    // The C# change as the original's writes: the answer, the flags written (none for the full heal or a refusal), the points
    // written, and the full heal's three writes (LifeRules.FateFullHeal).
    private static string Writes(FateChange change, bool pointsWritten, bool fieldWritten, bool refundCap = true)
    {
        var done = change.Status is FateStatus.Armed or FateStatus.UsedAtOnce or FateStatus.Undone or FateStatus.Used;
        var heal = change.Status == FateStatus.UsedAtOnce;
        var flags = done && fieldWritten && !heal ? change.After.Armed : -1;
        var points = done && pointsWritten ? (refundCap ? change.After.Points : change.Before.Points + (change.Status == FateStatus.Undone ? 1 : -1)) : -1;
        return $"{(done ? 1 : 0)} {flags} {points} {(heal ? "0 0 0" : "-1 -1 -1")}";
    }

    // A planted variant of the arm, written apart from FateRules.
    private static string Variant(FateCase c, bool spendWhenArmed, bool healSpends, int minimum)
    {
        var mask = c.Kind == 0 ? 0 : 1 << (c.Kind - 1);
        var armed = (c.Armed & mask) != 0;
        if (armed && !spendWhenArmed) return "0 -1 -1 -1 -1 -1";
        if (c.Points < minimum) return "0 -1 -1 -1 -1 -1";
        if (mask == 0) return $"1 -1 {(healSpends ? c.Points - 1 : -1)} 0 0 0";
        return $"1 {c.Armed | mask} {c.Points - 1} -1 -1 -1";
    }

    /// <summary>Compares the C# side with the original on every input, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<FateCase, string> real, Func<FateCase, string> original, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        var unexpected = file.Cases.Where(c => real(c) != original(c)).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => m.Compute(c) != original(c))).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var pass = unexpected.Count == 0 && missed.Count == 0;
        var method = ExperienceOriginalExeTests.OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  differences: {unexpected.Count}");
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
            Assert.Fail($"{unexpected.Count} differences from the original; first: {unexpected[0]} original {original(unexpected[0])}, Steamwoven {real(unexpected[0])}");
        Assert.AreEqual(0, missed.Count, "planted mutants not caught (the inputs do not cover them): " + string.Join(", ", missed));
    }

    /// <summary>The oracle receipt (docs/verification/receipts), in the format of <see cref="ExperienceOriginalExeTests"/>.</summary>
    private void WriteReceipt(OracleFile file, MethodInfo method, int planted, int caught, int differences, bool pass)
    {
        var folder = Environment.GetEnvironmentVariable("ARCANUM_ORACLE_RECEIPTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var receipt = new Dictionary<string, object?>
        {
            ["format"] = 1,
            ["function"] = file.Function,
            ["name"] = file.Name,
            ["method"] = MethodIlHash.Name(method),
            ["seed"] = file.Seed,
            ["inputs"] = file.Cases.Count,
            ["mutantsPlanted"] = planted,
            ["mutantsCaught"] = caught,
            ["differences"] = differences,
            ["otherMode"] = null,
            ["result"] = pass ? "pass" : "fail",
            ["exeSha256"] = file.ExeSha256,
            ["ilSha256"] = MethodIlHash.Compute(method),
            ["configuration"] = MethodIlHash.Configuration(method),
        };
        var path = Path.Combine(folder, file.Key + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
        TestContext.WriteLine($"  receipt {path}");
    }

    /// <summary>One oracle input: the kind, the PC's fate points and armed flags, the original's answer and its writes (−1: none).</summary>
    internal sealed record FateCase(int Kind, int Points, int Armed, int Result, int FlagsWritten, int PointsWritten, int PoisonWritten,
        int HitPointDamageWritten, int FatigueDamageWritten)
    {
        public override string ToString() => $"kind {Kind} points {Points} armed {Armed:X}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<FateCase> Cases)
    {
        /// <summary>The oracle's file for <paramref name="key"/>, or an inconclusive result without <c>ARCANUM_EXE</c> or the file
        /// (a failure instead when <c>ARCANUM_ORACLE_REQUIRED=1</c>).</summary>
        public static OracleFile Load(string key, string name, TestContext context)
        {
            var exe = Environment.GetEnvironmentVariable("ARCANUM_EXE");
            if (string.IsNullOrWhiteSpace(exe)) Missing("ARCANUM_EXE is required for the OriginalExe check.");
            var folder = Environment.GetEnvironmentVariable("ARCANUM_ORACLE_DIR");
            if (string.IsNullOrWhiteSpace(folder))
            {
                var repository = RepositoryRoot();
                if (repository is null) Missing("the repository root (Arcanum.Steamwoven.sln) was not found; set ARCANUM_ORACLE_DIR.");
                folder = Path.Combine(repository, "research-private", "oracle");
            }
            var path = Path.Combine(folder, key + ".json");
            if (!File.Exists(path)) Missing($"no oracle file {path}; run ./arcanum oracle first.");
            Assert.IsTrue(File.Exists(exe), $"ARCANUM_EXE {exe} does not exist.");
            using (var stream = File.OpenRead(exe))
                Assert.AreEqual(ExperienceOriginalExeTests.ExeSha256, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), "ARCANUM_EXE is not the retail 1.0.7.4 executable.");
            context.WriteLine($"oracle file {path}");

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            Assert.AreEqual(1, root.GetProperty("format").GetInt32(), "oracle file format");
            Assert.AreEqual(key, root.GetProperty("key").GetString(), "oracle file function");
            Assert.AreEqual(name, root.GetProperty("name").GetString(), "oracle file name");
            var sha = root.GetProperty("exeSha256").GetString()!;
            Assert.AreEqual(ExperienceOriginalExeTests.ExeSha256, sha, "the oracle file was not written from the retail executable");
            var count = root.GetProperty("count").GetInt32();
            var rows = root.GetProperty("cases");
            Assert.AreEqual(count, rows.GetArrayLength(), "oracle file case count");
            Assert.IsTrue(count >= ExperienceOriginalExeTests.MinimumCases, $"the oracle file holds {count} inputs; at least {ExperienceOriginalExeTests.MinimumCases} are required.");
            CollectionAssert.AreEqual(Fields, root.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray());
            var cases = new List<FateCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                var v = row.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                cases.Add(new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]));
            }
            return new(key, root.GetProperty("function").GetString()!, name, sha, root.GetProperty("seed").GetInt64(), cases);
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
}
