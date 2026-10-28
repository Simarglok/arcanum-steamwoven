using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Magic.SpellResistanceOriginalExeTests;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-WORLD-088: one global AI action list (<see cref="AiGlobalLists.Build"/>: 0x4CC380 with the runtime's quicksort 0x58A548 and
/// the comparator 0x4CC2D0) against the retail 1.0.7.4 executable on the function oracle's seeded inputs (<c>tools/original-oracle</c>,
/// model <c>ai-global-list</c>: the 223 records' values for one list, many of them equal): the count and the order of the listed
/// spells (an FNV-1a digest and the first three). No rule-mode switch touches it, so Steamwoven must match on every input. Opt-in like
/// <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class AiListOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void GlobalListMatchesTheOriginal()
    {
        var file = OracleFile.Load("4CC380", "ai-global-list", TestContext);
        Run(file, c => Describe(Global(c)), c => $"{c["count"]} {c["digest"]} {c["first"]} {c["second"]} {c["third"]}",
            new Mutant("a stable sort, equal values in number order", c => Describe(Listed(c).OrderByDescending(s => Value(c, s)).ToArray())),
            new("equal values in reverse number order", c => Describe(Listed(c).OrderByDescending(s => Value(c, s)).ThenByDescending(s => s).ToArray())),
            new(".NET's introsort with the same comparator", c =>
            {
                var spells = Listed(c).ToArray();
                Array.Sort(spells, (a, b) => AiActionLists.CompareValues(Value(c, a), Value(c, b)));
                return Describe(spells);
            }));
    }

    private static int Value(Row c, int spell) => c[$"value{spell}"];

    private static IEnumerable<int> Listed(Row c) => Enumerable.Range(0, AiGlobalLists.Spells).Where(s => Value(c, s) != -1);

    private static int[] Global(Row c)
    {
        var list = (AiActionList)c["list"];
        var spells = Enumerable.Range(0, AiGlobalLists.Spells).Select(s =>
        {
            var values = Enumerable.Repeat(-1, AiGlobalLists.Lists).ToArray();
            values[(int)list] = Value(c, s);
            return new AiSpellValues(values, 0);
        }).ToArray();
        return [.. AiGlobalLists.Build(spells)[list]];
    }

    private static string Describe(int[] order)
    {
        var first = order.Concat(Enumerable.Repeat(-1, 3)).Take(3).ToArray();
        return $"{order.Length} {Digest(order)} {first[0]} {first[1]} {first[2]}";
    }

    // FNV-1a over the 32-bit values, as the oracle's _digest.
    private static int Digest(IEnumerable<int> values)
    {
        var hash = 0x811C9DC5u;
        foreach (var value in values)
            foreach (var b in BitConverter.GetBytes(value))
                hash = unchecked((hash ^ b) * 0x01000193u);
        return unchecked((int)hash);
    }

    private void Run(OracleFile file, Func<Row, string> real, Func<Row, string> original, params Mutant[] mutants)
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
            Assert.Fail($"{unexpected.Count} differences from the original; first: list {unexpected[0]["list"]} original {original(unexpected[0])}, Steamwoven {real(unexpected[0])}");
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
