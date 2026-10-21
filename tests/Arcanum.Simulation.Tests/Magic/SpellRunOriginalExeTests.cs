using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Tests.Character;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Magic.SpellResistanceOriginalExeTests;

namespace Arcanum.Simulation.Tests.Magic;

/// <summary>
/// ARC-MAGIC-009: the run rules against the retail 1.0.7.4 executable on the function oracle's seeded inputs
/// (<c>tools/original-oracle</c>): the time scaling (<see cref="SpellRunRules.ScaleTime"/>, 0x455250, model <c>spell-time</c>), the
/// stage at BEGIN and MAINTAIN (<see cref="SpellRunRules.Stage"/>, 0x452F20 with its payment helpers, model <c>spell-stage</c>:
/// the payment answers by the source's fatigue, as <see cref="SpellPaymentRules.Pay"/> does for a critter without stores) and the
/// maintenance cap (<see cref="SpellRunRules.WithinCap"/>, 0x4545E0, model <c>spell-cap</c>). The Original setting must match on every
/// input; the Steamwoven setting may differ only on MG3's inputs (the stage). Opt-in like <see cref="ExperienceOriginalExeTests"/>.
/// </summary>
[TestClass]
public sealed class SpellRunOriginalExeTests
{
    public const string Category = ExperienceOriginalExeTests.Category;
    private const string NoSwitch = "none";

    public TestContext TestContext { get; set; } = null!;

    private sealed record Mutant(string Name, Func<Row, string> Compute);

    [TestMethod, TestCategory(Category)]
    public void TimeMatchesTheOriginal()
    {
        var file = OracleFile.Load("455250", "spell-time", TestContext);
        string Time(Row c, Func<SpellTimeFacts, SpellTimeFacts>? change = null, bool anySpell = false)
        {
            var facts = new SpellTimeFacts(c["parent"] != 0 && c["parentType"] is 15 or 16, c["mastery"], c["target"] != 0 && c["targetType"] is 15 or 16,
                c["parentAptitude"], c["targetAptitude"]);
            if (change is not null) facts = change(facts);
            var spell = anySpell && c["spell"] is >= 80 ? c["spell"] % 80 : c["spell"];
            return $"{c["days"]} {unchecked((int)SpellRunRules.ScaleTime(unchecked((uint)c["ms"]), spell, facts))}";
        }
        Run(file, c => Time(c), c => $"{c["daysAfter"]} {c["msAfter"]}", (NoSwitch, NoSwitch, _ => false, c => Time(c)),
            new Mutant("no mastery doubling", c => Time(c, f => f with { MasteredCollege = -2 })),
            new("the signed aptitude scaling applies", c => SignedScaling(c, Time(c))),
            new("mastery for every spell", c => Time(c, null, anySpell: true)));
    }

    // MG6's intended reading: a target whose aptitude differs scales the time by 100 / (difference + 100).
    private static string SignedScaling(Row c, string plain)
    {
        var parts = plain.Split(' ');
        var ms = uint.Parse(parts[1] is var m && m.StartsWith('-') ? unchecked((uint)int.Parse(m)).ToString() : m);
        if (c["parent"] != 0 && c["parentType"] is 15 or 16 && c["target"] != 0 && c["targetType"] is 15 or 16 && ms > 0)
        {
            var difference = c["parentAptitude"] - c["targetAptitude"];
            if (difference is < 0 and > -100) ms = (uint)(ms * 100UL / (uint)(difference + 100));
        }
        return $"{parts[0]} {unchecked((int)ms)}";
    }

    [TestMethod, TestCategory(Category)]
    public void StageMatchesTheOriginal()
    {
        var file = OracleFile.Load("452F20", "spell-stage", TestContext);
        bool Mg3(Row c) => c["action"] == 1 && c["margin"] != 0 && c["parent"] != 0 && c["parentType"] is 15 or 16 && c["source"] != 0;
        Run(file, c => Stage(c, MagicAnomalyProfile.Original), c =>
                $"{c["result"]} {c["actionAfter"]} {c["payCalls"]} {c["pay0"]} {c["pay1"]} {c["message"]} {c["stackAsked"]} {c["cancels"]} {c["bar"]} {c["processed"]} {c["stackOn"]}",
            // MG3 and MG29 (owner decision 2026-10-21) both change only a MAINTAIN tick with a margin of a critter parent's sourced run.
            ("steamwoven", "MG3, MG29", Mg3, c => Stage(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("technology records are checked at BEGIN", c => Stage(c, MagicAnomalyProfile.Original, r => r with { Technology = false })),
            new("a free run pays", c => Stage(c, MagicAnomalyProfile.Original, null, run => run with { Flags = run.Flags & ~SpellRunFlags.Free })),
            new("no surcharge", c => Stage(c, MagicAnomalyProfile.Original, null, run => run with { Margin = 0 })),
            new("the minimum intelligence is 4", c => Stage(c, MagicAnomalyProfile.Original, null, null, f => f with { ParentIntelligence = f.ParentIntelligence + 1 })),
            new("the no-stack test asks the source first", c => Stage(c, MagicAnomalyProfile.Original, sourceFirst: true)));
    }

    // sourceFirst plants the mutant that asks about the source before the target.
    private sealed class Stacking(Row c, bool sourceFirst) : ISpellRunStacking
    {
        public int Asked { get; private set; }
        public int Cancels { get; private set; }
        public int On { get; private set; } = -1;

        public int? Blocking(SpellRun run, SpellRunObject target)
        {
            Asked++;
            var on = sourceFirst ? run.Source ?? target : target;
            On = on.Id switch { "target" => 2, "source" => 1, _ => 0 };
            return c["blocking"] < 0 ? null : c["blocking"];
        }

        public void Cancel(SpellRun run) => Cancels++;
    }

    private sealed class NoCells : ISpellCellSource
    {
        public SpellCellPayment Pay(SpellCellRequest request) => throw new AssertFailedException("no charge cells in the model");
    }

    private static string Stage(Row c, MagicAnomalyProfile profile, Func<SpellRunRecord, SpellRunRecord>? record = null, Func<SpellRun, SpellRun>? run = null,
        Func<SpellStageFacts, SpellStageFacts>? facts = null, bool sourceFirst = false)
    {
        var source = c["source"] != 0 ? new SpellRunObject("source", c["sourceType"]) : null;
        var parent = c["parent"] == 0 ? null : c["own"] != 0 && source is not null ? source : new SpellRunObject("parent", c["parentType"]);
        var theRun = new SpellRun(0, c["spell"], (SpellRunAction)c["action"], source, parent, SpellLocation.None, c["target"] != 0 ? new("target", 16) : null,
            SpellLocation.None, null, (SpellRunFlags)c["flags"], Margin: c["margin"]);
        var theRecord = new SpellRunRecord(c["technology"] != 0, SkipsBeginCost: c["skipBeginCost"] != 0, Cost: c["cost"], MaintainCost: c["maintain"],
            Duration: new(1), WillpowerMinimum: c["willpowerMinimum"], StackingRules: c["stacking"] != 0);
        var theFacts = new SpellStageFacts(c["dead"] != 0, c["cannotAct"] != 0, c["intelligence"], c["willpower"], c["local"] != 0,
            new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: c["fatigue"]), new SpellCaster(c["sourceType"] is 15 or 16, c["race"], c["mastery"]), c["cheat"] != 0);
        if (record is not null) theRecord = record(theRecord);
        if (run is not null) theRun = run(theRun);
        if (facts is not null) theFacts = facts(theFacts);
        var stacking = new Stacking(c, sourceFirst);
        var result = SpellRunRules.Stage(theRun, theRecord, theFacts, new NoCells(), stacking, profile);
        var pays = result.Costs.Concat(Enumerable.Repeat(-999, 2 - result.Costs.Count)).ToList();
        var action = result.Ends ? 2 : c["action"];
        // The maintain bar is updated with the message: a failure that ends the run, told to the local player.
        var bar = result.Ends && result.Message is not null ? 1 : 0;
        return $"{(result.Proceeds ? 1 : 0)} {action} {result.Costs.Count} {pays[0]} {pays[1]} {result.Message ?? -1} {stacking.Asked} {stacking.Cancels} {bar} {(result.Ends ? 1 : 0)} {stacking.On}";
    }

    /// <summary>ARC-MAGIC-010: the scheduler (<see cref="SpellRunScheduler.Decide"/>, 0x453FA0 with the tick helpers 0x454790 and
    /// 0x4547F0, model <c>spell-schedule</c>): the run after it and its calls in order; Steamwoven differs only on MG19's inputs
    /// (bit 0x10 at BEGIN, MAINTAIN or CALLBACK, which Steamwoven reads as unresistable only).</summary>
    [TestMethod, TestCategory(Category)]
    public void ScheduleMatchesTheOriginal()
    {
        var file = OracleFile.Load("453FA0", "spell-schedule", TestContext);
        static bool Mg19(Row c) => (c["flags"] & 0x10) != 0 && c["callbackNow"] == 0 && c["action"] is 0 or 1 or 3;
        Run(file, c => Schedule(c, MagicAnomalyProfile.Original), Expected,
            ("steamwoven", "MG19", Mg19, c => Schedule(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("the tail ignores the callback components", c => Schedule(c, MagicAnomalyProfile.Original, r => r with { ActionComponents = [0, 0, 0, 0, 0] })),
            new("a sourceless run always ticks 8 times", c => Schedule(c, MagicAnomalyProfile.Original, r => r with { TriggerCount = 0 })),
            new("no stat bonus", c => Schedule(c, MagicAnomalyProfile.Original, null, f => f with { StatValue = null })),
            new("the cap is not checked", c => Schedule(c, MagicAnomalyProfile.Original, null, f => f with { WithinCap = true })),
            new("nothing applied goes on", c => Schedule(c, MagicAnomalyProfile.Original, null, f => f with { Applied = true })));
    }

    /// <summary>ARC-MAGIC-010: the interrupt's action (<see cref="SpellRunSystem.InterruptAction"/>, 0x457110, model
    /// <c>spell-interrupt</c>): a run that exists turns to it, its target list empties and it is processed with it.</summary>
    [TestMethod, TestCategory(Category)]
    public void InterruptMatchesTheOriginal()
    {
        var file = OracleFile.Load("457110", "spell-interrupt", TestContext);
        string Interrupt(Row c, Func<SpellRunAction, SpellRunRecord, SpellRunAction> rule)
        {
            if (c["found"] == 0) return $"{c["action"]} {c["listed"]} -9";
            var record = new SpellRunRecord { ActionComponents = [0, 0, 0, c["callbackCount"], c["endCallbackCount"]] };
            var action = (int)rule((SpellRunAction)c["action"], record);
            return $"{action} 0 {action}";
        }
        Run(file, c => Interrupt(c, SpellRunSystem.InterruptAction), c => $"{c["actionAfter"]} {c["listAfter"]} {c["processed"]}",
            (NoSwitch, NoSwitch, _ => false, c => Interrupt(c, SpellRunSystem.InterruptAction)),
            new Mutant("MAINTAIN picks END_CALLBACK", c => Interrupt(c, (a, r) => SpellRunSystem.InterruptAction(a == SpellRunAction.Maintain ? SpellRunAction.Begin : a, r))),
            new("the callback components are ignored", c => Interrupt(c, (a, r) => SpellRunSystem.InterruptAction(a, r with { ActionComponents = [0, 0, 0, 0, r.Components(SpellRunAction.EndCallback)] }))));
    }

    private static string Expected(Row c) =>
        $"{c["actionAfter"]} {c["flagsAfter"]} {c["leftAfter"]} " + string.Join(",", Enumerable.Range(0, c["calls"]).Select(i => $"{c[$"call{i}Kind"]}:{c[$"call{i}Value"]}"));

    private static string Schedule(Row c, MagicAnomalyProfile profile, Func<SpellRunRecord, SpellRunRecord>? record = null,
        Func<SpellScheduleFacts, SpellScheduleFacts>? facts = null)
    {
        // The input bit 0x10: the one bit of the original, unresistable at creation (the final END too in the Original setting).
        var flags = (SpellRunFlags)(c["flags"] & ~0x10) | ((c["flags"] & 0x10) != 0 ? SpellRunRules.Unresistable(profile) : SpellRunFlags.None);
        var parent = c["parentType"] == -1 ? null : new SpellRunObject("parent", c["parentType"]);
        var target = c["targetType"] == -1 ? null : new SpellRunObject("target", c["targetType"]);
        var run = new SpellRun(0, c["spell"], (SpellRunAction)c["action"], c["source"] != 0 ? new("source", 15) : null, parent, SpellLocation.None,
            target, SpellLocation.None, null, flags, TriggerCount: c["left"]);
        var theRecord = new SpellRunRecord(Technology: c["technology"] != 0, MaintainCost: 2, MaintainPeriod: c["period"],
            Duration: new(c["seconds"], c["stat"], c["level"], c["perPoint"]), TriggerCount: c["recordCount"])
        { ActionComponents = [0, 0, c["endCount"], c["callbackCount"], c["endCallbackCount"]] };
        if (record is not null) theRecord = record(theRecord);
        var holder = SpellRunRules.DurationStatHolder(run);
        var theFacts = new SpellScheduleFacts(c["callbackNow"] != 0, c["callbackEnd"] != 0, c["guard"] != 0, c["applied"] != 0, c["pending"] != 0,
            c["withinCap"] != 0, c["tickAdded"] != 0, c["barAdds"] != 0, c["parentLocal"] != 0, c["targetLocal"] != 0,
            c["stat"] > -1 && holder is not null ? c["statValue"] : null,
            new(parent?.IsCritter == true, c["mastery"], target?.IsCritter == true, c["parentAptitude"], c["targetAptitude"]));
        if (facts is not null) theFacts = facts(theFacts);
        var outcome = SpellRunScheduler.Decide(run, theRecord, theFacts, profile);
        // The original's flag word: the one bit 0x10 for either flag.
        var after = outcome.Run.Flags;
        var word = (int)(after & ~(SpellRunFlags.Unresistable | SpellRunFlags.FinalEnd))
            | ((after & (SpellRunFlags.Unresistable | SpellRunFlags.FinalEnd)) != 0 ? 0x10 : 0);
        static int Kind(SpellScheduleEffect effect) => effect switch
        {
            SpellScheduleEffect.CallbackAction => 1, SpellScheduleEffect.MaintainBarRefresh => 2, SpellScheduleEffect.Free => 3,
            SpellScheduleEffect.TargetRefresh => 4, SpellScheduleEffect.Message => 5, SpellScheduleEffect.Process => 6,
            SpellScheduleEffect.Tick => 7, _ => 8,
        };
        return $"{(int)outcome.Run.Action} {word} {outcome.Run.TriggerCount} " +
            string.Join(",", outcome.Calls.Select(call => $"{Kind(call.Effect)}:{unchecked((int)call.Value)}"));
    }

    [TestMethod, TestCategory(Category)]
    public void CapMatchesTheOriginal()
    {
        var file = OracleFile.Load("4545E0", "spell-cap", TestContext);
        // Inputs where the original reads a missing parent's type through handle 0 (MG26) are redrawn by the model.
        Run(file, c => Cap(c), c => $"{c["result"]}", (NoSwitch, NoSwitch, _ => false, c => Cap(c)),
            new Mutant("technology records count", c => Cap(c, technologyCounts: true)),
            new("the cap is intelligence / 3", c => Cap(c, divisor: 3)),
            new("unmaintained runs count", c => Cap(c, anyFlags: true)));
    }

    private static string Cap(Row c, bool technologyCounts = false, int divisor = 4, bool anyFlags = false)
    {
        // A spell drawn twice has one record (the same values).
        var records = Enumerable.Range(0, 6).DistinctBy(i => c[$"spell{i}"]).ToDictionary(i => c[$"spell{i}"], i =>
            new SpellRunRecord(!technologyCounts && c[$"spell{i}Technology"] != 0, MaintainPeriod: c[$"spell{i}Period"]));
        var table = SpellRunTable.Empty;
        SpellRun? self = null;
        for (var i = 0; i < c["runs"]; i++)
        {
            var parentIndex = c[$"run{i}Parent"];
            var parent = parentIndex < 0 ? null : new SpellRunObject($"p{parentIndex}", i == 0 ? c["parentType"] : 16);
            var flags = (SpellRunFlags)c[$"run{i}Flags"];
            if (anyFlags) flags |= SpellRunFlags.Processed;
            var run = new SpellRun(c[$"run{i}Slot"], c[$"run{i}Spell"], SpellRunAction.Maintain, i == 0 && c["source"] == 0 ? null : new("source", 16), parent,
                SpellLocation.None, null, SpellLocation.None, null, flags);
            table = table.With(run);
            if (i == 0) self = run;
        }
        var intelligence = divisor == 4 ? c["intelligence"] : c["intelligence"] * 4 / divisor;
        return SpellRunRules.WithinCap(self!, table, intelligence, spell => records[spell]) ? "1" : "0";
    }

    private void Run(OracleFile file, Func<Row, string> real, Func<Row, string> original,
        (string Mode, string Ledger, Func<Row, bool> Applies, Func<Row, string> Compute) other, params Mutant[] mutants)
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
        WriteReceipt(file, method, other.Mode, other.Ledger, ledgerInputs, otherDifferences.Count, outsideLedger.Count, mutants.Length, caught.Count, unexpected.Count, pass);
        if (unexpected.Count > 0)
            Assert.Fail($"{unexpected.Count} differences from the original; first: {unexpected[0]} original {original(unexpected[0])}, Steamwoven (original mode) {real(unexpected[0])}");
        if (outsideLedger.Count > 0)
            Assert.Fail($"the {other.Mode} mode differs outside {other.Ledger}: {outsideLedger[0]} original {original(outsideLedger[0])}, {other.Mode} {other.Compute(outsideLedger[0])}");
        Assert.AreEqual(0, missed.Count, "planted mutants not caught (the inputs do not cover them): " + string.Join(", ", missed));
    }

    /// <summary>The oracle receipt (docs/verification/receipts), in the format of <see cref="ExperienceOriginalExeTests"/>.</summary>
    private void WriteReceipt(OracleFile file, MethodInfo method, string mode, string ledger, int ledgerInputs, int otherDifferences, int outsideLedger,
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
            ["otherMode"] = ledger == NoSwitch ? null : new Dictionary<string, object>
            {
                ["mode"] = mode, ["ledger"] = ledger, ["ledgerInputs"] = ledgerInputs, ["differ"] = otherDifferences, ["outsideLedger"] = outsideLedger,
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
}
