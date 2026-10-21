using System.Reflection;
using System.Text.Json;
using Arcanum.Simulation.Combat;
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

    private static readonly string[] StageObjects = ["source", "parent", "other", "caster"];

    /// <summary>ARC-MAGIC-011: the per-target stage (<see cref="SpellTargetStage.Decide"/>, 0x455550, model
    /// <c>spell-target-stage</c>): the answer, the run's source and the candidate after it, the run flags, the message, the maintain
    /// bar and the reflecting caster's payment; Steamwoven differs only on MG15's inputs (a payment whose last cost is not the
    /// reflected cost).</summary>
    [TestMethod, TestCategory(Category)]
    public void TargetStageMatchesTheOriginal()
    {
        var file = OracleFile.Load("455550", "spell-target-stage", TestContext);
        const int ReflectedCost = 7;
        static int Index(SpellRunObject? o) => o is null ? -1 : Array.IndexOf(StageObjects, o.Id);
        string Stage(Row c, MagicAnomalyProfile profile, Func<SpellTargetStageFacts, SpellTargetStageFacts>? change = null, bool clearReflected = false)
        {
            SpellRunObject Obj(int i) => new(StageObjects[i], i == 2 || i == 3 ? 16 : 15);
            var candidate = c["candidate"] switch { 0 => null, 1 => Obj(1), 2 => Obj(0), _ => Obj(2) };
            var flags = (SpellRunFlags)c["runFlags"] & (clearReflected ? ~SpellRunFlags.Reflected : (SpellRunFlags)~0);
            var run = new SpellRun(0, c["spell"], SpellRunAction.Begin, Obj(0), Obj(1), SpellLocation.None, null, SpellLocation.None, null, flags)
            { SetSpellFlags = c["setFlags"] };
            var facts = new SpellTargetStageFacts(c["targetFlags"], c["sourceFlags"], c["local"] != 0,
                c["found"] != 0 && (c["targetFlags"] & SpellTargetStage.FullReflection) != 0 ? Obj(3) : null, c["lastCost"], ReflectedCost);
            if (change is not null) facts = change(facts);
            var result = SpellTargetStage.Decide(run, new SpellRunRecord { NoReflect = c["noReflect"] != 0 }, new(candidate, SpellLocation.None), facts, profile);
            var pay = result.Payment is { } p ? $"{Index(p.Payer)} {p.Cost} 1 {SpellPaymentRules.NoSpell}" : "-9 0 0 0";
            return $"{(result.Passes ? 1 : 0)} {Index(result.Run.Source)} {Index(result.Target.Object)} {(int)result.Run.Flags} {result.Message ?? -1} {(result.MaintainBar ? 1 : 0)} {pay}";
        }
        static bool Mg15(Row c) => c["payer"] != -9 && c["cost"] != ReflectedCost;
        Run(file, c => Stage(c, MagicAnomalyProfile.Original),
            c => $"{c["result"]} {c["sourceAfter"]} {c["candidateAfter"]} {c["runFlagsAfter"]} {c["message"]} {c["bar"]} {c["payer"]} {c["cost"]} {c["apply"]} {c["paidSpell"]}",
            ("steamwoven", "MG15", Mg15, c => Stage(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("no reflection payment", c => Stage(c, MagicAnomalyProfile.Original, f => f with { ReflectionCaster = null })),
            new("a run reflected earlier is left alone", c => Stage(c, MagicAnomalyProfile.Original, clearReflected: true)),
            new("the caster's own reflection does not fizzle", c => Stage(c, MagicAnomalyProfile.Original, f => f with { SourceSpellFlags = 0 })));
    }

    /// <summary>ARC-MAGIC-011: the no-stack test (<see cref="SpellTargetStage.Blocking"/>, 0x453410, model <c>spell-no-stack</c>).</summary>
    [TestMethod, TestCategory(Category)]
    public void NoStackMatchesTheOriginal()
    {
        var file = OracleFile.Load("453410", "spell-no-stack", TestContext);
        string Block(Row c, bool parentForSelf = true, bool sameIdBlocks = false)
        {
            var (table, records) = Table(c);
            if (c["on"] < 0) return "0 -7";
            var on = new SpellRunObject($"o{c["on"]}", 16);
            int? found = null;
            foreach (var other in table.Runs)
            {
                if ((other.Flags & SpellRunFlags.Active) == 0) continue;
                if (other.Target?.Id == on.Id) { if (other.Spell == c["spell"] && (sameIdBlocks || other.Id != c["run"])) { found = other.Id; break; } }
                else if (other.Target is null && records(other.Spell).SelfSpell && other.Spell == c["spell"] && other.Id != c["run"]
                    && (parentForSelf ? other.Parent : other.Source)?.Id == on.Id) { found = other.Id; break; }
            }
            if (parentForSelf && !sameIdBlocks)
                found = SpellTargetStage.Blocking(table, new(c["run"], c["spell"], SpellRunAction.Begin, null, null, SpellLocation.None, null, SpellLocation.None, null,
                    SpellRunFlags.Active), on, records);
            return found is { } id ? $"1 {id}" : "0 -7";
        }
        Run(file, c => Block(c), c => $"{c["result"]} {c["out"]}", (NoSwitch, NoSwitch, _ => false, c => Block(c)),
            new Mutant("a self spell matched by its source", c => Block(c, parentForSelf: false)),
            new("the run blocks itself", c => Block(c, sameIdBlocks: true)));
    }

    /// <summary>ARC-MAGIC-011: the cancels (<see cref="SpellTargetStage.Cancelled"/>, 0x4534E0, model <c>spell-cancels</c>): the runs
    /// interrupted in order and how often the environment was asked; the second loop compares this record's own fields (MG33).</summary>
    [TestMethod, TestCategory(Category)]
    public void CancelsMatchTheOriginal()
    {
        var file = OracleFile.Load("4534E0", "spell-cancels", TestContext);
        string Cancel(Row c, Func<SpellRunTable, SpellRun, Func<int, SpellRunRecord>, Func<int, bool>, IReadOnlyList<int>>? rule = null)
        {
            var (table, records) = Table(c);
            var asked = 0;
            var run = table[c["run"]]!;
            var ids = (rule ?? SpellTargetStage.Cancelled)(table, run, records, _ => { asked++; return c["env"] != 0; });
            return $"{asked} " + string.Join(",", ids);
        }
        static IReadOnlyList<int> OtherRecordsEnv(SpellRunTable table, SpellRun run, Func<int, SpellRunRecord> records, Func<int, bool> env)
        {
            var ids = new List<int>();
            if ((run.Flags & SpellRunFlags.Active) == 0) return ids;
            var record = records(run.Spell);
            bool Same(SpellRun o) => (o.Flags & SpellRunFlags.Active) != 0 && o.Source?.Id == run.Source?.Id && o.Id != run.Id;
            if (record.CancelsSpellFlags != 0) ids.AddRange(table.Runs.Where(o => Same(o) && (records(o.Spell).CancelsSpellFlags & record.CancelsSpellFlags) != 0).Select(o => o.Id));
            if (record.CancelsEnvironmentSpellFlags != 0 && env(record.CancelsEnvironmentSpellFlags))
                ids.AddRange(table.Runs.Where(o => Same(o) && (records(o.Spell).CancelsEnvironmentSpellFlags & record.CancelsSpellFlags) != 0).Select(o => o.Id));
            return ids;
        }
        Run(file, c => Cancel(c), c => $"{c["envAsked"]} " + string.Join(",", Enumerable.Range(0, 13).Select(i => c[$"interrupt{i}"]).Where(v => v >= 0)),
            (NoSwitch, NoSwitch, _ => false, c => Cancel(c)),
            new Mutant("the second loop compares the other run's Cancels_EnvSF", c => Cancel(c, OtherRecordsEnv)),
            new("no environment loop", c => Cancel(c, (t, r, rec, e) => SpellTargetStage.Cancelled(t, r, rec, _ => false))));
    }

    // The run table and records of a no-stack or cancels case.
    private static (SpellRunTable Table, Func<int, SpellRunRecord> Records) Table(Row c)
    {
        static SpellRunObject? Obj(int i) => i < 0 ? null : new($"o{i}", 16);
        var table = SpellRunTable.Empty;
        for (var i = 0; i < c["runs"]; i++)
            table = table.With(new(c[$"run{i}Slot"], c[$"run{i}Spell"], SpellRunAction.Begin, Obj(c[$"run{i}Source"]), Obj(c[$"run{i}Parent"]), SpellLocation.None,
                Obj(c[$"run{i}Target"]), SpellLocation.None, null, (SpellRunFlags)c[$"run{i}Flags"]));
        var records = new Dictionary<int, SpellRunRecord>();
        for (var i = 0; i < 5; i++)
            records[c[$"spell{i}"]] = new() { SelfSpell = (c[$"spell{i}Self"] & 1) != 0, CancelsSpellFlags = c[$"spell{i}Cancels"], CancelsEnvironmentSpellFlags = c[$"spell{i}Env"] };
        return (table, spell => records.TryGetValue(spell, out var r) ? r : new());
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

    /// <summary>ARC-MAGIC-012: the Damage component (<see cref="SpellDamageRules.Damage"/>, 0x4518D0, model <c>spell-damage</c>): the draw,
    /// the route (the damage step or the acid's), the five amounts, the flags and the damage's source the step is given; the
    /// Steamwoven setting differs only on MG37's inputs (a margin and an amount of 0 or less).</summary>
    [TestMethod, TestCategory(Category)]
    public void DamageComponentMatchesTheOriginal()
    {
        var file = OracleFile.Load("4518D0", "spell-damage", TestContext);
        bool Mg37(Row c) => c["target"] != 0 && c["margin"] != 0 && c["type"] < 5 && (c["flags"] & SpellDamageFlags.Death) == 0 &&
            Packet(c, MagicAnomalyProfile.Original, noMargin: true).Packet!.Damage[(DamageType)c["type"]] <= 0;
        Run(file, c => Damage(c, MagicAnomalyProfile.Original),
            c => $"{c["inits"]} {c["draws"]} {Range(c)} {c["drawn"]} {c["route"]} {c["normal"]} {c["poison"]} {c["electrical"]} {c["fire"]} " +
                 $"{c["fatigueSlot"]} {c["damageFlags"]} {c["contextFlags"]} {c["source"]}",
            ("steamwoven", "MG37", Mg37, c => Damage(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("Scaled for any parent", c => Damage(c, MagicAnomalyProfile.Original, anyParent: true)),
            new("the death flag survives a margin", c => Damage(c, MagicAnomalyProfile.Original, deathSurvives: true)),
            new("a full fatigue hit is the current fatigue", c => Damage(c, MagicAnomalyProfile.Original, fatigueBonus: -10)));
    }

    private static (SpellDamagePacket? Packet, RollReplay Rolls) Packet(Row c, MagicAnomalyProfile profile, bool anyParent = false, bool noMargin = false,
        int fatigueBonus = 0, int clearFlags = 0)
    {
        var rolls = new RollReplay(c["drawn"]);
        if (c["target"] == 0) return (null, rolls);
        var parent = c["parent"] != 0 ? new SpellRunObject("parent", anyParent ? 16 : c["parentType"], c["aptitude"]) : null;
        var run = new SpellRun(0, 0, SpellRunAction.Begin, parent, parent, SpellLocation.None, null, SpellLocation.None,
            c["other"] != 0 ? new SpellRunObject("other", 16) : null, SpellRunFlags.Active, Margin: noMargin ? 0 : c["margin"]);
        var component = new SpellDamageComponent(c["type"], c["min"], c["max"], c["flags"] & ~clearFlags);
        return (SpellDamageRules.Damage(component, run, c["fatigue"] + fatigueBonus, rolls, profile), rolls);
    }

    private static string Damage(Row c, MagicAnomalyProfile profile, bool anyParent = false, bool deathSurvives = false, int fatigueBonus = 0)
    {
        var (packet, rolls) = Packet(c, profile, anyParent, fatigueBonus: fatigueBonus, clearFlags: deathSurvives ? SpellDamageFlags.Death : 0);
        var drawn = rolls.Calls.Count == 0 ? "0 0 0" : $"{rolls.Calls[0].Min} {rolls.Calls[0].Max} {c["drawn"]}";
        var head = $"{(packet is null ? 0 : 1)} {rolls.Calls.Count} {drawn}";
        if (packet is null || packet.Route == SpellDamageRoute.Cancelled) return $"{head} 0 0 0 0 0 0 0 0 -9";
        var d = packet.Damage;
        var flags = packet.Flags | (deathSurvives ? c["flags"] & SpellDamageFlags.Death : 0);
        var source = c["other"] != 0 ? 2 : c["parent"] != 0 ? 0 : -1;
        return $"{head} {(packet.Route == SpellDamageRoute.Step ? 1 : 2)} {d.Normal} {d.Poison} {d.Electrical} {d.Fire} {d.Fatigue} {flags} {0x280} {source}";
    }

    /// <summary>ARC-MAGIC-012: the Heal component (<see cref="SpellDamageRules.Heal"/>, 0x4521A0, model <c>spell-heal</c>): the draw,
    /// the amounts in the hit-point, poison and fatigue slots, and the flags the applier reads.</summary>
    [TestMethod, TestCategory(Category)]
    public void HealComponentMatchesTheOriginal()
    {
        var file = OracleFile.Load("4521A0", "spell-heal", TestContext);
        static string Flags(int f) => string.Join("", new[] { SpellDamageFlags.Full, SpellDamageFlags.Raise, SpellDamageFlags.Scars, SpellDamageFlags.Blind,
            SpellDamageFlags.CrippleArm, SpellDamageFlags.CrippleLeg }.Select(b => (f & b) != 0 ? '1' : '0')) + ((f & SpellDamageFlags.Reanimate) == SpellDamageFlags.Reanimate ? '1' : '0');
        Run(file, c => Heal(c),
            c => c["inits"] == 0 ? "0" : $"{c["draws"]} {Range(c)} {c["drawn"]} {Math.Max(0, c["normal"])} {Math.Max(0, c["poison"])} " +
                 $"{Math.Max(0, c["fatigueSlot"])} {Flags(c["damageFlags"])}",
            (NoSwitch, NoSwitch, _ => false, c => Heal(c)),
            new Mutant("Scaled for any parent", c => Heal(c, anyParent: true)),
            new("electrical heals fatigue", c => Heal(c, typeMap: t => t == 2 ? 4 : t)),
            new("the full heal rolls too", c => Heal(c, fullRolls: true)));
    }

    private static string Heal(Row c, bool anyParent = false, Func<int, int>? typeMap = null, bool fullRolls = false)
    {
        if (c["target"] == 0) return "0";
        static string Flags(LifeHeal h) => string.Concat(new[] { h.Full, h.Resurrect, h.Scars, h.Blindness, h.CrippledArms, h.CrippledLegs, h.Reanimate }
            .Select(b => b ? '1' : '0'));
        var rolls = new RollReplay(c["drawn"]);
        var parent = c["parent"] != 0 ? new SpellRunObject("parent", anyParent ? 16 : c["parentType"], c["aptitude"]) : null;
        var run = new SpellRun(0, 0, SpellRunAction.Begin, parent, parent, SpellLocation.None, null, SpellLocation.None, null, SpellRunFlags.Active);
        var type = typeMap?.Invoke(c["type"]) ?? c["type"];
        var heal = SpellDamageRules.Heal(new(type, c["min"], c["max"], fullRolls ? c["flags"] & ~SpellDamageFlags.Full : c["flags"]), run, rolls);
        if (fullRolls) heal = heal with { Full = (c["flags"] & SpellDamageFlags.Full) != 0 };
        var drawn = rolls.Calls.Count == 0 ? "0 0 0" : $"{rolls.Calls[0].Min} {rolls.Calls[0].Max} {c["drawn"]}";
        return $"{rolls.Calls.Count} {drawn} {heal.HitPoints} {heal.Poison} {heal.Fatigue} {Flags(heal)}";
    }

    /// <summary>ARC-MAGIC-012: the acid's steps (<see cref="SpellDamageRules.Acid"/>, 0x4B5810, model <c>spell-acid</c>): each worn item
    /// in slot order with the amount it is given (the step lowers it in place by the item's resistance, as the model's step stub
    /// does), then the wearer's third; the Steamwoven setting differs only on MG38's inputs (a critter wearing body armour).</summary>
    [TestMethod, TestCategory(Category)]
    public void AcidMatchesTheOriginal()
    {
        var file = OracleFile.Load("4B5810", "spell-acid", TestContext);
        static bool Critter(Row c) => c["targetType"] is 15 or 16;
        Run(file, c => Acid(c, MagicAnomalyProfile.Original),
            c => string.Join(" ", Enumerable.Range(0, c["steps"]).Select(i => $"{c[$"step{i}Slot"]}:{c[$"step{i}Normal"]}")),
            ("steamwoven", "MG38", c => Critter(c) && c["worn1006"] != 0, c => Acid(c, MagicAnomalyProfile.Steamwoven)),
            new Mutant("the wearer takes a half", c => Acid(c, MagicAnomalyProfile.Original, divisor: 2)),
            new("the resistances do not carry over", c => Acid(c, MagicAnomalyProfile.Original, carry: false)),
            new("any target's items are hit", c => Acid(c, MagicAnomalyProfile.Original, anyTarget: true)));
    }

    private static string Acid(Row c, MagicAnomalyProfile profile, int divisor = 3, bool carry = true, bool anyTarget = false)
    {
        var worn = Enumerable.Range(1000, 9).Where(s => c[$"worn{s}"] != 0)
            .Select(s => new SpellAcidItem($"i{s}", s, false, carry ? c[$"resist{s}"] : 0)).ToList();
        var packet = new SpellDamagePacket(SpellDamageRoute.Acid, DamageByType.Zero with { Normal = c["amount"] }, c["flags"]);
        var plan = SpellDamageRules.Acid(packet, anyTarget || c["targetType"] is 15 or 16, worn, profile);
        var steps = plan.Items.Select(i => $"{i.Item.Slot}:{i.Damage.Normal}").ToList();
        if (plan.Wearer is { } share) steps.Add($"-1:{(divisor == 3 ? share.Normal : (carry ? Carried(c, worn) : c["amount"]) / divisor)}");
        return string.Join(" ", steps);
    }

    private static int Carried(Row c, IReadOnlyList<SpellAcidItem> worn) =>
        worn.Aggregate(c["amount"], (n, i) => n - (int)((long)i.NormalResistance * n / 100));

    // The range of the draw, in order (0x4CEF00 swaps a reversed range itself; the rules pass it in order).
    private static string Range(Row c) => $"{Math.Min(c["drawMin"], c["drawMax"])} {Math.Max(c["drawMin"], c["drawMax"])}";

    /// <summary>Replays the model's one draw and records the range it was asked for.</summary>
    private sealed class RollReplay(int value) : ISpellRollSource
    {
        public List<(int Min, int Max)> Calls { get; } = [];

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            Calls.Add((min, max));
            return value;
        }
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
