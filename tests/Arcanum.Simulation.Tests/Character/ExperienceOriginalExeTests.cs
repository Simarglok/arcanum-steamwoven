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
    public void ProgressMatchesTheOriginalExceptTheXp3Wrap()
    {
        var file = OracleFile.Load("4A6900", "experience-progress", TestContext);
        int Real(OracleCase c) => ExperienceRules.Progress(c.Table!, c.Level, c.Experience);
        Assert.IsTrue(file.Cases.Any(IsXp3), "the inputs must reach the XP3 range");
        Assert.IsTrue(file.Cases.Any(c => !IsXp3(c) && c.Level is >= 1 and < ExperienceRules.MaxLevel), "the inputs must reach the ordinary range");
        // XP3 inputs are not compared with Steamwoven (until ARC-WORLD-032), but the original must give the progress with a
        // wrapping 32-bit 1000 × remaining there (the ledger's description), from the C# remainder and step.
        Run(file, Real, new("XP3", IsXp3, WrappedProgress),
            new("rounded instead of truncated", c => RoundedProgress(c) ?? Real(c)),
            new("fallback when the next level is just reached", c => c.Level is >= 1 and < ExperienceRules.MaxLevel
                && ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience) == 0 ? ExperienceRules.ProgressFallback : Real(c)),
            new("level 0 answers 0", c => c.Level == 0 ? 0 : Real(c)));
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

    /// <summary>XP3 (ledger, until ARC-WORLD-032): the original's 1000 × remaining is a 32-bit product, Steamwoven's exact.</summary>
    private static bool IsXp3(OracleCase c)
    {
        if (c.Level is < 1 or >= ExperienceRules.MaxLevel) return false;
        var remaining = ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        return remaining >= 0 && 1000L * remaining > int.MaxValue;
    }

    private static int WrappedProgress(OracleCase c)
    {
        var remaining = ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        var step = c.Table!.Threshold(c.Level + 1) - c.Table.Threshold(c.Level);
        return unchecked(1000 - unchecked(1000 * remaining) / step);
    }

    private static int? RoundedProgress(OracleCase c)
    {
        if (c.Level is < 1 or >= ExperienceRules.MaxLevel) return null;
        var remaining = ExperienceRules.ToNextLevel(c.Table!, c.Level, c.Experience);
        if (remaining < 0) return null;
        var step = c.Table!.Threshold(c.Level + 1) - c.Table.Threshold(c.Level);
        return 1000 - (int)Math.Round(1000.0 * remaining / step, MidpointRounding.AwayFromZero);
    }

    /// <summary>A recorded difference: on the inputs it <see cref="Applies"/> to, Steamwoven is not compared, and the original
    /// must give <see cref="Original"/> (the ledger's description of its behaviour).</summary>
    private sealed record ExpectedDifference(string Ledger, Func<OracleCase, bool> Applies, Func<OracleCase, int> Original);

    /// <summary>Compares the C# side with the original on every input outside the recorded difference, checks the original
    /// against the recorded difference's description on the others, then replays each mutant, which must differ on at least
    /// one compared input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<OracleCase, int> real, ExpectedDifference? expected, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        bool Recorded(OracleCase c) => expected?.Applies(c) == true;
        var unexpected = file.Cases.Where(c => !Recorded(c) && real(c) != c.Result).ToList();
        var recorded = file.Cases.Where(Recorded).ToList();
        var recordedDifferences = recorded.Count(c => real(c) != c.Result);
        var unexplained = recorded.Where(c => expected!.Original(c) != c.Result).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => !Recorded(c) && m.Compute(c) != c.Result)).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var pass = unexpected.Count == 0 && unexplained.Count == 0 && missed.Count == 0;
        var method = OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  differences: {unexpected.Count} unexpected" + (expected is null ? "" :
            $"; {recordedDifferences} of {recorded.Count} {expected.Ledger} inputs differ (recorded, not failed), "
            + $"{recorded.Count - unexplained.Count}/{recorded.Count} match the recorded behaviour"));
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, expected, recorded.Count, recordedDifferences, unexplained.Count, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
        {
            var c = unexpected[0];
            Assert.Fail($"{unexpected.Count} differences from the original; first: {c.Describe()} original {c.Result}, Steamwoven {real(c)}");
        }
        if (unexplained.Count > 0)
            Assert.Fail($"{expected!.Ledger} input not explained by the recorded behaviour: {unexplained[0].Describe()} original {unexplained[0].Result}");
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
    private void WriteReceipt(OracleFile file, MethodInfo method, ExpectedDifference? expected, int recorded, int recordedDifferences,
        int unexplained, int planted, int caught, int differences, bool pass)
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
            ["recordedDifference"] = expected is null ? null : new Dictionary<string, object>
            {
                ["ledger"] = expected.Ledger, ["inputs"] = recorded, ["differ"] = recordedDifferences, ["unexplained"] = unexplained,
            },
            ["result"] = pass ? "pass" : "fail",
            ["exeSha256"] = file.ExeSha256,
            ["ilSha256"] = MethodIlHash.Compute(method),
            ["configuration"] = MethodIlHash.Configuration(method),
        };
        var path = Path.Combine(folder, file.Key + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }) + "\n");
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
