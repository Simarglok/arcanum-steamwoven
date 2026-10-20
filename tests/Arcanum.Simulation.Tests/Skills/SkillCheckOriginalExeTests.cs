using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

/// <summary>
/// ARC-SKILLS-003: <see cref="SkillCheckRules"/> effectiveness against the retail 1.0.7.4 executable on the function oracle's
/// seeded inputs (<c>tools/original-oracle</c>, model <c>effectiveness</c>): a source and an optional target with their skill
/// levels, the target's perception, intelligence and Magnetic Inversion flag, the source's extraordinary intelligence, the
/// local PC and the game difficulty setting. Opt-in like <see cref="ExperienceOriginalExeTests"/>: inconclusive without
/// <c>ARCANUM_EXE</c> or the oracle's files; planted mutants of the C# side must each be caught.
/// </summary>
[TestClass]
public sealed class SkillCheckOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] Fields =
        ["skill", "level", "target", "targetLevel", "targetPe", "targetIn", "magneticInversion", "otherFlags", "extraordinary", "local", "difficulty", "result"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<EffectivenessCase, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void BasicEffectivenessMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C62E0", "basic-skill-effectiveness", TestContext);
        Run(file, c => Basic(c), Original,
            new Mutant("extraordinary intelligence ignored", c => Basic(c with { Extraordinary = false })),
            new("the difficulty for every critter", c => Basic(c with { Local = 0 })),
            new("easy and hard swapped", c => Basic(c with { Difficulty = Swapped(c.Difficulty) })),
            new("the opposed table one entry off", c => Basic(c, levelShift: 1)));
    }

    [TestMethod, TestCategory(Category)]
    public void OpposedEffectivenessMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C6410", "opposed-skill-effectiveness", TestContext);
        Run(file, c => SkillCheckRules.Opposed(c.Level, OpponentValue(c)).ToString(), Original,
            new Mutant("intelligence and perception swapped", c => SkillCheckRules.Opposed(c.Level, OpponentValue(c, swapStats: true)).ToString()),
            new("the table one entry off", c => SkillCheckRules.Opposed(Math.Min(c.Level + 1, 20), OpponentValue(c)).ToString()),
            new("no target reads like a level-0 opponent", c => SkillCheckRules.Opposed(c.Level, OpponentValue(c) ?? 0).ToString()));
    }

    [TestMethod, TestCategory(Category)]
    public void TechEffectivenessMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C69F0", "tech-skill-effectiveness", TestContext);
        Run(file, c => Tech(c), Original,
            new Mutant("Magnetic Inversion ignored", c => Tech(c with { MagneticInversion = false })),
            new("the difficulty for every critter", c => Tech(c with { Local = 0 })),
            new("disarm traps 4L + 20 at level 0", c => TechVariant(c, disarmZeroAtZero: false, capFirearms: false)),
            new("firearms capped at 95", c => TechVariant(c, disarmZeroAtZero: true, capFirearms: true)));
    }

    private static string Original(EffectivenessCase c) => c.Result.ToString();

    // The source's difficulty setting: 0 easy, 2 hard, anything else changes nothing.
    private static GameDifficulty Difficulty(int setting) => setting switch { 0 => GameDifficulty.Easy, 2 => GameDifficulty.Hard, _ => GameDifficulty.Normal };

    private static int Swapped(int setting) => setting switch { 0 => 2, 2 => 0, _ => setting };

    private static SkillEffectivenessFacts Facts(EffectivenessCase c, int? opponent) => new()
    {
        OpponentValue = opponent,
        TargetMagneticInversion = c.Target && c.MagneticInversion,
        ExtraordinaryIntelligence = c.Extraordinary,
        LocalPc = c.Local == 0,
        Difficulty = Difficulty(c.Difficulty),
    };

    // The opposed value the source reads: the target's perception, intelligence or level of the same skill by the skill's flag.
    private static int? OpponentValue(EffectivenessCase c, bool swapStats = false)
    {
        if (!c.Target) return null;
        return SkillCheckRules.Opponent(SkillRules.Basic(c.Skill)) switch
        {
            SkillOpponent.Perception => swapStats ? c.TargetIn : c.TargetPe,
            SkillOpponent.Intelligence => swapStats ? c.TargetPe : c.TargetIn,
            _ => c.TargetLevel,
        };
    }

    private static string Basic(EffectivenessCase c, int levelShift = 0)
    {
        var skill = SkillRules.Basic(c.Skill);
        var level = SkillCheckRules.Opponent(skill) == SkillOpponent.None ? c.Level : Math.Min(c.Level + levelShift, 20);
        return SkillCheckRules.Effectiveness(skill, level, Facts(c, OpponentValue(c))).ToString();
    }

    private static string Tech(EffectivenessCase c) => SkillCheckRules.Effectiveness(SkillRules.Tech(c.Skill), c.Level, Facts(c, null)).ToString();

    // A planted variant of the tech formulas, written apart from SkillCheckRules.
    private static string TechVariant(EffectivenessCase c, bool disarmZeroAtZero, bool capFirearms)
    {
        var l = c.Level;
        var value = c.Skill switch { 0 => 4 * l, 1 => 5 * l + 25, 2 => 6 * l, _ => l == 0 && disarmZeroAtZero ? 0 : 4 * l + 20 };
        if (c.Skill == 1 && c.Target && c.MagneticInversion) value -= 20;
        if (c.Extraordinary) value += 10;
        if (c.Local == 0) value += c.Difficulty == 0 ? value / 2 : c.Difficulty == 2 ? -(value / 4) : 0;
        if ((c.Skill != 1 || capFirearms) && value > 95) value = 95;
        return Math.Max(0, value).ToString();
    }

    /// <summary>Compares the C# side with the original on every input, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<EffectivenessCase, string> real, Func<EffectivenessCase, string> original, params Mutant[] mutants)
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

    /// <summary>One oracle input: the skill (basic 0..11 or tech 0..3), the source's level, whether there is a target, its level
    /// of the same skill, perception, intelligence, Magnetic Inversion and other spell flags, the source's extraordinary
    /// intelligence, the local PC (0 the source, 1 another PC, 2 none), the difficulty setting, and the original's answer.</summary>
    internal sealed record EffectivenessCase(int Skill, int Level, bool Target, int TargetLevel, int TargetPe, int TargetIn, bool MagneticInversion,
        bool OtherFlags, bool Extraordinary, int Local, int Difficulty, int Result)
    {
        public override string ToString() =>
            $"skill {Skill} level {Level} target {Target} ({TargetLevel}, PE {TargetPe}, IN {TargetIn}, MI {MagneticInversion}) extraordinary {Extraordinary} local {Local} difficulty {Difficulty}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<EffectivenessCase> Cases)
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
            var cases = new List<EffectivenessCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                var v = row.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                cases.Add(new(v[0], v[1], v[2] != 0, v[3], v[4], v[5], v[6] != 0, v[7] != 0, v[8] != 0, v[9], v[10], v[11]));
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
