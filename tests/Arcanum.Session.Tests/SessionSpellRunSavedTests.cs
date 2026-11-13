using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-210 (MIG14 S4, schema 73): the settled spell runs, the maintain bar, the last cost and the runs' magic.run events in a save.
public sealed partial class SessionSpellRunTests
{
    private static GameSessionOwner WithMaintainedRun(out int run, out Spells world)
    {
        var owner = Create();
        world = new Spells(Timed);
        run = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, run)).Disposition);
        return owner;
    }

    [TestMethod]
    public void A_timed_run_with_a_trigger_count_is_saved_after_its_begin_and_ticks_on_after_the_load()
    {
        // A record with DurationTriggerCount stays in the Begin action (BEGIN processed) until its first tick turns it to Maintain: the save must keep it.
        var counted = Timed with { TriggerCount = 2 };
        using var owner = Create();
        var world = new Spells(counted);
        var run = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), counted).Creation!.Run!.Id;
        Assert.IsTrue(GameSessionOwner.SpellRunIsSaved(owner.QuerySpellRuns()[run]!) == false, "before its BEGIN the run is cut");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, run)).Disposition);
        var begun = owner.QuerySpellRuns()[run]!;
        Assert.AreEqual(SpellRunAction.Begin, begun.Action, "the first tick has not fired yet");
        Assert.AreNotEqual(0, (int)(begun.Flags & SpellRunFlags.Processed));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(begun, restored.QuerySpellRuns()[run]);
        Assert.AreEqual(1, restored.QueryEvents().Count(e => e.Kind == SessionEventKinds.SpellRun), "its tick is saved with it");
        Advance(restored, 80_000);
        Assert.IsTrue(restored.ProcessEvents(world).SpellRuns.Any(f => f.Ran), "the restored tick fires");
    }

    [TestMethod]
    public void A_maintained_run_its_tick_and_the_bar_survive_a_save_and_a_load()
    {
        using var owner = WithMaintainedRun(out var run, out _);
        Assert.AreEqual(SpellRunAction.End, owner.QuerySpellRuns()[run]!.Action, "a timed run with no trigger count waits for its END");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QuerySpellRuns()[run], restored.QuerySpellRuns()[run], "every field of the run, the objects it names and its locations");
        Assert.IsTrue(restored.QueryMaintainBar().SameAs(owner.QueryMaintainBar()));
        CollectionAssert.AreEqual(owner.QueryEvents().Select(e => (e.Kind, e.DeadlineMs)).ToArray(), restored.QueryEvents().Select(e => (e.Kind, e.DeadlineMs)).ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "a second save of the loaded session is the same text");
    }

    // ARC-MAGIC-1234 (schema 92): a run's object list (the original's +0x130) is part of the save: the objects and their order survive, and a run with no list writes an empty one.
    [TestMethod]
    public void A_runs_object_list_survives_a_save_and_a_load_in_its_order()
    {
        using var owner = Create();
        var world = new Spells(Timed) { Built = [new("oid:b", 16, 7), new("oid:a", 16, 0)] };
        var run = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, run)).Disposition);
        CollectionAssert.AreEqual(new[] { "oid:b", "oid:a" }, owner.QuerySpellRuns()[run]!.ObjectList.Items.Select(o => o.Id).ToArray(), "the BEGIN's collection built the list, newest first");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"objectList\":[{\"id\":\"oid:b\",\"type\":16,\"aptitude\":7},{\"id\":\"oid:a\",\"type\":16,\"aptitude\":0}]");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QuerySpellRuns()[run], restored.QuerySpellRuns()[run], "the list is equal by its objects in order");
        CollectionAssert.AreEqual(new[] { "oid:b", "oid:a" }, restored.QuerySpellRuns()[run]!.ObjectList.Items.Select(o => o.Id).ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // A saved list names objects the way the other run objects do: a damaged entity is refused.
        Assert.AreEqual(SnapshotDecodeFailure.WrongType, GameSessionSnapshotCodec.TryDecode(text.Replace("{\"id\":\"oid:a\",\"type\":16,\"aptitude\":0}", "null")).Failure);
    }

    [TestMethod]
    public void A_schema_91_save_gets_an_empty_object_list_on_each_run()
    {
        using var owner = WithMaintainedRun(out var run, out _);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(current, "\"setSpellFlags\":0,\"objectList\":[]}");
        var older = SnapshotUpgradeTests.Schema91(current);
        Assert.IsFalse(older.Contains("objectList"), "the older text has no list");
        StringAssert.Contains(older, "\"schemaVersion\":91,");
        using var restored = Restore(Decode(older));
        Assert.AreEqual(owner.QuerySpellRuns()[run], restored.QuerySpellRuns()[run], "the run is the same, with the empty list");
        Assert.AreEqual(0, restored.QuerySpellRuns()[run]!.ObjectList.Count);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void A_run_before_its_begin_is_saved_with_its_begin_event_and_cut_without_one()
    {
        // A parentless run has its BEGIN as an event 2 ms later: ARC-DEV-289 saves it with that event. A parented one waits for its casting goal: with no saved goal naming
        // it (here none; the hero's casting goal is the viewer's), it is cut.
        using var owner = Create();
        var parentless = Start(owner, new(33, null, Hero, Orc, SpellLocation.None), Instant).Creation!.Run!.Id;
        var parented = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.AreEqual(2, owner.QuerySpellRuns().Count);
        Assert.AreEqual(1, owner.QueryEvents().Count, "the parentless run's BEGIN");
        var captured = owner.Capture();
        CollectionAssert.AreEqual(new[] { parentless }, captured.Spells.Runs.Select(r => r.Id).ToArray());
        Assert.AreEqual(1, captured.Events.Pending.Count(e => e.Kind == SessionEventKinds.SpellRun));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(captured)));
        Assert.AreEqual(owner.QuerySpellRuns()[parentless], restored.QuerySpellRuns()[parentless]);
        Assert.IsNull(restored.QuerySpellRuns()[parented]);
        Advance(restored, 10);
        Assert.IsTrue(restored.ProcessEvents(new Spells(Instant)).SpellRuns.Any(f => f.Ran), "the restored BEGIN event fires");
    }

    [TestMethod]
    public void A_cut_run_takes_its_bar_slot_and_events_with_it_while_a_settled_one_keeps_them()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var maintained = Instant with { MaintainPeriod = 2 };
        var world = new Spells(maintained) { Bar = owner };
        var settled = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), maintained).Creation!.Run!.Id;
        owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, settled));
        var waiting = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), maintained).Creation!.Run!.Id;
        var captured = owner.Capture();
        CollectionAssert.AreEqual(new[] { settled }, captured.Spells.Runs.Select(r => r.Id).ToArray());
        Assert.IsTrue(captured.Spells.Runs.Single(r => r.Id == settled).Action is SpellRunAction.Maintain or SpellRunAction.End);
        Assert.IsTrue(captured.Events.Pending.Where(e => e.Kind == SessionEventKinds.SpellRun).All(e => e.Values[1] == settled), "no event of the cut run");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(captured)));
        Assert.IsNull(restored.QuerySpellRuns()[waiting]);
        Assert.AreEqual(settled, restored.QueryMaintainBar()[0].Run);
    }

    [TestMethod]
    public void A_schema_72_save_is_upgraded_with_an_empty_spells_section_and_refuses_one_already_there()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema72(current);
        StringAssert.DoesNotMatch(older, new System.Text.RegularExpressions.Regex("\"spells\""));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot!));
        var twice = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.WithoutCritterEffects(current).Replace(NoEncounters, "").Replace(NoBarLights, "").Replace(NoGamblingRoll, "").Replace(NoHeroStoredKeys, "").Replace(SnapshotUpgradeTests.NoCreated, "}").Replace(NoHeroKeys, "").Replace(NoTownMapCells, "").Replace("\"schemaVersion\":101,\"world\"", "\"schemaVersion\":72,\"world\""));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.spells"), (twice.Failure, twice.Path));
    }

    [TestMethod]
    public void A_damaged_spells_section_is_refused_with_a_typed_failure_or_a_refused_restore()
    {
        using var owner = WithMaintainedRun(out _, out _);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Decodes(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path), damaged[..Math.Min(60, damaged.Length)]);
        }
        string With(string from, string to)
        {
            StringAssert.Contains(text, from);
            return text.Replace(from, to);
        }
        Decodes(With("\"action\":2,", "\"action\":9,"), SnapshotDecodeFailure.OutOfRange, "$.spells.runs[0].action");
        Decodes(With("\"flags\":65543,", "\"flags\":65671,"), SnapshotDecodeFailure.OutOfRange, "$.spells.runs[0].flags");
        Decodes(With("\"lastCost\":0,", "\"lastCost\":-1,"), SnapshotDecodeFailure.OutOfRange, "$.spells.lastCost");
        Decodes(With(",{\"run\":-1,\"state\":0}],\"lastCost\"", "],\"lastCost\""), SnapshotDecodeFailure.OutOfRange, "$.spells.bar");
        Decodes(With("\"bar\":[{\"run\":-1,\"state\":0}", "\"bar\":[{\"run\":-1,\"state\":7}"), SnapshotDecodeFailure.OutOfRange, "$.spells.bar[0].state");
        Decodes(With("\"source\":{\"id\":\"hero\"", "\"source\":{\"id\":\"\""), SnapshotDecodeFailure.OutOfRange, "$.spells.runs[0].source.id");
        // Shapes the codec accepts that the restore refuses: a run of an action the build does not save, and a bar slot showing a run that is not there.
        var begin = Decode(With("\"action\":2,", "\"action\":3,"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(begin, RestoreOptions()).Failure, "a run waiting for a callback is not a saved kind");
        var strayBar = Decode(With("\"bar\":[{\"run\":-1,\"state\":0}", "\"bar\":[{\"run\":300,\"state\":1}"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(strayBar, RestoreOptions()).Failure, "a showing slot names a run that is not in the table");
    }

    [TestMethod]
    public void A_saved_tick_of_a_run_that_is_not_saved_is_refused()
    {
        using var owner = WithMaintainedRun(out _, out _);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var start = text.IndexOf("\"spells\":{\"runs\":[", StringComparison.Ordinal) + "\"spells\":{\"runs\":[".Length;
        var end = text.IndexOf("],\"bar\"", start, StringComparison.Ordinal);
        var runless = Decode(text[..start] + text[end..]);
        Assert.AreEqual(1, runless.Events.Pending.Count(e => e.Kind == SessionEventKinds.SpellRun), "the event is still there");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(runless, RestoreOptions()).Failure);
    }
}
