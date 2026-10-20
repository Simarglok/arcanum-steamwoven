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
/// ARC-MAGIC-005: <see cref="SpellResistanceRules.Resist"/> and <see cref="SpellResistanceRules.FailChance"/> against the retail
/// 1.0.7.4 executable on the function oracle's seeded inputs (<c>tools/original-oracle</c>, models <c>spell-resist</c> and
/// <c>spell-fail-chance</c>): one run, record and target, the pre-drawn d100 and d20, the armed fate, and recording stubs for the
/// random range, the fate use and the AI notification. The Original setting must match on every input; the Steamwoven setting
/// may differ only where the MG4 penalty reaches the save. Opt-in like <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class SpellResistanceOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private const int Untouched = 0x7FFF0001;

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, string> Compute);

    private sealed record OtherMode(string Mode, string Ledger, Func<Row, bool> Applies, Func<Row, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void ResistMatchesTheOriginal()
    {
        var file = OracleFile.Load("4537B0", "spell-resist", TestContext);
        Run(file, c => Resist(c, MagicAnomalyProfile.Original),
            c => $"{c["result"]} {c["margin"]} {c["displayMargin"]} {c["rolls"]} {c["fateUses"]} {c["notified"]}",
            new("steamwoven", "MG4", c => ReachesSaveWithPenalty(c), c => Resist(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("the aptitude scales any resistance", c => Variant(c, anyAptitude: true)),
            new("the fate save keeps margin 100", c => Variant(c, fateMargin: 100)),
            new("the damaging floor is 40", c => Variant(c, floor: 40)),
            new("the caster resists", c => Variant(c, casterResists: true)));
    }

    [TestMethod, TestCategory(Category)]
    public void FailChanceMatchesTheOriginal()
    {
        var file = OracleFile.Load("453B20", "spell-fail-chance", TestContext);
        Run(file, c => FailChance(c, MagicAnomalyProfile.Original), c => $"{c["result"]} {c["rolls"]}",
            new("steamwoven", "MG4", c => PredictorReachesSaveWithPenalty(c), c => FailChance(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("no certain answer above 40", c => PredictorVariant(c, certainAbove: int.MaxValue)),
            new("the damaging floor is 40", c => PredictorVariant(c, floor: 40)),
            new("no d20 for the predictor", c => PredictorVariant(c, rolls: false)));
    }

    private static SpellResistRecord Record(Row c) => new(c["spell"], c["technology"] != 0, c["damaging"] != 0, c.Has("aggressive") && c["aggressive"] != 0,
        c["period"], c["resistStat"], c["modifier"]);

    private static SpellResistTarget Target(Row c) => new(c["caster"] != 0, c["type"] switch { 15 => SpellTargetKind.Pc, 16 => SpellTargetKind.Npc, _ => SpellTargetKind.Other },
        c.Has("noDisintegrate") && c["noDisintegrate"] != 0, c["resistance"], c["aptitude"], c["statValue"], c["extraordinary"] != 0);

    private static string Resist(Row c, MagicAnomalyProfile profile)
    {
        var rolls = new Rolls(c);
        var fate = new Fate(c["fate"] != 0);
        var result = SpellResistanceRules.Resist(Record(c), Target(c), new(c["action"] == 0, c["unresistable"] != 0, c["fateMaximized"] != 0),
            rolls, fate, profile);
        return $"{(result.Applies ? 1 : 0)} {result.Margin ?? Untouched} {result.DisplayMargin ?? Untouched} {rolls.Code} {fate.Uses} {(result.NotifiesAi ? 1 : 0)}";
    }

    private static string FailChance(Row c, MagicAnomalyProfile profile)
    {
        var rolls = new Rolls(c);
        var result = SpellResistanceRules.FailChance(Record(c), Target(c), c["fateMaximized"] != 0, rolls, profile);
        return $"{result} {rolls.Count}";
    }

    // MG4's inputs: the save is reached with a non-zero penalty (the Original run's penalty), so adding it can change the answer.
    private static bool ReachesSaveWithPenalty(Row c)
    {
        var result = SpellResistanceRules.Resist(Record(c), Target(c), new(c["action"] == 0, c["unresistable"] != 0, c["fateMaximized"] != 0),
            new Rolls(c), new Fate(c["fate"] != 0), MagicAnomalyProfile.Original);
        return result.Penalty != 0;
    }

    private static bool PredictorReachesSaveWithPenalty(Row c)
    {
        var record = Record(c);
        var target = Target(c);
        if (record.Technology || target.IsCaster || !record.HasSave || target.Kind == SpellTargetKind.Other || record.Damaging || record.MaintainPeriod > 0)
            return false;
        var resistance = target.StoredMagicResistance;
        if (c["fateMaximized"] == 0 && target.Aptitude < 0) resistance = unchecked(100 - (100 - resistance) * (target.Aptitude + 100) / 100);
        return resistance / 10 != 0 && resistance > 0;
    }

    // A planted variant of the resistance, written apart from SpellResistanceRules.
    private static string Variant(Row c, bool anyAptitude = false, int fateMargin = 10, int floor = 50, bool casterResists = false)
    {
        var record = Record(c);
        var target = Target(c);
        string rolls = "";
        int fateUses = 0;
        string Out(int applies, int margin, int display, int notify = 0) => $"{applies} {margin} {display} {Code(rolls)} {fateUses} {notify}";
        if (c["action"] != 0 || record.Technology || c["unresistable"] != 0) return Out(1, Untouched, Untouched);
        int margin = 0, display = 0, penalty = 0;
        var critter = target.Kind != SpellTargetKind.Other;
        var caster = target.IsCaster && !casterResists;
        if (record.Spell == 34 && critter && target.NoDisintegrate) return Out(0, 0, 0);
        if (!caster)
        {
            if (target.Kind == SpellTargetKind.Pc && (fateUses = 1) == 1 && c["fate"] != 0) (display, margin) = (100, fateMargin);
            else
            {
                var r = target.StoredMagicResistance;
                if ((anyAptitude || target.Aptitude < 0) && c["fateMaximized"] == 0) r = unchecked(100 - (100 - r) * (target.Aptitude + 100) / 100);
                rolls += "d";
                if (c["d100"] < r) display = margin = r - c["d100"];
                if (r > 0 && !record.Damaging && record.MaintainPeriod <= 0)
                {
                    if (record.HasSave) penalty = margin / 10;
                    else if (c["d100"] < r) return Out(0, margin, display);
                }
            }
        }
        if (caster || !record.HasSave || !critter) return Out(1, margin, display);
        if (record.ResistStat == 6 && target.ResistStatExtraordinary) return Out(0, margin, display, record.Aggressive ? 1 : 0);
        var save = target.ResistStatValue - penalty + record.ResistModifier;
        if (save <= 0) return Out(1, margin, display);
        rolls += "s";
        if (c["d20"] > save) return Out(1, margin, display);
        display = margin += save - c["d20"];
        if (record.Damaging) return margin < floor ? Out(1, floor, floor) : Out(1, margin, display);
        return Out(record.MaintainPeriod != 0 ? 1 : 0, margin, display);
    }

    private static string PredictorVariant(Row c, int certainAbove = 40, int floor = 50, bool rolls = true)
    {
        var record = Record(c);
        var target = Target(c);
        if (record.Technology) return "0 0";
        int chance = 0, penalty = 0;
        if (!target.IsCaster)
        {
            chance = target.StoredMagicResistance;
            if (c["fateMaximized"] == 0 && target.Aptitude < 0) chance = unchecked(100 - (100 - chance) * (target.Aptitude + 100) / 100);
            if (chance > 0 && !record.Damaging && record.MaintainPeriod <= 0)
            {
                if (!record.HasSave) { if (chance > certainAbove) return "100 0"; }
                else penalty = chance / 10;
            }
        }
        if (target.IsCaster || !record.HasSave || target.Kind == SpellTargetKind.Other) return $"{chance} 0";
        if (record.ResistStat == 6 && target.ResistStatExtraordinary) return "100 0";
        var save = target.ResistStatValue - penalty + record.ResistModifier;
        if (save <= 0) return $"{chance} 0";
        var count = rolls ? 1 : 0;
        if (c["d20"] > save) return $"{chance} {count}";
        if (record.Damaging) return $"{Math.Max(chance, floor)} {count}";
        return $"{(record.MaintainPeriod != 0 ? chance : 100)} {count}";
    }

    private static int Code(string rolls) => rolls switch { "" => 0, "d" => 1, "s" => 2, _ => 3 };

    private sealed class Rolls(Row c) : ISpellRollSource
    {
        private string _made = "";

        public int Code => SpellResistanceOriginalExeTests.Code(_made);

        public int Count => _made.Length;

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            _made += purpose == SpellRollPurpose.ResistanceD100 ? "d" : "s";
            return purpose == SpellRollPurpose.ResistanceD100 ? c["d100"] : c["d20"];
        }
    }

    private sealed class Fate(bool armed) : ISpellFateSave
    {
        public int Uses { get; private set; }

        public bool ConsumeSaveAgainstMagick()
        {
            Uses++;
            return armed;
        }
    }

    private void Run(OracleFile file, Func<Row, string> real, Func<Row, string> original, OtherMode other, params Mutant[] mutants)
    {
        Assert.IsTrue(mutants.Length >= 2, "at least two planted mutants per function");
        var unexpected = file.Cases.Where(c => real(c) != original(c)).ToList();
        var caught = mutants.Where(m => file.Cases.Any(c => m.Compute(c) != original(c))).Select(m => m.Name).ToList();
        var missed = mutants.Select(m => m.Name).Except(caught).ToList();
        var ledgerInputs = file.Cases.Count(other.Applies);
        var otherDifferences = file.Cases.Where(c => other.Compute(c) != original(c)).ToList();
        var outsideLedger = otherDifferences.Where(c => !other.Applies(c)).ToList();
        var pass = unexpected.Count == 0 && missed.Count == 0 && outsideLedger.Count == 0;
        var method = ExperienceOriginalExeTests.OriginalMethod(file.Key);
        TestContext.WriteLine($"oracle {file.Function} {file.Name} ({MethodIlHash.Name(method)}): seed {file.Seed}, {file.Cases.Count} inputs, exe {file.ExeSha256}");
        TestContext.WriteLine($"  differences: {unexpected.Count} (original mode); {other.Mode} mode: {otherDifferences.Count} differences among the "
            + $"{ledgerInputs} {other.Ledger} inputs, {outsideLedger.Count} outside them");
        TestContext.WriteLine($"  mutants caught: {caught.Count}/{mutants.Length}");
        WriteReceipt(file, method, other, ledgerInputs, otherDifferences.Count, outsideLedger.Count, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
            Assert.Fail($"{unexpected.Count} differences from the original; first: {unexpected[0]} original {original(unexpected[0])}, Steamwoven (original mode) {real(unexpected[0])}");
        if (outsideLedger.Count > 0)
            Assert.Fail($"the {other.Mode} mode differs outside {other.Ledger}: {outsideLedger[0]} original {original(outsideLedger[0])}");
        Assert.AreEqual(0, missed.Count, "planted mutants not caught (the inputs do not cover them): " + string.Join(", ", missed));
    }

    /// <summary>The oracle receipt (docs/verification/receipts), in the format of <see cref="ExperienceOriginalExeTests"/>.</summary>
    private void WriteReceipt(OracleFile file, MethodInfo method, OtherMode other, int ledgerInputs, int otherDifferences, int outsideLedger,
        int planted, int caught, int differences, bool pass)
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
            ["otherMode"] = new Dictionary<string, object>
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

    /// <summary>One oracle input and the original's answer, by field name.</summary>
    internal sealed class Row(IReadOnlyDictionary<string, int> index, int[] values)
    {
        public int this[string field] => values[index[field]];

        public bool Has(string field) => index.ContainsKey(field);

        public override string ToString() => string.Join(' ', index.OrderBy(p => p.Value).Select(p => $"{p.Key} {values[p.Value]}")) + ":";
    }

    internal sealed record OracleFile(string Key, string Function, string Name, string ExeSha256, long Seed, IReadOnlyList<Row> Cases)
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
            var fields = root.GetProperty("fields").EnumerateArray().Select((f, i) => (f.GetString()!, i)).ToDictionary(p => p.Item1, p => p.i);
            var cases = rows.EnumerateArray().Select(row => new Row(fields, row.EnumerateArray().Select(e => e.GetInt32()).ToArray())).ToList();
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
