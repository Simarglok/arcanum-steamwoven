using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>
/// ARC-DEV-007: <see cref="ExperienceRules"/> against the retail 1.0.7.4 executable, on the seeded inputs and outputs the
/// function oracle (<c>tools/original-oracle</c>) wrote to a local folder outside git. Opt-in: without <c>ARCANUM_EXE</c> or
/// the oracle's files the tests are inconclusive. Every run also replays planted mutants of the C# side, and fails unless
/// the same inputs catch each of them.
/// </summary>
[TestClass]
public sealed class ExperienceOriginalExeTests
{
    public const string Category = "OriginalExe";
    /// <summary>The retail GOG 1.0.7.4 <c>Arcanum.exe</c> (the oracle refuses any other file).</summary>
    public const string ExeSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";
    public const int MinimumCases = 10_000;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<OracleCase, int> Compute);

    [TestMethod, TestCategory(Category)]
    public void ToNextLevelMatchesTheOriginal()
    {
        var file = OracleFile.Load("4A68B0", "experience-to-next-level", TestContext);
        int Real(OracleCase c) => ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        Run(file, Real, null,
            new("no next level from level 50", c => c.Level >= ExperienceRules.MaxLevel - 1 ? ExperienceRules.NoNextLevel : Real(c)),
            new("negative remainder clamped to 0", c => Math.Max(0, Real(c))));
    }

    [TestMethod, TestCategory(Category)]
    public void ProgressMatchesTheOriginal()
    {
        var file = OracleFile.Load("4A6900", "experience-progress", TestContext);
        int Real(OracleCase c) => ExperienceRules.Progress(c.Table!, c.Level, c.Experience, ExperienceRuleProfile.Original);
        Assert.IsTrue(file.Cases.Any(IsXp3), "the inputs must reach the XP3 range");
        Assert.IsTrue(file.Cases.Any(c => !IsXp3(c) && c.Level is >= 1 and < ExperienceRules.MaxLevel), "the inputs must reach the ordinary range");
        // The Original mode is compared on every input; the Steamwoven mode (ARC-WORLD-033) may differ only on XP3 inputs.
        Run(file, Real, new("steamwoven", "XP3", IsXp3, c => ExperienceRules.Progress(c.Table!, c.Level, c.Experience, ExperienceRuleProfile.Steamwoven)),
            new Mutant("rounded instead of truncated", c => RoundedProgress(c) ?? Real(c)),
            new("fallback when the next level is just reached", c => c.Level is >= 1 and < ExperienceRules.MaxLevel
                && ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience) == 0 ? ExperienceRules.ProgressFallback : Real(c)),
            new("level 0 answers 0", c => c.Level == 0 ? 0 : Real(c)),
            new("the exact product (the Steamwoven mode)", c => ExperienceRules.Progress(c.Table!, c.Level, c.Experience, ExperienceRuleProfile.Steamwoven)));
    }

    [TestMethod, TestCategory(Category)]
    public void BonusPointsMatchTheOriginal()
    {
        var file = OracleFile.Load("4A6980", "level-bonus-points", TestContext);
        int Real(OracleCase c) => ExperienceRules.BonusPoints(c.From, c.To);
        Run(file, Real, null,
            new("every fourth level", c => Enumerable.Range(c.From + 1, Math.Max(0, c.To - c.From)).Sum(l => l % 4 == 0 ? 2 : 1)),
            new("the starting level counted", c => Real(c) + (c.From <= c.To ? (c.From % 5 == 0 ? 2 : 1) : 0)));
    }

    /// <summary>XP3 (ledger): 1000 × remaining does not fit in 32 bits; the Original mode wraps it, the Steamwoven mode does not.</summary>
    private static bool IsXp3(OracleCase c)
    {
        if (c.Level is < 1 or >= ExperienceRules.MaxLevel) return false;
        var remaining = ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        return remaining >= 0 && 1000L * remaining > int.MaxValue;
    }

    private static int? RoundedProgress(OracleCase c)
    {
        if (c.Level is < 1 or >= ExperienceRules.MaxLevel) return null;
        var remaining = ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        if (remaining < 0) return null;
        var step = c.Table!.Threshold(c.Level + 1) - c.Table.Threshold(c.Level);
        return 1000 - (int)Math.Round(1000.0 * remaining / step, MidpointRounding.AwayFromZero);
    }

    /// <summary>The other rule mode of a function with a rule-mode switch: it may differ from the original only on the inputs
    /// its ledger row <see cref="Applies"/> to.</summary>
    private sealed record OtherMode(string Mode, string Ledger, Func<OracleCase, bool> Applies, Func<OracleCase, int> Compute);

    /// <summary>Compares the C# side (its Original mode, where there is a switch) with the original on every input, checks
    /// that the other mode differs only on its ledger row's inputs, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<OracleCase, int> real, OtherMode? other, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        var unexpected = file.Cases.Where(c => real(c) != c.Result).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => m.Compute(c) != c.Result)).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var ledgerInputs = other is null ? 0 : file.Cases.Count(other.Applies);
        var otherDifferences = other is null ? [] : file.Cases.Where(c => other.Compute(c) != c.Result).ToList();
        var outsideLedger = otherDifferences.Where(c => !other!.Applies(c)).ToList();
        var pass = unexpected.Count == 0 && missed.Count == 0 && outsideLedger.Count == 0;
        var method = OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  differences: {unexpected.Count}" + (other is null ? "" : $" (original mode); {other.Mode} mode: "
            + $"{otherDifferences.Count} differences among the {ledgerInputs} {other.Ledger} inputs, {outsideLedger.Count} outside them"));
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, other, ledgerInputs, otherDifferences.Count, outsideLedger.Count, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
        {
            var c = unexpected[0];
            Assert.Fail($"{unexpected.Count} differences from the original; first: {c.Describe()} original {c.Result}, Steamwoven{(other is null ? "" : " (original mode)")} {real(c)}");
        }
        if (outsideLedger.Count > 0)
            Assert.Fail($"the {other!.Mode} mode differs outside {other.Ledger}: {outsideLedger[0].Describe()} original {outsideLedger[0].Result}");
        Assert.AreEqual(0, missed.Count, "planted mutants not caught (the inputs do not cover them): " + string.Join(", ", missed));
    }

    /// <summary>The method marked <see cref="OriginalFunctionAttribute"/> with this oracle function's address.</summary>
    internal static MethodInfo OriginalMethod(string key)
    {
        var address = Convert.ToUInt32(key, 16);
        var methods = typeof(ExperienceRules).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<OriginalFunctionAttribute>()?.Address == address)
            .ToList();
        Assert.AreEqual(1, methods.Count, $"exactly one method must carry OriginalFunction(0x{key})");
        return methods[0];
    }

    /// <summary>ARC-DEV-008: the oracle receipt (docs/verification/receipts), written by <c>./arcanum oracle</c>.</summary>
    private void WriteReceipt(OracleFile file, MethodInfo method, OtherMode? other, int ledgerInputs, int otherDifferences,
        int outsideLedger, int planted, int caught, int differences, bool pass)
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
            ["otherMode"] = other is null ? null : new Dictionary<string, object>
            {
                ["mode"] = other.Mode, ["ledger"] = other.Ledger, ["ledgerInputs"] = ledgerInputs, ["differ"] = otherDifferences,
                ["outsideLedger"] = outsideLedger,
            },
            ["result"] = pass ? "pass" : "fail",
            ["exeSha256"] = file.ExeSha256,
            ["ilSha256"] = MethodIlHash.Compute(method),
            ["configuration"] = MethodIlHash.Configuration(method),
        };
        var path = Path.Combine(folder, file.Key + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
        TestContext.WriteLine($"  receipt {path}");
    }

    internal sealed record OracleCase(ExperienceTable? Table, int TableIndex, int Level, int Experience, int From, int To, int Result)
    {
        public string Describe() => Table is null ? $"from {From} to {To}:" : $"table {TableIndex} level {Level} experience {Experience}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<OracleCase> Cases)
    {
        /// <summary>The oracle's file for <paramref name="key"/>, or an inconclusive result without <c>ARCANUM_EXE</c> or the file
        /// (a failure instead when <c>ARCANUM_ORACLE_REQUIRED=1</c>, as <c>./arcanum oracle</c> sets it).</summary>
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
            Assert.IsTrue(count >= MinimumCases, $"the oracle file holds {count} inputs; at least {MinimumCases} are required.");

            var cases = new List<OracleCase>(count);
            if (root.TryGetProperty("tables", out var tableRows))
            {
                CollectionAssert.AreEqual(new[] { "table", "level", "experience", "result" }, Fields(root));
                var tables = new List<ExperienceTable>();
                foreach (var row in tableRows.EnumerateArray())
                {
                    var table = ExperienceTable.Create(row.EnumerateArray().Select(v => v.GetInt32()).ToArray());
                    Assert.IsNotNull(table, "an oracle table is outside ExperienceTable's admission");
                    tables.Add(table);
                }
                foreach (var row in rows.EnumerateArray())
                {
                    var index = row[0].GetInt32();
                    cases.Add(new(tables[index], index, row[1].GetInt32(), row[2].GetInt32(), 0, 0, row[3].GetInt32()));
                }
            }
            else
            {
                CollectionAssert.AreEqual(new[] { "from", "to", "result" }, Fields(root));
                foreach (var row in rows.EnumerateArray())
                    cases.Add(new(null, -1, 0, 0, row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()));
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

        private static string[] Fields(JsonElement root) => root.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray();

        private static string? RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Arcanum.Steamwoven.sln"))) return directory.FullName;
            return null;
        }
    }
}
