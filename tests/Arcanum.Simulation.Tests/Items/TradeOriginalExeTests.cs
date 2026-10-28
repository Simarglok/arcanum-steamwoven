using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>
/// ARC-UI-050: <see cref="TradeRules.Price"/> against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>, model <c>item-price</c>): the barter quote of one item between the PC and a merchant with the checks on,
/// the item's worth, identified test, sale and buy checks and the reaction scale run as they are. Opt-in like
/// <see cref="ExperienceOriginalExeTests"/>: inconclusive without <c>ARCANUM_EXE</c> or the oracle's file; planted mutants of the C#
/// side must each be caught.
/// </summary>
[TestClass]
public sealed class TradeOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private static readonly string[] Fields =
        ["sell", "type", "worth", "complexity", "flags", "multiplier", "haggle", "training", "script", "dealer", "npcFlags", "broken", "current",
         "maximum", "location", "reaction", "result"];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<PriceCase, int> Compute);

    [TestMethod, TestCategory(Category)]
    public void PriceMatchesTheOriginal()
    {
        var file = OracleFile.Load("461F80", "item-price", TestContext);
        Run(file, c => Real(c),
            new Mutant("the multiplier at every value", c => Variant(c, multiplierAlways: true)),
            new("the haggle share without halving", c => Variant(c, haggleWhole: true)),
            new("the reaction not capped at 100", c => Variant(c, reactionCap: false)),
            new("the buy check ignores a worn item", c => Real(c, location: 0)),
            new("a fence check only below Expert", c => c.Training >= 2 ? Real(c with { Flags = c.Flags & ~0x8000 }) : Real(c)),
            new("the buy markup in 64 bits", c => Variant(c, wide: true)));
    }

    // The worth of 0x461F20 through Steamwoven's own rules (EquipmentChoice, F2): a magickal item without its identified flag is 300.
    private static int Worth(PriceCase c)
    {
        var identified = c.Complexity <= 0 || (c.Flags & 0x1) != 0;
        return identified ? Math.Max(EquipmentChoice.MinimumWorth, c.Worth) : EquipmentChoice.UnidentifiedWorth;
    }

    private static TradeItem Item(PriceCase c, int? location = null) => new(Worth(c), c.Worth, unchecked((uint)c.Flags),
        new ItemCondition(c.Maximum, 0, c.Maximum - c.Current, c.Broken), (location ?? c.Location) is >= 1000 and <= 1008, c.Type == 8);

    private static TradeParties Parties(PriceCase c) => new(c.Multiplier, c.Haggle, (SkillTraining)c.Training, c.Reaction, c.Script == 1, c.Dealer != 0,
        (c.NpcFlags & 0x4000) != 0);

    private static int Real(PriceCase c, int? location = null) =>
        TradeRules.Price(c.Sell ? TradeDirection.PcSells : TradeDirection.PcBuys, Item(c, location), Parties(c)).Price;

    // A planted variant of the price, written apart from TradeRules: the checks come from TradeRules, the arithmetic is redone here.
    private static int Variant(PriceCase c, bool multiplierAlways = false, bool haggleWhole = false, bool reactionCap = true, bool wide = false)
    {
        var item = Item(c);
        var parties = Parties(c);
        if (item.Gold) return 0;
        var w = item.Worth;
        unchecked
        {
            if (c.Sell)
            {
                if (TradeRules.SaleCheck(item, parties) != TradeRefusal.None) return 0;
                var p = w;
                if (c.Multiplier > 100 || multiplierAlways) p = p * (100 + 3 * (100 - c.Multiplier) / 8) / 100;
                p = haggleWhole ? p * (c.Haggle + 50) / 100 : p * (c.Haggle / 2 + 50) / 100;
                if (c.Current < c.Maximum) p = c.Current * p / c.Maximum;
                return p < 0 ? 0 : p == 1 ? 2 : p;
            }
            if (TradeRules.BuyCheck(item, parties) != TradeRefusal.None) return 0;
            var cost = wide ? (int)(w + (long)(100 - c.Haggle) * c.Multiplier * w / 10000) : w + (100 - c.Haggle) * c.Multiplier * w / 10000;
            var r = reactionCap && c.Reaction > 100 ? 100 : c.Reaction;
            var factor = r < 0 ? 200 : r < 50 ? 200 - 2 * r : 120 - 2 * r / 5;
            cost = cost * factor / 100;
            if (cost <= w) cost = w + 1;
            return cost is 1 or 2 ? 3 : cost;
        }
    }

    /// <summary>Compares the C# side with the original on every input, then replays each mutant, which must differ on at least
    /// one input. Writes the oracle receipt when <c>ARCANUM_ORACLE_RECEIPTS</c> names a folder, before failing.</summary>
    private void Run(OracleFile file, Func<PriceCase, int> real, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        var unexpected = file.Cases.Where(c => real(c) != c.Result).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => m.Compute(c) != c.Result)).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var pass = unexpected.Count == 0 && missed.Count == 0;
        var method = ExperienceOriginalExeTests.OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  differences: {unexpected.Count}");
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
            Assert.Fail($"{unexpected.Count} differences from the original; first: {unexpected[0]} original {unexpected[0].Result}, Steamwoven {real(unexpected[0])}");
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

    /// <summary>One oracle input (see the model's fields) and the original's quote.</summary>
    internal sealed record PriceCase(bool Sell, int Type, int Worth, int Complexity, int Flags, int Multiplier, int Haggle, int Training, int Script,
        int Dealer, int NpcFlags, bool Broken, int Current, int Maximum, int Location, int Reaction, int Result)
    {
        public override string ToString() =>
            $"{(Sell ? "sell" : "buy")} type {Type} worth {Worth} complexity {Complexity} flags {Flags:X} m {Multiplier} H {Haggle} training {Training} script {Script} " +
            $"dealer {Dealer} npc {NpcFlags:X} broken {Broken} hp {Current}/{Maximum} location {Location} reaction {Reaction}:";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<PriceCase> Cases)
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
            var cases = new List<PriceCase>(count);
            foreach (var row in rows.EnumerateArray())
            {
                // Field values are 32-bit words; a flags word may be stored unsigned.
                var v = row.EnumerateArray().Select(e => unchecked((int)e.GetInt64())).ToArray();
                cases.Add(new(v[0] != 0, v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11] != 0, v[12], v[13], v[14], v[15], v[16]));
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
