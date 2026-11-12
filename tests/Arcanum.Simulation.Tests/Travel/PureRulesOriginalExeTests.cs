using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Simulation.Travel;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Magic.SpellResistanceOriginalExeTests;

namespace Arcanum.Simulation.Tests.Travel;

/// <summary>
/// ARC-DEV-1105 (V3 of the function audit plan): ten pure numeric functions of the retail 1.0.7.4 executable against Steamwoven's
/// methods, on the function oracle's seeded inputs (<c>tools/original-oracle</c>, the <c>pure</c> models: no world model, every
/// input an argument or a table value of the image): the tile, sector and window directions, the Chebyshev location distance and
/// window estimate, the stat cost, the key stat cap and minimum, and the key stat of a basic and a tech skill. No rule-mode
/// switch touches them, so Steamwoven must match on every input. Opt-in like <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class PureRulesOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, long> Compute);

    // ---------- geometry ----------

    [TestMethod, TestCategory(Category)]
    public void DistanceMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B96F0", "location-distance", TestContext);
        Run(file, c => WorldTravelGeometry.Distance(Location(c, "a"), Location(c, "b")), c => ((long)(uint)c["high"] << 32) | (uint)c["low"],
            new Mutant("the smaller difference", c => Math.Min(Math.Abs(U(c, "ax") - U(c, "bx")), Math.Abs(U(c, "ay") - U(c, "by")))),
            new("the sum of the differences", c => Math.Abs(U(c, "ax") - U(c, "bx")) + Math.Abs(U(c, "ay") - U(c, "by"))),
            new("32-bit wrapping differences", c => Math.Max(Math.Abs(unchecked((int)((uint)c["ax"] - (uint)c["bx"]))),
                Math.Abs(unchecked((int)((uint)c["ay"] - (uint)c["by"]))))));
    }

    [TestMethod, TestCategory(Category)]
    public void RotationMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B8D50", "tile-direction", TestContext);
        Run(file, c => LineOfFireRules.Rotation(new CellCoord(c["ax"], c["ay"]), new CellCoord(c["bx"], c["by"])), c => c["result"],
            new Mutant("the signs alone", c => Octant(Dx(c), Dy(c), (_, _) => false)),
            new("the minor axis dropped when twice it only equals the major", c => Octant(Dx(c), Dy(c), (minor, major) => 2 * minor <= major)),
            new("the minor axis dropped at any smaller size", c => Octant(Dx(c), Dy(c), (minor, major) => minor < major)),
            new("the axes swapped", c => Octant(Dy(c), Dx(c), (minor, major) => 2 * minor < major)));
    }

    [TestMethod, TestCategory(Category)]
    public void SectorDirectionMatchesTheOriginal()
    {
        var file = OracleFile.Load("4CFCD0", "sector-direction", TestContext);
        Run(file, c => WorldTravelGeometry.Direction(new TravelSector(c["ax"], c["ay"]), new TravelSector(c["bx"], c["by"])), c => c["result"],
            new Mutant("the signs alone", c => Octant(Dx(c), Dy(c), (_, _) => false)),
            new("the minor axis dropped when twice it only equals the major", c => Octant(Dx(c), Dy(c), (minor, major) => 2 * minor <= major)),
            new("the minor axis dropped at any smaller size", c => Octant(Dx(c), Dy(c), (minor, major) => minor < major)),
            new("the axes swapped", c => Octant(Dy(c), Dx(c), (minor, major) => 2 * minor < major)));
    }

    [TestMethod, TestCategory(Category)]
    public void EstimateMatchesTheOriginal()
    {
        var file = OracleFile.Load("4200C0", "window-estimate", TestContext);
        Run(file, c => WorldRoute.Estimate(c["from"], c["to"]), c => c["result"],
            new Mutant("the sum of the cell differences", c => 10 * (Math.Abs(c["from"] % 16 - c["to"] % 16) + Math.Abs(c["from"] / 16 - c["to"] / 16))),
            new("the smaller cell difference", c => 10 * Math.Min(Math.Abs(c["from"] % 16 - c["to"] % 16), Math.Abs(c["from"] / 16 - c["to"] / 16))),
            new("a step cost of 8", c => 8 * Math.Max(Math.Abs(c["from"] % 16 - c["to"] % 16), Math.Abs(c["from"] / 16 - c["to"] / 16))));
    }

    [TestMethod, TestCategory(Category)]
    public void WindowDirectionMatchesTheOriginal()
    {
        var file = OracleFile.Load("420110", "window-direction", TestContext);
        Run(file, c => WorldRoute.WindowDirection(c["from"], c["to"]), c => c["result"],
            new Mutant("the sector direction's minor-axis rule (what the C# side did before the oracle)",
                c => WorldTravelGeometry.Direction(new(c["from"] % 16, c["from"] / 16), new(c["to"] % 16, c["to"] / 16))),
            new("the axes swapped", c => Octant(c["to"] / 16 - c["from"] / 16, c["to"] % 16 - c["from"] % 16, (_, _) => false)),
            new("the same cell as 0", c => c["from"] == c["to"] ? 0 : Octant(c["to"] % 16 - c["from"] % 16, c["to"] / 16 - c["from"] / 16, (_, _) => false)));
    }

    // ---------- character tables ----------

    [TestMethod, TestCategory(Category)]
    public void StatCostMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B0F50", "stat-cost", TestContext);
        Run(file, c => AutoLevelRules.StatCost(c["value"]), c => c["result"],
            new Mutant("one costs a point too", c => c["value"] < 1 ? 0 : 1),
            new("two is still free", c => c["value"] <= 2 ? 0 : 1),
            new("a value below one costs a point", c => c["value"] <= 0 ? 1 : c["value"] == 1 ? 0 : 1));
    }

    [TestMethod, TestCategory(Category)]
    public void KeyStatCapMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C5F70", "key-stat-cap", TestContext);
        int[] caps = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
        Run(file, c => SkillRules.KeyStatCap(c["keyStat"]), c => c["result"],
            new Mutant("a stat below one answers 0", c => c["keyStat"] < 1 ? 0 : caps[Math.Min(c["keyStat"], 20) - 1]),
            new("the table shifted by one", c => caps[Math.Clamp(c["keyStat"], 1, 20) % 20]),
            new("a stat above 17 clamped to 17", c => caps[Math.Clamp(c["keyStat"], 1, 17) - 1]));
    }

    [TestMethod, TestCategory(Category)]
    public void MinimumKeyStatMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C64E0", "minimum-key-stat", TestContext);
        int[] caps = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
        Run(file, c => SkillRules.MinimumKeyStat(c["level"]), c => c["result"],
            new Mutant("the first cap above the level", c => Enumerable.Range(1, 20).FirstOrDefault(s => caps[s - 1] > c["level"])),
            new("20 for a level above every cap", c => Enumerable.Range(1, 20).Where(s => caps[s - 1] >= c["level"]).DefaultIfEmpty(20).First()),
            new("a level of 0 or below needs no stat", c => c["level"] <= 0 ? 0 : Enumerable.Range(1, 20).FirstOrDefault(s => caps[s - 1] >= c["level"])));
    }

    [TestMethod, TestCategory(Category)]
    public void BasicKeyStatMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C64D0", "basic-key-stat", TestContext);
        Run(file, c => (int)SkillRules.KeyStat(SkillRules.Basic(c["skill"])), c => c["result"],
            new Mutant("dexterity for every skill", c => (int)CharacterStat.Dexterity),
            new("the next skill's key stat", c => (int)SkillRules.KeyStat(SkillRules.Basic((c["skill"] + 1) % 12))),
            new("prowling reads dexterity", c => c["skill"] == 6 ? (int)CharacterStat.Dexterity : (int)SkillRules.KeyStat(SkillRules.Basic(c["skill"]))));
    }

    [TestMethod, TestCategory(Category)]
    public void TechKeyStatMatchesTheOriginal()
    {
        var file = OracleFile.Load("4C6B10", "tech-key-stat", TestContext);
        Run(file, c => (int)SkillRules.KeyStat(SkillRules.Tech(c["skill"])), c => c["result"],
            new Mutant("intelligence for every tech skill", c => (int)CharacterStat.Intelligence),
            new("the tech skills in reverse", c => (int)SkillRules.KeyStat(SkillRules.Tech(3 - c["skill"]))));
    }

    // ---------- helpers ----------

    private static long U(Row c, string field) => (uint)c[field];

    private static TravelLocation Location(Row c, string prefix) => new(U(c, prefix + "x"), U(c, prefix + "y"));

    private static long Dx(Row c) => U(c, "bx") - U(c, "ax");

    private static long Dy(Row c) => U(c, "by") - U(c, "ay");

    /// <summary>The direction code (0..7, 5 for none) of a step: the signs of the two deltas, the minor axis zeroed when
    /// <paramref name="drop"/> (minor, major) says so.</summary>
    private static int Octant(long dx, long dy, Func<long, long, bool> drop)
    {
        long sx = Math.Sign(dx), sy = Math.Sign(dy), ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (ax > ay) { if (drop(ay, ax)) sy = 0; }
        else if (drop(ax, ay)) sx = 0;
        for (var code = 0; code < 8; code++)
            if (WorldTravelGeometry.DeltaX[code] == sx && WorldTravelGeometry.DeltaY[code] == sy) return code;
        return 5;
    }

    private void Run(OracleFile file, Func<Row, long> real, Func<Row, long> original, params Mutant[] mutants)
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
}
