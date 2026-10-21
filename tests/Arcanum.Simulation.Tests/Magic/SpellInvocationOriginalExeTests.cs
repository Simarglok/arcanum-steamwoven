using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Magic.SpellResistanceOriginalExeTests;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>
/// ARC-MAGIC-006: the invocation check's range (<see cref="SpellInvocationRules.InRange"/>, 0x454700), forbidden flags
/// (<see cref="SpellInvocationRules.FlagsAllow"/>, 0x456430) and item gate (<see cref="SpellInvocationRules.HolderCanUseItem"/>,
/// 0x456A10) against the retail 1.0.7.4 executable on the function oracle's seeded inputs (<c>tools/original-oracle</c>, models
/// <c>spell-range</c>, <c>spell-flags</c> and <c>item-gate</c>). No rule-mode switch touches them, so Steamwoven must match on every
/// input. Opt-in like <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class SpellInvocationOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void RangeMatchesTheOriginal()
    {
        var file = OracleFile.Load("454700", "spell-range", TestContext);
        Run(file, c => Bit(SpellInvocationRules.InRange(Origin(c), Tile(c), Target(c), RecordRange(c))), c => $"{c["result"]}",
            new Mutant("an object in an inventory is measured", c => RangeVariant(c, inventory: false)),
            new("strictly closer than the range", c => RangeVariant(c, strict: true)),
            new("the Manhattan distance", c => RangeVariant(c, manhattan: true)),
            new("32-bit wrapping differences", c => RangeVariant(c, wrap: true)),
            new("the record's range before the table loads", c => RangeVariant(c, ignoreLoaded: true)));
    }

    [TestMethod, TestCategory(Category)]
    public void FlagsMatchTheOriginal()
    {
        var file = OracleFile.Load("456430", "spell-flags", TestContext);
        Run(file, c => Bit(SpellInvocationRules.FlagsAllow(Caster(c), FlagsTarget(c), Masks(c))), c => $"{c["result"]}",
            new Mutant("the target is tested without a caster", c => FlagsVariant(c, needCaster: false)),
            new("critter flags of any object", c => FlagsVariant(c, anyKind: true)),
            new("the masks swapped", c => FlagsVariant(c, swapped: true)));
    }

    [TestMethod, TestCategory(Category)]
    public void ItemGateMatchesTheOriginal()
    {
        var file = OracleFile.Load("456A10", "item-gate", TestContext);
        Run(file, c => Gate(c, (aptitude, complexity, power) => SpellInvocationRules.HolderCanUseItem(aptitude, complexity, power)),
            c => $"{c["result"]} {c["powerCalls"]}",
            new Mutant("a power of 0 suffices", c => Gate(c, (a, k, p) => a is not { } v || k <= 0 || v >= 0 || p() >= 0)),
            new("the complexity is not tested", c => Gate(c, (a, _, p) => a is not { } v || v >= 0 || p() > 0)),
            new("the second object's aptitude", c => Gate(c, (a, k, p) => a is null || k <= 0 || (c["other"] != 0 ? c["otherAptitude"] : a) >= 0 || p() > 0)));
    }

    private static string Bit(bool value) => value ? "1" : "0";

    private static SpellLocation Location(Row c, string x, string y) => new(unchecked((uint)c[x]), unchecked((uint)c[y]));

    private static SpellLocation Origin(Row c) => Location(c, "originX", "originY");

    private static SpellLocation Tile(Row c) => Location(c, "tileX", "tileY");

    private static SpellObjectFacts? Target(Row c) => c["target"] == 0 ? null
        : new("target", SpellObjectFacts.FirstItemType, Location(c, "locationX", "locationY"), ObjectFlags: c["objectFlags"]);

    // The record's Range: as the getter (0x450280) answers it: 1 before the spell table is loaded.
    private static int RecordRange(Row c) => c["loaded"] != 0 ? c["range"] : 1;

    private static SpellObjectFacts? Caster(Row c) => c["caster"] == 0 ? null
        : new("caster", SpellObjectFacts.NpcType, SpellLocation.None, SpellFlags: c["casterFlags"]);

    private static SpellObjectFacts? FlagsTarget(Row c) => c["target"] == 0 ? null
        : new("target", c["type"], SpellLocation.None, SpellFlags: c["targetFlags"], CritterFlags: c["critterFlags"]);

    private static SpellInvocationRecord Masks(Row c) => new(false, 0, c["disallowedCaster"], c["disallowedTarget"], c["disallowedCritter"]);

    private static string Gate(Row c, Func<int?, int, Func<int>, bool> gate)
    {
        var calls = 0;
        var result = gate(c["holder"] != 0 ? c["aptitude"] : null, c["complexity"], () =>
        {
            calls++;
            return c["power"];
        });
        return $"{Bit(result)} {calls}";
    }

    // Planted variants, written apart from SpellInvocationRules.
    private static string RangeVariant(Row c, bool inventory = true, bool strict = false, bool manhattan = false, bool wrap = false,
        bool ignoreLoaded = false)
    {
        var origin = Origin(c);
        if (origin.IsNone) return "1";
        var tile = Tile(c);
        if (c["target"] != 0)
        {
            if (inventory && (c["objectFlags"] & SpellObjectFacts.InInventory) != 0) return "1";
            tile = Location(c, "locationX", "locationY");
        }
        if (tile.IsNone) return "0";
        long dx = wrap ? Math.Abs((long)unchecked((int)(origin.X - tile.X))) : Math.Abs((long)origin.X - tile.X);
        long dy = wrap ? Math.Abs((long)unchecked((int)(origin.Y - tile.Y))) : Math.Abs((long)origin.Y - tile.Y);
        var distance = manhattan ? dx + dy : Math.Max(dx, dy);
        var range = ignoreLoaded ? c["range"] : RecordRange(c);
        return Bit(strict ? distance < range : distance <= range);
    }

    private static string FlagsVariant(Row c, bool needCaster = true, bool anyKind = false, bool swapped = false)
    {
        var (casterMask, targetMask) = swapped ? (c["disallowedTarget"], c["disallowedCaster"]) : (c["disallowedCaster"], c["disallowedTarget"]);
        if (c["caster"] == 0 && needCaster) return "1";
        if (c["caster"] != 0 && (c["casterFlags"] & casterMask) != 0) return "0";
        if (c["target"] == 0) return "1";
        if ((c["targetFlags"] & targetMask) != 0) return "0";
        return Bit(!(anyKind || c["type"] is 15 or 16) || (c["critterFlags"] & c["disallowedCritter"]) == 0);
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
