using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

/// <summary>
/// ARC-SKILLS-001: <see cref="SkillRules"/> against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>, models <c>skill-level</c> and <c>training-set</c>): one critter with its twelve stored basic
/// skills, its eight primary stats, the monstrous flag and an additive effect change of the skill. Opt-in like
/// <see cref="ExperienceOriginalExeTests"/>: inconclusive without <c>ARCANUM_EXE</c> or the oracle's files; planted mutants of
/// the C# side must each be caught.
/// </summary>
[TestClass]
public sealed class SkillOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] CritterFields = ["skill", "stored", "monstrous", "effect", "st", "dx", "cn", "be", "in", "pe", "wp", "ch"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<SkillCase, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void SkillLevelMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C5EB0", "basic-skill-level", TestContext);
        Run(file, Level, c => c.Result.ToString(),
            new Mutant("dexterity for every skill", c => Level(c with { Stats = [.. Enumerable.Repeat(c.Stats[1], 8)] })),
            new("no 0..20 clamp", c => Math.Min(Base(c) + c.Effect, SkillRules.KeyStatCap(KeyStat(c))).ToString()),
            new("no monstrous melee", c => Level(c with { Monstrous = false })),
            new("effects ignored", c => Level(c with { Effect = 0 })));
    }

    [TestMethod, TestCategory(Category)]
    public void TrainingSetMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C6170", "basic-skill-training-set", TestContext);
        Run(file, Training, c => $"{c.Result} {c.Written} {c.Educated}",
            new Mutant("raising skips tiers", c => Rule(c, LevelOf(c), oneStep: false, sameTierWrites: true, targetMinimum: true)),
            new("the minimum of the stored tier", c => Rule(c, LevelOf(c), oneStep: true, sameTierWrites: true, targetMinimum: false)),
            new("the base level instead of the effective level", c => Rule(c, Math.Min(SkillRules.BaseLevel(c.Stored), 20), true, true, true)),
            new("the same tier not written again", c => Rule(c, LevelOf(c), oneStep: true, sameTierWrites: false, targetMinimum: true)));
    }

    private static int KeyStat(SkillCase c) => c.Stats[(int)SkillRules.KeyStat(SkillRules.Basic(c.Skill))];

    private static int Base(SkillCase c) => c.Skill == 2 && c.Monstrous ? SkillRules.MonstrousMeleeLevel : SkillRules.BaseLevel(c.Stored);

    private static int LevelOf(SkillCase c) => SkillRules.Level(SkillRules.Basic(c.Skill), c.Stored, KeyStat(c), c.Monstrous,
        c.Effect == 0 ? null : [new EffectModifier(StatModifierOperator.Add, c.Effect)]);

    private static string Level(SkillCase c) => LevelOf(c).ToString();

    private static string Training(SkillCase c)
    {
        var change = SkillRules.SetTraining(SkillRules.Basic(c.Skill), c.Stored, (SkillTraining)c.Training, LevelOf(c));
        var changed = change.Status == SkillChangeStatus.Changed;
        return $"{(int)change.Training} {(changed ? change.Stored : -1)} {(changed ? 1 : 0)}";
    }

    // A planted variant of the training setter, written apart from SkillRules.
    private static string Rule(SkillCase c, int level, bool oneStep, bool sameTierWrites, bool targetMinimum)
    {
        if (c.Training is < 0 or > 3) return "0 -1 0";
        var current = (c.Stored >> 6) & 3;
        if (oneStep && c.Training > current && c.Training - 1 != current) return $"{current} -1 0";
        if (!sameTierWrites && c.Training == current) return $"{current} -1 0";
        if (level < SkillRules.MinimumLevel((SkillTraining)(targetMinimum ? c.Training : current))) return $"{current} -1 0";
        return $"{c.Training} {(c.Stored & ~0xC0) | (c.Training << 6)} 1";
    }

    /// <summary>Compares the C# side with the original on every input, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<SkillCase, string> real, Func<SkillCase, string> original, params Mutant[] mutants)
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

    /// <summary>One oracle input: the skill, its stored value, the monstrous flag, the effect change, the eight primary stats,
    /// and the original's answer (the level; or the tier returned, the value written or −1 and whether the Educator update ran,
    /// for a requested training).</summary>
    internal sealed record SkillCase(int Skill, int Stored, bool Monstrous, int Effect, int[] Stats, int Training, int Result, int Written, int Educated)
    {
        public override string ToString() => $"skill {Skill} stored {Stored} monstrous {Monstrous} effect {Effect} stats [{string.Join(",", Stats)}] training {Training}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<SkillCase> Cases)
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
            var training = root.GetProperty("fields").GetArrayLength() > CritterFields.Length + 1;
            string[] tail = training ? ["training", "result", "written", "educated"] : ["result"];
            CollectionAssert.AreEqual(CritterFields.Concat(tail).ToArray(), root.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray());
            var cases = new List<SkillCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                var v = row.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                var stats = v[4..12];
                cases.Add(training ? new(v[0], v[1], v[2] != 0, v[3], stats, v[12], v[13], v[14], v[15]) : new(v[0], v[1], v[2] != 0, v[3], stats, 0, v[12], 0, 0));
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
