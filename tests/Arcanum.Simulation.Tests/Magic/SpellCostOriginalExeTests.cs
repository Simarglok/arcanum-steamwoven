using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>
/// ARC-MAGIC-003: <see cref="SpellCostRules"/> against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>, model <c>spell-cost</c>): one caster (object type, race, mastered college), one spell
/// record's cost, the cheat flag, and a recording payment stub whose arguments are the run-time and check costs. Opt-in like
/// <see cref="ExperienceOriginalExeTests"/>: inconclusive without <c>ARCANUM_EXE</c> or the oracle's files; planted mutants of the
/// C# side must each be caught.
/// </summary>
[TestClass]
public sealed class SpellCostOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] Fields =
        ["spell", "cost", "mastery", "type", "race", "cheat", "payAnswer", "result", "payCalls", "paidCost", "paidApply", "paidSpell"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<CostCase, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void RunCostMatchesTheOriginal()
    {
        var file = OracleFile.Load("4507D0", "spell-run-cost", TestContext);
        Run(file, c => Paid(c, SpellCostRules.RunCost(c.Cost, c.Spell, Caster(c), c.Cheat), apply: 1), Original,
            new Mutant("mastery rounds down", c => Paid(c, Variant(c, mastery: true, roundUp: false, critterDwarf: true), 1)),
            new("the cheat after the dwarf doubling", c => Paid(c, c.Cheat ? 1 : Variant(c, mastery: true, roundUp: true, critterDwarf: true), 1)),
            new("a dwarf of any object type", c => Paid(c, Variant(c, mastery: true, roundUp: true, critterDwarf: false), 1)),
            new("no mastery", c => Paid(c, Variant(c, mastery: false, roundUp: true, critterDwarf: true), 1)));
    }

    [TestMethod, TestCategory(Category)]
    public void CheckCostMatchesTheOriginal()
    {
        var file = OracleFile.Load("4508A0", "spell-check-cost", TestContext);
        Run(file, c => Paid(c, SpellCostRules.CheckCost(c.Cost, Caster(c), c.Cheat), apply: 0), Original,
            new Mutant("mastery applied", c => Paid(c, Variant(c, mastery: true, roundUp: true, critterDwarf: true), 0)),
            new("a dwarf of any object type", c => Paid(c, Variant(c, mastery: false, roundUp: true, critterDwarf: false), 0)),
            new("payment applied", c => Paid(c, SpellCostRules.CheckCost(c.Cost, Caster(c), c.Cheat), 1)));
    }

    [TestMethod, TestCategory(Category)]
    public void CastCostMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B1660", "spell-cast-cost", TestContext);
        Run(file, c => $"{SpellCostRules.CastCost(c.Cost, c.Spell, Caster(c))} 0", c => $"{c.Result} {c.PayCalls}",
            new Mutant("mastery rounds up", c => $"{Cast(c, roundUp: true, critterDwarf: false, masteryFirst: false)} 0"),
            new("a dwarf only as a critter", c => $"{Cast(c, roundUp: false, critterDwarf: true, masteryFirst: false)} 0"),
            new("mastery before the dwarf", c => $"{Cast(c, roundUp: false, critterDwarf: false, masteryFirst: true)} 0"));
    }

    private static SpellCaster Caster(CostCase c) => new(c.Type is 15 or 16, c.Race, c.Mastery);

    private static string Paid(CostCase c, int cost, int apply) => $"1 {cost} {apply} {c.Spell} {c.PayAnswer}";

    private static string Original(CostCase c) => $"{c.PayCalls} {c.PaidCost} {c.PaidApply} {c.PaidSpell} {c.Result}";

    // Planted variants of the formulas, written apart from SpellCostRules.
    private static int Variant(CostCase c, bool mastery, bool roundUp, bool critterDwarf)
    {
        var cost = c.Cost;
        if (mastery && c.Spell is >= 0 and < 80 && c.Mastery == c.Spell / 5)
            cost = cost / 2 + (roundUp && (cost & 1) != 0 ? 1 : 0);
        if (c.Cheat) cost = 1;
        return c.Race == 1 && (!critterDwarf || c.Type is 15 or 16) ? unchecked(cost * 2) : cost;
    }

    private static int Cast(CostCase c, bool roundUp, bool critterDwarf, bool masteryFirst)
    {
        var cost = c.Cost;
        var mastered = c.Mastery == c.Spell / 5;
        var dwarf = c.Race == 1 && (!critterDwarf || c.Type is 15 or 16);
        if (masteryFirst && mastered) cost /= 2;
        if (dwarf) cost = unchecked(cost * 2);
        if (!masteryFirst && mastered) cost = cost / 2 + (roundUp && (cost & 1) != 0 ? 1 : 0);
        return cost;
    }

    /// <summary>Compares the C# side with the original on every input, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<CostCase, string> real, Func<CostCase, string> original, params Mutant[] mutants)
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

    /// <summary>One oracle input: the spell, its cost, the caster's mastered college, object type and race, the cheat flag and
    /// the payment stub's answer; then the original's result, how often it paid and the payment's arguments.</summary>
    internal sealed record CostCase(int Spell, int Cost, int Mastery, int Type, int Race, bool Cheat, int PayAnswer, int Result, int PayCalls,
        int PaidCost, int PaidApply, int PaidSpell)
    {
        public override string ToString() => $"spell {Spell} cost {Cost} mastery {Mastery} type {Type} race {Race} cheat {Cheat} pay {PayAnswer}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<CostCase> Cases)
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
            var cases = new List<CostCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                var v = row.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                cases.Add(new(v[0], v[1], v[2], v[3], v[4], v[5] != 0, v[6], v[7], v[8], v[9], v[10], v[11]));
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
