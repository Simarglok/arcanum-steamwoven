using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-009: run creation, the stage's payments, times and the maintenance cap (ledger RN1–RN14, MG3, MG6, MG11, MG19, MG28,
// MG29). Records, casters and the world doubles are invented.
[TestClass]
public sealed class SpellRunRulesTests
{
    private static readonly SpellObjectFacts Mage = new("mage", 15, new(10, 10), Aptitude: 30, Intelligence: 12);
    private static readonly SpellObjectFacts Orc = new("orc", 16, new(12, 10), Aptitude: 20);
    private static readonly SpellRunRecord Bolt = new(Cost: 6, MaintainCost: 4, WillpowerMinimum: 8, Invocation: new(false, 10, 0, 0, 0));

    private sealed class World : ISpellInvocationWorld
    {
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public bool TargetMatchesBeginArea(SpellInvocation invocation) => true;
        public bool BeginAreaTakesTiles(int spell) => false;
        public SpellLocation FirstTileInBeginArea(SpellInvocation invocation, int radius) => SpellLocation.None;
    }

    private sealed class NoRolls : ISpellRollSource
    {
        public int Roll(SpellRollPurpose purpose, int min, int max) => throw new AssertFailedException("no roll");
    }

    private sealed class NoCells : ISpellCellSource
    {
        public SpellCellPayment Pay(SpellCellRequest request) => throw new AssertFailedException("no cells");
    }

    private sealed class Stacking(int? blocking = null) : ISpellRunStacking
    {
        public int Cancels { get; private set; }
        public int? Blocking(SpellRun run, SpellRunObject target) => blocking;
        public void Cancel(SpellRun run) => Cancels++;
    }

    private static SpellRunCreation Create(SpellRunRequest request, SpellRunRecord? record = null, SpellRunTable? table = null,
        MagicAnomalyProfile? profile = null) =>
        SpellRunRules.Create(table ?? SpellRunTable.Empty, request, record ?? Bolt, new World(), new NoRolls(), profile ?? MagicAnomalyProfile.Steamwoven);

    private static SpellInvocation AtOrc => new(33, Mage, Mage, Orc, SpellLocation.None);

    [TestMethod]
    public void CreationChecksFirstTakesTheFirstFreeSlotAndCapturesTheObjects()
    {
        var created = Create(new(AtOrc, Triggers: 0x4), Bolt with { TriggerCount = 3 });
        Assert.AreEqual(SpellRunCreationStatus.Created, created.Status);
        var run = created.Run!;
        Assert.AreEqual((0, 33, SpellRunAction.Begin, 0x4, 3), (run.Id, run.Spell, run.Action, run.Triggers, run.TriggerCount));
        Assert.AreEqual((new SpellRunObject("mage", 15, 30), new SpellRunObject("orc", 16, 20)), (run.Parent, run.Target), "aptitudes captured for critters");
        Assert.AreEqual((SpellRunRules.GoalCast, false), (created.CastGoal, created.BeginEvent));
        Assert.AreEqual(SpellRunRules.GoalCastAnimation, Create(new(AtOrc), Bolt with { CastAnimation = 3 }).CastGoal);
        Assert.AreEqual(SpellRunRules.GoalCastWithoutGoal, Create(new(AtOrc, SkipCastGoal: true)).CastGoal);
        var trap = new SpellObjectFacts("trap", 17, new(1, 1), Aptitude: 99);
        var sourceless = Create(new(new SpellInvocation(33, null, trap, Orc, SpellLocation.None)));
        Assert.AreEqual((0, true, 0), (sourceless.CastGoal, sourceless.BeginEvent, sourceless.Run!.Source!.Aptitude), "no parent: a BEGIN event; no aptitude for a trap");
        Assert.AreEqual(1, Create(new(AtOrc), table: created.Table).Run!.Id, "the next free slot");
        var wand = new SpellObjectFacts("wand", 5, new(3, 4));
        Assert.AreEqual(new SpellLocation(3, 4), Create(new(AtOrc with { Source = wand })).Run!.StartLocation, "the source's location, not the holder's");
        // ARC-MAGIC-049: the Cast component's invocation has no source but the parent's location at +0x38, which is the run's start location.
        Assert.AreEqual(new SpellLocation(9, 9), Create(new(AtOrc with { Source = null, SourceLocation = new SpellLocation(9, 9) })).Run!.StartLocation, "the invocation's own source location");
        Assert.AreEqual(new SpellLocation(9, 9), Create(new(AtOrc with { Source = wand, SourceLocation = new SpellLocation(9, 9) })).Run!.StartLocation, "the explicit location wins over the source's");
        Assert.AreEqual(SpellLocation.None, Create(new(AtOrc with { Source = null })).Run!.StartLocation, "no source and no location: none");
        Assert.AreEqual(SpellRunCreationStatus.Refused, Create(new(AtOrc with { Spell = 300 })).Status, "the invocation check first");
        Assert.IsNull(run.Other, "no other object without one");
        Assert.AreEqual(new SpellRunObject("orc", 16, 20), Create(new(AtOrc with { Other = Orc })).Run!.Other, "ARC-MAGIC-025: the invocation's other object");
        Assert.AreEqual(SpellRunCreationStatus.Disabled, SpellRunRules.Create(SpellRunTable.Empty, new(AtOrc), Bolt, new World(), new NoRolls(),
            MagicAnomalyProfile.Steamwoven, magicDisabled: true).Status);
    }

    [TestMethod]
    public void Mg28AFullTableRefusesTheCastAndMg19KeepsTheFlagsApart()
    {
        var table = SpellRunTable.Empty;
        for (var i = 0; i < SpellRunTable.Slots; i++) table = Create(new(AtOrc), table: table).Table;
        Assert.AreEqual((SpellRunTable.Slots, -1), (table.Count, table.FirstFree()));
        Assert.AreEqual(SpellRunCreationStatus.TableFull, Create(new(AtOrc), table: table).Status, "the original quits the game here");
        Assert.AreEqual(5, Create(new(AtOrc), table: table.Free(5)).Run!.Id);

        var steam = Create(new(AtOrc, Free: true, Unresistable: true)).Run!.Flags;
        Assert.AreEqual(SpellRunFlags.Active | SpellRunFlags.Free | SpellRunFlags.Unresistable, steam);
        var original = Create(new(AtOrc), Bolt with { NoResist = true }, profile: MagicAnomalyProfile.Original).Run!.Flags;
        Assert.IsTrue((original & SpellRunFlags.FinalEnd) != 0, "MG19: one bit, so the run's END counts as scheduled");
        var run = Create(new(AtOrc)).Run!;
        Assert.AreEqual(SpellRunFlags.Active | SpellRunFlags.FinalEnd, SpellRunRules.CountTrigger(run, MagicAnomalyProfile.Steamwoven).Flags);
        Assert.AreEqual(SpellRunFlags.Active | SpellRunFlags.FinalEnd | SpellRunFlags.Unresistable, SpellRunRules.CountTrigger(run, MagicAnomalyProfile.Original).Flags);
        Assert.AreEqual((SpellRunAction.Begin, 1), (SpellRunRules.CountTrigger(run with { TriggerCount = 2 }, MagicAnomalyProfile.Steamwoven) is var counted
            ? (counted.Action, counted.TriggerCount) : default));
    }

    private static SpellRun Run(SpellRunAction action, int margin = 0, SpellRunFlags flags = SpellRunFlags.Active) =>
        new(0, 33, action, new("mage", 15), new("mage", 15), SpellLocation.None, new("orc", 16), SpellLocation.None, null, flags, Margin: margin);

    private static SpellStageFacts Facts(int fatigue = 20, int intelligence = 12, int willpower = 12, bool local = true) =>
        new(ParentIntelligence: intelligence, ParentWillpower: willpower, ParentIsLocalPlayer: local, SourcePayer: new(SpellPayerKind.Critter, CurrentFatigue: fatigue),
            SourceCaster: new(true, 0, -1));

    [TestMethod]
    public void BeginChecksAndPaysUnlessTechnologyThenAsksTheStackingRules()
    {
        var paid = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 1, 6), (paid.Proceeds, paid.Payments.Count, paid.Costs[0]));
        var poor = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt, Facts(fatigue: -10), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, SpellStageStatus.BeginCost, (int?)SpellRunRules.MessageCost), (poor.Proceeds, poor.Status, poor.Message));
        var dull = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt, Facts(intelligence: 4, local: false), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, SpellStageStatus.Checks, (int?)null, 0), (dull.Proceeds, dull.Status, dull.Message, dull.Payments.Count), "no message for an NPC");
        var tech = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt with { Technology = true }, Facts(intelligence: 1, willpower: 1), new NoCells(), new Stacking(),
            MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 0), (tech.Proceeds, tech.Payments.Count), "a technology record skips the checks and the cost at BEGIN");
        Assert.AreEqual(0, SpellRunRules.Stage(Run(SpellRunAction.Begin, flags: SpellRunFlags.Active | SpellRunFlags.Free), Bolt, Facts(), new NoCells(),
            new Stacking(), MagicAnomalyProfile.Steamwoven).Payments.Count, "a free run");
        var blocked = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt with { StackingRules = true }, Facts(), new NoCells(), new Stacking(blocking: 7),
            MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, (int?)7), (blocked.Proceeds, blocked.BlockedBy));
        var stacking = new Stacking();
        SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt, Facts(), new NoCells(), stacking, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual(1, stacking.Cancels);
        var dead = SpellRunRules.Stage(Run(SpellRunAction.Begin), Bolt, Facts() with { SourceDead = true }, new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, SpellStageStatus.SourceDown, (int?)null), (dead.Proceeds, dead.Status, dead.Message));
    }

    [TestMethod]
    public void MaintainChecksTechnologyToo_Mg3PaysOnlyTheSurcharge_Mg29OnlyInTheOriginal()
    {
        var tech = SpellRunRules.Stage(Run(SpellRunAction.Maintain), Bolt with { Technology = true }, Facts(intelligence: 3), new NoCells(), new Stacking(),
            MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, true, (int?)SpellRunRules.MessageChecks), (tech.Proceeds, tech.Ends, tech.Message), "MAINTAIN does not test the technology flag");
        // Cost 4, margin 30: surcharge 4 + max(1, 120 / 100) = 5.
        var original = SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), Bolt, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Original);
        CollectionAssert.AreEqual(new[] { 4, 5 }, original.Costs.ToArray());
        var steam = SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), Bolt, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { 5 }, steam.Costs.ToArray());
        // MG29 (owner decision 2026-10-21): the original charges the surcharge for a free run, after a failed check and at a
        // maintenance cost of 0 (then 1); Steamwoven in none of the three.
        var freeRun = Run(SpellRunAction.Maintain, 30, SpellRunFlags.Active | SpellRunFlags.Free);
        CollectionAssert.AreEqual(new[] { 5 }, SpellRunRules.Stage(freeRun, Bolt, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Original).Costs.ToArray(),
            "MG29: a free run still pays the surcharge in the original");
        Assert.AreEqual(0, SpellRunRules.Stage(freeRun, Bolt, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven).Costs.Count);
        var weak = SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), Bolt, Facts(willpower: 2), new NoCells(), new Stacking(), MagicAnomalyProfile.Original);
        Assert.AreEqual((false, true, 1), (weak.Proceeds, weak.Ends, weak.Costs.Count), "MG29: the surcharge after a failed willpower check");
        var weakSteam = SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), Bolt, Facts(willpower: 2), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((false, true, 0), (weakSteam.Proceeds, weakSteam.Ends, weakSteam.Costs.Count), "no surcharge after a failure");
        var timed = Bolt with { MaintainCost = 0 };
        CollectionAssert.AreEqual(new[] { 0, 1 }, SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), timed, Facts(), new NoCells(), new Stacking(),
            MagicAnomalyProfile.Original).Costs.ToArray(), "a maintenance cost of 0 pays a surcharge of 1 in the original");
        Assert.AreEqual(0, SpellRunRules.Stage(Run(SpellRunAction.Maintain, 30), timed, Facts(), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven).Costs.Count);
        var broke = SpellRunRules.Stage(Run(SpellRunAction.Maintain), Bolt, Facts(fatigue: -12), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((SpellStageStatus.MaintainCost, (int?)SpellRunRules.MessageMaintain, true), (broke.Status, broke.Message, broke.Ends));
        Assert.AreEqual(5, SpellRunRules.SurchargedCost(4, 1, new(true, 0, -1)), "at least 1");
        Assert.AreEqual(10, SpellRunRules.SurchargedCost(4, 1, new(true, SpellCostRules.DwarfRace, -1)), "a dwarf pays double");
        Assert.IsTrue(SpellRunRules.Stage(Run(SpellRunAction.End), Bolt, Facts(intelligence: 0), new NoCells(), new Stacking(), MagicAnomalyProfile.Steamwoven).Proceeds);
    }

    [TestMethod]
    public void TimesDoubleForTheMasteredCollegeAndMg6NeverScales()
    {
        var mastered = new SpellTimeFacts(true, 33 / 5, true, 80, -80);
        Assert.AreEqual(2000u, SpellRunRules.ScaleTime(1000, 33, mastered));
        Assert.AreEqual(1000u, SpellRunRules.ScaleTime(1000, 33, mastered with { MasteredCollege = 0 }));
        Assert.AreEqual(1000u, SpellRunRules.ScaleTime(1000, 33, mastered with { ParentIsCritter = false }));
        Assert.AreEqual(1000u, SpellRunRules.ScaleTime(1000, 140, new(true, 140 / 5)), "only spells 0..79");
        // Duration 10 s, DurationStatInfo: 2 s per point below level 15 of the parent's stat 12: 16 s → 16000 ms × 8.
        Assert.AreEqual(16000u * 8, SpellRunRules.DurationTime(new(10, 3, 15, 2), 140, 12, new(false)));
        Assert.AreEqual(10000u * 8, SpellRunRules.DurationTime(new(10, 3, 15, 2), 140, 20, new(false)), "a stat above the level adds nothing");
        Assert.AreEqual(1000u * 8, SpellRunRules.DurationTime(new(0), 140, null, new(false)), "0 seconds counts 1");
        Assert.AreEqual(5000u * 8, SpellRunRules.MaintainTime(5, 140, new(false)));
        Assert.AreEqual(8u, SpellRunRules.MaintainTime(0, 140, new(false)), "a period of 0 is one millisecond");
        var run = new SpellRun(0, 33, SpellRunAction.Begin, null, null, SpellLocation.None, new("orc", 16), SpellLocation.None, null, SpellRunFlags.Active);
        Assert.AreEqual("orc", SpellRunRules.DurationStatHolder(run)!.Id, "without a parent the target's stat");
        Assert.IsNull(SpellRunRules.DurationStatHolder(run with { Parent = new("wand", 6) }), "a parent that is no critter: none");
    }

    [TestMethod]
    public void Mg11TheCapCountsHeldSlotsAndTheCountIsZeroInTheOriginal()
    {
        var mage = new SpellRunObject("mage", 15);
        var records = new Dictionary<int, SpellRunRecord> { [1] = new(MaintainPeriod: 5), [2] = new(Technology: true, MaintainPeriod: 5), [3] = new() };
        var table = SpellRunTable.Empty;
        SpellRun Held(int id, int spell, SpellRunFlags extra = SpellRunFlags.Processed) =>
            new(id, spell, SpellRunAction.Maintain, mage, mage, SpellLocation.None, null, SpellLocation.None, null, SpellRunFlags.Active | extra);
        foreach (var run in new[] { Held(0, 1), Held(1, 1), Held(2, 2), Held(3, 3), Held(4, 1, SpellRunFlags.None) }) table = table.With(run);
        Assert.AreEqual(2, SpellRunRules.MaintainedRuns(table, mage, s => records[s], MagicAnomalyProfile.Steamwoven), "magic, maintained, with a period");
        Assert.AreEqual(0, SpellRunRules.MaintainedRuns(table, mage, s => records[s], MagicAnomalyProfile.Original));
        Assert.AreEqual(3 - 2, SpellRunRules.FreeMaintainSlots(table, mage, 12, s => records[s], MagicAnomalyProfile.Steamwoven));
        Assert.AreEqual(3, SpellRunRules.FreeMaintainSlots(table, mage, 12, s => records[s], MagicAnomalyProfile.Original));
        Assert.IsTrue(SpellRunRules.WithinCap(table[0]!, table, 8, s => records[s]), "2 runs, 8 / 4 = 2 slots");
        Assert.IsFalse(SpellRunRules.WithinCap(table[0]!, table, 7, s => records[s]));
        Assert.IsFalse(SpellRunRules.WithinCap(table[0]! with { Parent = new("wand", 6) }, table, 40, s => records[s]), "a parent that is no critter");
        Assert.IsTrue(SpellRunRules.WithinCap(table[0]! with { Source = null }, table, 0, s => records[s]), "no source");
        Assert.AreEqual(-1, SpellRunRules.MaintainSlots(-5), "toward zero");
    }
}
