using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-010: the session's spell runs and its magic-tech events (not saved until MT9b). Objects, records and the host are
// invented.
[TestClass]
public sealed class SessionSpellRunTests
{
    private static readonly SpellObjectFacts Trap = new("trap", 17, new(1, 1));
    private static readonly SpellObjectFacts Hero = new("hero", 15, new(2, 2), Aptitude: 10, Intelligence: 12);
    private static readonly SpellObjectFacts Orc = new("orc", 16, new(3, 2), Aptitude: 20);
    private static readonly SpellRunRecord Instant = new(Duration: new(0), Invocation: new(false, 10, 0, 0, 0)) { ActionComponents = [1, 0, 0, 0, 0] };
    private static readonly SpellRunRecord Timed = Instant with { Duration = new(10) };

    private sealed class Plain : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Spells(SpellRunRecord record) : ISessionSpellWorld, ISpellRunHost
    {
        public int Applied { get; private set; }
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public ISpellRunHost SpellRuns => this;
        public SpellRunRecord Record(int spell) => record;
        public SpellStageFacts StageFacts(SpellRun run) => new(false, false, 12, 20, false, new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), new(true, 0, -1));
        public ISpellCellSource Cells { get; } = new NoCells();
        public ISpellRunStacking Stacking { get; } = new NoStacking();
        public bool FlagsAllow(SpellRun run) => true;
        public int SourceSpellFlags(SpellRun run) => 0;
        public bool IsLocalPlayer(SpellRunObject? who) => false;
        public IReadOnlyList<SpellRunTarget> Collect(SpellRun run) => [new(run.Target, run.TargetTile)];
        public bool UseSpellAtMaximum(SpellRunObject pc) => false;
        public bool PerTarget(SpellRun run, SpellRunTarget target) => true;
        public bool TakesEffect(SpellRun run, SpellRunTarget target) => true;
        public SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target) => new(true, target);
        public SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target) { Applied++; return SpellComponentOutcome.None; }
        public SpellTimeFacts TimeFacts(SpellRun run) => new(true);
        public int? Stat(SpellRunObject who, int stat) => 12;
        public bool MaintainBarHasSlot(SpellRun run) => true;
        public IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run) => [];
    }

    private sealed class NoCells : ISpellCellSource
    {
        public SpellCellPayment Pay(SpellCellRequest request) => throw new AssertFailedException("no cells");
    }

    private sealed class NoStacking : ISpellRunStacking
    {
        public int? Blocking(SpellRun run, SpellRunObject target) => null;
        public void Cancel(SpellRun run) { }
    }

    private sealed class Check : ISpellInvocationWorld
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

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static SessionSpellRunResult Start(GameSessionOwner owner, SpellInvocation invocation, SpellRunRecord record) =>
        owner.StartSpellRun(owner.Version, new(invocation, Free: true), record, new Check(), new NoRolls());

    [TestMethod]
    public void A_run_without_a_parent_begins_by_its_process_event_and_a_host_without_magic_does_not_run_it()
    {
        using var owner = Create();
        var started = Start(owner, new(33, null, Trap, Orc, SpellLocation.None), Instant);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 1L), (started.Disposition, started.Creation!.BeginEvent, owner.Version.Revision));
        var pending = owner.QueryEvents().Single();
        Assert.AreEqual((SessionEventKinds.SpellRun, 0L, (long)SpellRunEventCode.Process, owner.QueryClockSnapshot().GameTimestampMs + 2),
            (pending.Kind, pending.Payload.Values[1], pending.Payload.Values[2], pending.DeadlineMs));
        Advance(owner, 2);
        var plain = owner.ProcessEvents(new Plain()).SpellRuns.Single();
        Assert.AreEqual((0, SpellRunEventCode.Process, false), (plain.Run, plain.Code, plain.Ran));
        Assert.AreEqual(1, owner.QuerySpellRuns().Count, "not run: the run stays");

        var world = new Spells(Instant);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, 0)).Disposition);
        Assert.AreEqual((1, 0), (world.Applied, owner.QuerySpellRuns().Count), "BEGIN applied, END at once, freed");

        Start(owner, new(33, null, Trap, Orc, SpellLocation.None), Instant);
        Advance(owner, 2);
        var ran = owner.ProcessEvents(world).SpellRuns.Single();
        Assert.AreEqual((true, 0), (ran.Ran, owner.QuerySpellRuns().Count));
        Assert.IsTrue(ran.Notes.Any(n => n.Kind == SpellRunNoteKind.Freed));
    }

    [TestMethod]
    public void A_timed_run_ticks_on_the_queue_and_neither_runs_nor_events_are_saved()
    {
        using var owner = Create();
        var world = new Spells(Timed);
        var started = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed);
        Assert.AreEqual((false, 26), (started.Creation!.BeginEvent, started.Creation.CastGoal));
        Assert.AreEqual(0, owner.QueryEvents().Count, "a parent waits for its casting goal");
        owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, 0));
        var tick = owner.QueryEvents().Single();
        Assert.AreEqual(((long)SpellRunEventCode.Tick, owner.QueryClockSnapshot().GameTimestampMs + 80_000), (tick.Payload.Values[2], tick.DeadlineMs));

        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual((0, 0), (restored.QuerySpellRuns().Count, restored.QueryEvents().Count), "MT9b saves them");

        Advance(owner, 80_000);
        Assert.IsTrue(owner.ProcessEvents(world).SpellRuns.Single().Ran);
        Assert.AreEqual((0, 0), (owner.QuerySpellRuns().Count, owner.QueryEvents().Count), "no trigger count: the tick was the END");
    }

    [TestMethod]
    public void Refusals()
    {
        using var owner = Create();
        var world = new Spells(Instant);
        Assert.AreEqual(GameSessionFailure.InvalidSpellRun, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, 512)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSpellRun, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Demaintain)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSpellRun, owner.ActOnSpellRun(owner.Version, world, new((SessionSpellRunActionKind)99, 0)).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Interrupt, 3)).Disposition, "no such run");
        var stale = owner.Version;
        Start(owner, new(33, null, Trap, Orc, SpellLocation.None), Instant);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ActOnSpellRun(stale, world, new(SessionSpellRunActionKind.Begin, 0)).Failure);
        var refused = Start(owner, new(300, null, Trap, Orc, SpellLocation.None), Instant);
        Assert.AreEqual((GameSessionFailure.SpellRunRefused, SpellRunCreationStatus.Refused), (refused.Failure, refused.Creation!.Status));
    }
}
