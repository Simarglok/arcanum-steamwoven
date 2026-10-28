using System.Collections.Immutable;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-010: the run processing, scheduler, events and interrupts (ledger RP1–RP14, MG19's scheduler side). Records,
// objects and the host are invented.
[TestClass]
public sealed class SpellRunSystemTests
{
    private static readonly SpellRunObject Hero = new("hero", SpellObjectFacts.PcType, 10);
    private static readonly SpellRunObject Orc = new("orc", SpellObjectFacts.NpcType, 0);
    private static readonly SpellRunObject Wand = new("wand", 5);

    private sealed class Host : ISpellRunHost
    {
        public Dictionary<int, SpellRunRecord> Records { get; } = [];
        public bool Allow { get; set; } = true;
        public int SourceFlags { get; set; }
        public string? LocalPlayer { get; set; } = "hero";
        public List<SpellRunTarget> Targets { get; set; } = [new(Orc, new(5, 5))];
        public bool Fate { get; set; }
        public bool Takes { get; set; } = true;
        /// <summary>Objects that resist the next time the resistance asks about them (once each).</summary>
        public HashSet<string> Resisting { get; } = [];
        public bool Passes { get; set; } = true;
        public bool BarFree { get; set; } = true;
        public int Intelligence { get; set; } = 12;
        public Func<int, SpellComponentOutcome> Outcome { get; set; } = _ => SpellComponentOutcome.None;
        public List<(int Run, SpellRunAction Action, int Component, string? Target)> Applied { get; } = [];

        public SpellRunRecord Record(int spell) => Records.TryGetValue(spell, out var r) ? r : new();
        public SpellStageFacts StageFacts(SpellRun run) => new(false, false, Intelligence, 20, run.Parent?.Id == LocalPlayer,
            new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), new(true, 0, -1));
        public ISpellCellSource Cells { get; } = new NoCells();
        public Dictionary<string, int> Flags { get; } = [];
        public bool FlagsAllow(SpellRun run) => Allow;
        public int SpellFlags(SpellRunObject who) => Flags.TryGetValue(who.Id, out var f) ? f : who.Id is "hero" or "wand" ? SourceFlags : 0;
        public int? PlayerSpellFlags => 0;
        public bool PlayerPerceptionExtraordinary => false;
        public SpellPayer? Payer(SpellRunObject who) => new(SpellPayerKind.Critter, CurrentFatigue: 50);
        public SpellResistRecord ResistRecord(int spell) => new(spell, false, false, false, 0, SpellResistanceRules.NoSave, 0);
        public SpellResistTarget ResistTarget(SpellRun run, SpellRunTarget target) =>
            new(false, SpellTargetKind.Npc, false, Takes && !Resisting.Remove(target.Object?.Id ?? "") ? 0 : 100, 0, 0, false);
        public ISpellRollSource Rolls { get; } = new FixedRolls();
        public ISpellFateSave FateSave => SpellFateSave.None;
        public bool IsLocalPlayer(SpellRunObject? who) => who?.Id is { } id && id == LocalPlayer;
        public IReadOnlyList<SpellRunTarget> Collect(SpellRun run) => Targets;
        public bool UseSpellAtMaximum(SpellRunObject pc) => Fate;
        public SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target) => new(Passes, target);
        public SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target)
        {
            Applied.Add((run.Id, run.Action, component, target.Object?.Id));
            return Outcome(component);
        }
        public SpellTimeFacts TimeFacts(SpellRun run) => new(true);
        public int? Stat(SpellRunObject who, int stat) => stat == 4 ? Intelligence : 8;
        public bool MaintainBarHasSlot(SpellRun run) => BarFree;
        public IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run) => [new(SpellPaymentStatus.Paid, 3, [], 3, false, false, null)];
    }

    private sealed class NoCells : ISpellCellSource
    {
        public SpellCellPayment Pay(SpellCellRequest request) => throw new AssertFailedException("no cells");
    }

    private sealed class FixedRolls : ISpellRollSource
    {
        public int Roll(SpellRollPurpose purpose, int min, int max) => min;
    }

    private static SpellRunRecord Record(int begin = 1, int maintain = 0, int end = 0, int callback = 0, int endCallback = 0) =>
        new(Duration: new(0)) { ActionComponents = [begin, maintain, end, callback, endCallback] };

    private static SpellRun Run(int id = 0, SpellRunObject? source = null, SpellRunObject? parent = null, SpellRunFlags flags = SpellRunFlags.Active | SpellRunFlags.Free,
        int spell = 7, SpellRunAction action = SpellRunAction.Begin) =>
        new(id, spell, action, source ?? Hero, parent ?? Hero, SpellLocation.None, Orc, SpellLocation.None, null, flags);

    private static SpellRunSystem System(Host host, params SpellRun[] runs) =>
        new(runs.Aggregate(SpellRunTable.Empty, (t, r) => t.With(r)), host, MagicAnomalyProfile.Steamwoven);

    [TestMethod]
    public void AnInstantSpellAppliesItsBeginThenEndsAndIsFreed()
    {
        var host = new Host();
        host.Records[7] = Record(begin: 2, end: 1);
        var system = System(host, Run());
        system.Begin(0);
        CollectionAssert.AreEqual(new[] { (0, SpellRunAction.Begin, 0, (string?)"orc"), (0, SpellRunAction.Begin, 1, "orc"), (0, SpellRunAction.End, 0, "orc") },
            host.Applied.ToArray(), "BEGIN's two components, then the END's at once (no duration, no maintenance)");
        Assert.IsNull(system.Table[0], "freed after its END");
        Assert.IsTrue(system.Notes.Any(n => n.Kind == SpellRunNoteKind.Freed) && system.Notes.Any(n => n.Kind == SpellRunNoteKind.MaintainBar), "the local player's bar is refreshed");
        Assert.AreEqual(0, system.Queued.Count);
    }

    [TestMethod]
    public void NothingAppliedEndsTheRunAndAResistedTargetIsReported()
    {
        var host = new Host { Takes = false };
        host.Records[7] = Record() with { Duration = new(30) };
        var system = System(host, Run());
        system.Begin(0);
        Assert.IsNull(system.Table[0], "every target resisted: nothing applied, END at once");
        Assert.AreEqual(1, system.Notes.Count(n => n.Kind == SpellRunNoteKind.Resisted));
        var empty = new Host { Targets = [] };
        empty.Records[7] = Record() with { Duration = new(30) };
        var listless = System(empty, Run());
        listless.Begin(0);
        Assert.AreEqual((SpellRunAction.End, 1), (listless.Table[0]!.Action, listless.Queued.Count),
            "an empty list counts as applied: the duration tick is queued (no trigger count: it is the END)");
    }

    [TestMethod]
    public void ATimedSpellTicksItsTriggerCountThenEnds()
    {
        var host = new Host();
        host.Records[7] = Record(maintain: 1) with { Duration = new(10), TriggerCount = 2 };
        var system = System(host, Run() with { TriggerCount = 2 });
        system.Begin(0);
        var tick = system.Queued.Single();
        Assert.AreEqual((SpellRunEventCode.Tick, 80_000L, 1), (tick.Code, tick.DelayMs, system.Table[0]!.TriggerCount), "10 s × 1000 × 8; one trigger fewer");
        system.Fire(0, SpellRunEventCode.Tick);
        Assert.AreEqual((SpellRunAction.Maintain, 0), (system.Table[0]!.Action, system.Table[0]!.TriggerCount), "the tick turns BEGIN to MAINTAIN");
        system.Fire(0, SpellRunEventCode.Tick);
        Assert.AreEqual(SpellRunAction.End, system.Table[0]!.Action, "no trigger left: the next tick is the END");
        Assert.AreNotEqual(SpellRunFlags.None, system.Table[0]!.Flags & SpellRunFlags.FinalEnd);
        system.Fire(0, SpellRunEventCode.Tick);
        Assert.IsNull(system.Table[0]);
        Assert.AreEqual(2, host.Applied.Count(a => a.Action == SpellRunAction.Maintain), "DurationTriggerCount + 1 periods, two MAINTAIN passes");
    }

    [TestMethod]
    public void AMaintainedSpellTicksChecksTheCapAndTheMaintainBar()
    {
        var host = new Host();
        host.Records[7] = Record(maintain: 1) with { MaintainPeriod = 5 };
        var system = System(host, Run());
        system.Begin(0);
        Assert.AreEqual((SpellRunAction.Maintain, SpellRunEventCode.Tick, 40_000L), (system.Table[0]!.Action, system.Queued[0].Code, system.Queued[0].DelayMs));
        system.Begin(0);
        Assert.AreEqual(1, system.Queued.Count, "one tick per run (0x4548D0)");

        var full = new Host { BarFree = false };
        full.Records[7] = host.Records[7];
        var bar = System(full, Run());
        bar.Begin(0);
        Assert.AreEqual((null, 0), (bar.Table[0], bar.Queued.Count), "the local player's maintain bar is full: END at once, its tick freed with it");

        var dull = new Host { Intelligence = 7 };
        dull.Records[7] = host.Records[7];
        var capped = System(dull, Run(), Run(1, flags: SpellRunFlags.Active | SpellRunFlags.Processed));
        capped.Begin(0);
        Assert.IsNull(capped.Table[0], "intelligence 7 holds one slot, which the other run holds: the cap ends it");
        Assert.IsTrue(capped.Notes.Any(n => n is { Kind: SpellRunNoteKind.Message, Value: SpellRunSystem.MessageMaintain }));
    }

    [TestMethod]
    public void ASourcelessMaintainedRunTicksItsCountThenEnds()
    {
        var host = new Host();
        host.Records[7] = Record(maintain: 1) with { MaintainPeriod = 1 };
        var system = System(host, Run(source: null, parent: Orc) with { Source = null });
        system.Begin(0);
        Assert.AreEqual((SpellRunSystem.SourcelessTicks, 1), (system.Table[0]!.TriggerCount, system.Queued.Count), "no count: 8 ticks");
        var ticks = 0;
        while (system.Table[0] is not null && ticks < 20) { system.Fire(0, SpellRunEventCode.Tick); ticks++; }
        Assert.AreEqual(8, ticks, "seven ticks count down, the eighth ends it");
    }

    [TestMethod]
    public void Mg19TheOriginalEndsAnUnresistableRunAfterBegin()
    {
        var host = new Host();
        host.Records[7] = Record() with { Duration = new(10) };
        var flags = SpellRunFlags.Active | SpellRunFlags.Free;
        var original = new SpellRunSystem(SpellRunTable.Empty.With(Run(flags: flags | SpellRunRules.Unresistable(MagicAnomalyProfile.Original))), host,
            MagicAnomalyProfile.Original);
        original.Begin(0);
        Assert.IsNull(original.Table[0], "one bit: unresistable is the final END");
        var steam = System(host, Run(flags: flags | SpellRunRules.Unresistable(MagicAnomalyProfile.Steamwoven)));
        steam.Begin(0);
        Assert.AreEqual(1, steam.Queued.Count, "two flags: the duration goes on");
    }

    [TestMethod]
    public void RefusalsQueueTheInterruptAndAShelledSourceIsToldWhy()
    {
        var host = new Host { Allow = false };
        host.Records[7] = Record();
        var system = System(host, Run());
        system.Begin(0);
        Assert.AreEqual((SpellRunEventCode.Interrupt, SpellRunSystem.InterruptDelayMs), (system.Queued.Single().Code, system.Queued.Single().DelayMs));
        system.Fire(0, SpellRunEventCode.Interrupt);
        Assert.IsNull(system.Table[0], "the interrupt ends and frees it");

        var shelled = new Host { SourceFlags = SpellRunSystem.AntiMagicShell };
        shelled.Records[7] = Record();
        var shell = System(shelled, Run());
        shell.Begin(0);
        Assert.IsTrue(shell.Notes.Any(n => n is { Kind: SpellRunNoteKind.Message, Value: SpellRunSystem.MessageShelled }) && shell.Queued.Single().Code == SpellRunEventCode.Interrupt);
        Assert.AreEqual(0, shelled.Applied.Count);
        var own = System(shelled, Run() with { SetSpellFlags = SpellRunSystem.AntiMagicShell });
        own.Begin(0);
        Assert.AreEqual(1, shelled.Applied.Count, "the shell's own run goes on");
    }

    [TestMethod]
    public void CallbacksWaitOrRunAtOnceAndTheInterruptPicksTheCallbackAction()
    {
        var host = new Host { Outcome = _ => new(CallbackPending: true) };
        host.Records[7] = Record(callback: 1) with { Duration = new(5) };
        var system = System(host, Run());
        system.Begin(0);
        Assert.AreEqual((SpellRunAction.End, 1), (system.Table[0]!.Action, system.Queued.Count), "a timed BEGIN schedules its tick before the tail");
        var waiting = new Host { Outcome = _ => new(CallbackPending: true) };
        waiting.Records[7] = Record(callback: 1);
        var wait = System(waiting, Run());
        wait.Begin(0);
        Assert.AreEqual((SpellRunAction.Begin, 0), (wait.Table[0]!.Action, wait.Queued.Count), "an untimed BEGIN waits for its callback");
        wait.SetAndProcess(0, SpellRunAction.Callback);
        Assert.IsNull(wait.Table[0], "the callback came: no END components, END and free");

        var now = new Host();
        now.Outcome = c => now.Applied.Count == 1 ? new(CallbackNow: true, EndCallback: true) : SpellComponentOutcome.None;
        now.Records[7] = Record(endCallback: 1);
        var immediate = System(now, Run());
        immediate.Begin(0);
        Assert.AreEqual(SpellRunAction.EndCallback, now.Applied[1].Action, "the animation could not start: the END callback at once");
        Assert.IsNull(immediate.Table[0], "then the run is freed");

        var interrupted = new Host();
        interrupted.Records[7] = Record(maintain: 1, callback: 1) with { MaintainPeriod = 5 };
        var stop = System(interrupted, Run(action: SpellRunAction.Maintain));
        stop.Interrupt(0);
        Assert.AreEqual(SpellRunAction.Callback, interrupted.Applied.Last().Action, "MAINTAIN interrupted with callback components: CALLBACK");
        interrupted.Records[8] = Record(endCallback: 1);
        var ending = System(interrupted, Run(spell: 8));
        ending.Interrupt(0);
        Assert.AreEqual((SpellRunAction.EndCallback, 8), (interrupted.Applied.Last().Action, ending.Table[0] is null ? 8 : -1));
    }

    [TestMethod]
    public void ComponentsJumpSwapTheSourceAndTheFateMaximises()
    {
        var host = new Host { Fate = true, Targets = [new(Orc, new(1, 1)), new(Wand, new(2, 2))] };
        host.Outcome = c => c == 0 ? new(JumpTo: 1) : SpellComponentOutcome.None;
        host.Records[7] = Record(begin: 3);
        var system = System(host, Run());
        system.Begin(0);
        CollectionAssert.AreEqual(new[] { 0, 2, 0, 2 }, host.Applied.Select(a => a.Component).ToArray(), "a jump to 1 goes on at 2");
        Assert.IsTrue(system.Notes.Any(n => n.Kind == SpellRunNoteKind.SpellAtMaximum));

        var swap = new Host();
        swap.Outcome = c => new(ReleasesSource: SpellSourceRelease.Detach);
        swap.Records[7] = Record() with { Duration = new(10) };
        var swapped = System(swap, Run(source: Wand));
        swapped.Begin(0);
        Assert.AreEqual(("orc", (string?)"hero"), (swapped.Table[0]!.Source?.Id, swapped.Table[0]!.Parent?.Id),
            "a source detached during the pass: the current target becomes the source");
    }

    [TestMethod]
    public void TheEndingsSelectTheirRuns()
    {
        var host = new Host();
        host.Records[7] = Record() with { MaintainPeriod = 5 };
        host.Records[9] = Record() with { ItemTriggers = true };
        host.Records[11] = Record() with { Technology = true, MaintainPeriod = 5 };
        var processed = SpellRunFlags.Active | SpellRunFlags.Processed;
        var system = System(host, Run(0, flags: processed), Run(1, spell: 9, flags: processed), Run(2, spell: 11, flags: processed), Run(3, source: Orc, flags: processed));
        system.Demaintain("hero");
        CollectionAssert.AreEqual(new[] { 0 }, system.Queued.Select(e => e.Run).ToArray(), "magic, no triggers or maintained, of that source");

        system.DetachSource(3);
        Assert.AreEqual((null, (string?)"hero"), (system.Table[3]!.Source, system.Table[3]!.Parent?.Id));
        Assert.AreNotEqual(SpellRunFlags.None, system.Table[3]!.Flags & SpellRunFlags.FinalEnd);
        system.DetachSource(0);
        Assert.IsNull(system.Table[0]!.Parent, "a parent that is the source goes with it");
        system.ClearSources("hero");
        Assert.IsTrue(system.Table.Runs.All(r => r.Source is null || r.Source.Id != "hero"));

        var never = System(host, Run(5));
        never.EndByEvent(5);
        Assert.IsTrue(never.Table[5] is null && never.Notes.Any(n => n.Kind == SpellRunNoteKind.Payments), "a BEGIN never processed is charged and freed");

        var stop = System(host, Run(0), Run(1));
        stop.Stop(new SpellInvocation(7, new("hero", 15, new(1, 1)), new("hero", 15, new(1, 1)), new("orc", 16, new(2, 2)), SpellLocation.None));
        Assert.IsTrue(stop.Table[0] is null && stop.Table[1] is not null, "the first match is interrupted");
        stop.ForceEnd(1);
        Assert.IsNull(stop.Table[1]);
    }

    // ARC-MAGIC-011: the stage's cancels, the per-target stage and the resistance in the run system.
    [TestMethod]
    public void Mg30TheCancelledRunIsLeftInTheOriginalAndProcessedAfterThePassInSteamwoven()
    {
        var host = new Host();
        host.Records[7] = Record() with { CancelsSpellFlags = 0x4 };
        host.Records[8] = Record(maintain: 1) with { MaintainPeriod = 5, CancelsSpellFlags = 0x4 };
        var held = Run(1, spell: 8, flags: SpellRunFlags.Active | SpellRunFlags.Processed | SpellRunFlags.Free, action: SpellRunAction.Maintain);
        var original = new SpellRunSystem(SpellRunTable.Empty.With(Run()).With(held), host, MagicAnomalyProfile.Original, pendingTicks: [1]);
        original.Begin(0);
        Assert.AreEqual(SpellRunAction.End, original.Table[1]!.Action, "interrupted: END, but never processed");
        Assert.IsTrue(original.RemovedTicks.Contains(1) && original.Notes.Any(n => n is { Kind: SpellRunNoteKind.ReEntryRefused, Run: 1 }));
        var steam = new SpellRunSystem(SpellRunTable.Empty.With(Run()).With(held), host, MagicAnomalyProfile.Steamwoven, pendingTicks: [1]);
        steam.Begin(0);
        Assert.IsNull(steam.Table[1], "deferred, then its END processed and the run freed");
        Assert.IsTrue(steam.Notes.Any(n => n is { Kind: SpellRunNoteKind.Deferred, Run: 1 }));
    }

    [TestMethod]
    public void Mg32TheFreeRemovesEveryEventInSteamwoven()
    {
        var host = new Host();
        host.Records[7] = Record();
        var original = new SpellRunSystem(SpellRunTable.Empty.With(Run(5, flags: SpellRunFlags.Active | SpellRunFlags.Processed)), host, MagicAnomalyProfile.Original);
        original.EndByEvent(5);
        original.Free(5, chargeBegin: false);
        Assert.AreEqual((1, 0), (original.Queued.Count, original.RemovedEvents.Count), "the interrupt outlives the free");
        var steam = System(host, Run(5, flags: SpellRunFlags.Active | SpellRunFlags.Processed));
        steam.EndByEvent(5);
        steam.Free(5, chargeBegin: false);
        Assert.AreEqual((0, 5), (steam.Queued.Count, steam.RemovedEvents.Single()));
    }

    [TestMethod]
    public void AReflectingTargetTurnsTheRunOnItsCasterForEveryLaterTarget()
    {
        var host = new Host { Targets = [new(Orc, new(5, 5)), new(Wand, new(6, 6))] };
        host.Flags["orc"] = SpellTargetStage.FullReflection;
        host.Records[7] = Record(end: 0) with { Cost = 3, Duration = new(30) };
        var system = System(host, Run(flags: SpellRunFlags.Active));
        system.Begin(0);
        // The orc reflects: its component hits the caster; the run stays reflected, so the wand's also turns on the caster.
        Assert.AreEqual("hero,hero", string.Join(",", host.Applied.Where(a => a.Action == SpellRunAction.Begin).Select(a => a.Target)));
        Assert.AreEqual(("hero", true), (system.Table[0]!.Source!.Id, (system.Table[0]!.Flags & SpellRunFlags.Reflected) != 0),
            "the source is restored after each turned target (RP6); the run keeps the reflected mark");
    }

    [TestMethod]
    public void Mg34AResistedReflectionLeavesTheSourceSwappedInTheOriginalAndRestoresItInSteamwoven()
    {
        string Later(MagicAnomalyProfile profile)
        {
            var host = new Host { Targets = [new(Orc, new(5, 5)), new(Wand, new(6, 6))] };
            host.Flags["orc"] = SpellTargetStage.FullReflection;
            host.Resisting.Add("hero"); // the caster the orc turned the spell on resists it
            host.Records[7] = Record() with { Duration = new(30) };
            var system = new SpellRunSystem(SpellRunTable.Empty.With(Run(flags: SpellRunFlags.Active)), host, profile);
            system.Begin(0);
            Assert.IsTrue(system.Notes.Any(n => n is { Kind: SpellRunNoteKind.Resisted, Target.Object.Id: "hero" }), "the caster resisted its own spell");
            return string.Join(",", host.Applied.Select(a => a.Target)) + "/" + system.Table[0]!.Source!.Id;
        }
        // The wand is turned too (the run stays reflected): in the original the swapped-in orc is the "caster" it turns on, and the
        // orc stays the run's source; Steamwoven restored the hero first.
        Assert.AreEqual("orc/orc", Later(MagicAnomalyProfile.Original));
        Assert.AreEqual("hero/hero", Later(MagicAnomalyProfile.Steamwoven));
    }

    [TestMethod]
    public void TheResistanceWritesTheRunsMarginAndSkipsAResistingTarget()
    {
        var host = new Host { Targets = [new(Wand, new(6, 6)), new(Orc, new(5, 5))] };
        host.Resisting.Add("orc");
        host.Records[7] = Record() with { Duration = new(30) };
        var system = System(host, Run());
        system.Begin(0);
        Assert.AreEqual("wand", string.Join(",", host.Applied.Select(a => a.Target)), "the orc resisted; the wand took the spell, so the run lives on");
        Assert.IsTrue(system.Notes.Any(n => n is { Kind: SpellRunNoteKind.Resistance, Value: 99 }) && system.Notes.Any(n => n.Kind == SpellRunNoteKind.Resisted));
        var expected = SpellResistanceRules.Resist(host.ResistRecord(7), new SpellResistTarget(false, SpellTargetKind.Npc, false, 100, 0, 0, false), new(true, false, false), host.Rolls,
            SpellFateSave.None, MagicAnomalyProfile.Steamwoven).Margin;
        Assert.IsNotNull(expected);
        Assert.AreEqual(expected.Value, system.Table[0]!.Margin, "the last target's margin is the run's");
    }

    [TestMethod]
    public void TheReflectionsCasterPaysTheLastCostInTheOriginalAndTheRunsOwnStageCostInSteamwovenMg15()
    {
        var mage = new SpellRunObject("mage", SpellObjectFacts.NpcType, 0);
        var host = new Host();
        host.Flags["orc"] = SpellTargetStage.FullReflection;
        host.Records[7] = Record() with { Cost = 3, Duration = new(30) };
        // The mage's run set the reflection on the orc: the mage is the reflection's caster (0x459040).
        var shield = new SpellRun(1, 8, SpellRunAction.Maintain, mage, mage, SpellLocation.None, Orc, SpellLocation.None, null, SpellRunFlags.Active)
            { SetSpellFlags = SpellTargetStage.FullReflection };
        (int? Stage, int? Reflection, string? Payer, int Last) Cast(MagicAnomalyProfile profile, SpellRunFlags flags)
        {
            var system = new SpellRunSystem(SpellRunTable.Empty.With(Run(flags: flags)).With(shield), host, profile, lastCostCharged: 13);
            system.Begin(0);
            var reflection = system.Notes.Single(n => n.Kind == SpellRunNoteKind.ReflectionPayment);
            return (system.Notes.FirstOrDefault(n => n.Kind == SpellRunNoteKind.Payments)?.Payments![0].LastCost, reflection.Payments!.Single().LastCost,
                reflection.Target!.Object!.Id, system.LastCostCharged);
        }
        var paid = Cast(MagicAnomalyProfile.Original, SpellRunFlags.Active);
        Assert.IsNotNull(paid.Stage);
        Assert.AreEqual((paid.Stage, "mage", paid.Stage!.Value), (paid.Reflection, paid.Payer, paid.Last), "the stage's BEGIN cost is the last cost the mage pays");
        Assert.AreEqual((paid.Stage, paid.Stage, "mage"), Cast(MagicAnomalyProfile.Steamwoven, SpellRunFlags.Active) is var own ? (own.Stage, own.Reflection, own.Payer) : default);
        Assert.AreEqual(((int?)null, (int?)13, "mage", 13), Cast(MagicAnomalyProfile.Original, SpellRunFlags.Active | SpellRunFlags.Free),
            "a free cast: the original charges the last cost of an earlier payment");
        Assert.AreEqual(((int?)null, (int?)null, "mage", 13), Cast(MagicAnomalyProfile.Steamwoven, SpellRunFlags.Active | SpellRunFlags.Free),
            "a free cast paid nothing, so its reflection costs nothing");
    }
}
