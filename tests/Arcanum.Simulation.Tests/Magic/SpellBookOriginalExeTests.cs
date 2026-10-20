using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>
/// ARC-MAGIC-004: <see cref="SpellBookRules.Learn"/> against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>, model <c>spell-learn</c>): one critter (type, its college's rank, effective level, intelligence and
/// willpower, stored magick points), the spell, the forced flag and the spell's willpower minimum (from the loaded table, or the
/// defaults), with recording rank and magick point setters. Opt-in like <see cref="ExperienceOriginalExeTests"/>: inconclusive
/// without <c>ARCANUM_EXE</c> or the oracle's file; planted mutants of the C# side must each be caught.
/// </summary>
[TestClass]
public sealed class SpellBookOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] Fields =
        ["spell", "force", "type", "known", "level", "in", "wp", "magick", "loaded", "minimum", "result", "writes", "rankWritten", "magickWritten"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<LearnCase, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void LearnMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B1790", "spell-learn", TestContext);
        Run(file, c => Outcome(c, SpellBookRules.Learn(Book(c), c.Spell, c.Force, c.Level, c.Intelligence, c.Willpower, Minimum(c))),
            c => $"{c.Result} {c.Writes} {c.RankWritten} {c.MagickWritten}",
            new Mutant("a forced learn adds one magick point", c => Variant(c, forcedPoints: false, strictWillpower: false, forcedOrder: false)),
            new("willpower must exceed the minimum", c => Variant(c, forcedPoints: true, strictWillpower: true, forcedOrder: false)),
            new("a forced learn keeps the rank order", c => Variant(c, forcedPoints: true, strictWillpower: false, forcedOrder: true)));
    }

    private static SpellBook Book(LearnCase c) => SpellBook.Empty with { Ranks = SpellBook.Empty.Ranks.SetItem(c.Spell / 5, c.Known) };

    private static int Minimum(LearnCase c) => c.Loaded ? c.Minimum : AutoLevelRules.DefaultSpellMinimumWillpower(c.Spell);

    private static string Outcome(LearnCase c, SpellBookChange change) => change.Changed
        ? $"1 1 {change.Book.Ranks[c.Spell / 5]} {c.Magick + change.MagickPoints}"
        : $"{(change.Succeeded ? 1 : 0)} 0 -1 -1";

    // A planted variant of the learn, written apart from SpellBookRules.
    private static string Variant(LearnCase c, bool forcedPoints, bool strictWillpower, bool forcedOrder)
    {
        var rank = c.Spell % 5 + 1;
        if (c.Force && !forcedOrder)
            return rank <= c.Known ? "1 0 -1 -1" : $"1 1 {rank} {c.Magick + (forcedPoints ? rank - c.Known : 1)}";
        int[] levels = [1, 1, 5, 10, 15];
        var refused = rank != c.Known + 1 || levels[c.Spell % 5] > c.Level || c.Intelligence < 5 ||
            (strictWillpower ? Minimum(c) >= c.Willpower : Minimum(c) > c.Willpower);
        return refused ? "0 0 -1 -1" : $"1 1 {rank} {c.Magick + 1}";
    }

    private void Run(OracleFile file, Func<LearnCase, string> real, Func<LearnCase, string> original, params Mutant[] mutants)
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

    /// <summary>One oracle input and the original's answer: its result, how many rank writes it made, the rank and the magick
    /// points it wrote (−1 without a write).</summary>
    internal sealed record LearnCase(int Spell, bool Force, int Type, int Known, int Level, int Intelligence, int Willpower, int Magick,
        bool Loaded, int Minimum, int Result, int Writes, int RankWritten, int MagickWritten)
    {
        public override string ToString() =>
            $"spell {Spell} force {Force} known {Known} level {Level} in {Intelligence} wp {Willpower} magick {Magick} loaded {Loaded} minimum {Minimum}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<LearnCase> Cases)
    {
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
            var cases = new List<LearnCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                var v = row.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                cases.Add(new(v[0], v[1] != 0, v[2], v[3], v[4], v[5], v[6], v[7], v[8] != 0, v[9], v[10], v[11], v[12], v[13]));
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
