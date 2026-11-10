using System.Collections.Immutable;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-208 (MIG14 S2, docs/architecture/saved-runs.md): the goal runs of the active map are part of the snapshot (schema 72, the <c>goals</c> section). A load
/// re-creates the one <c>goal.step</c> a run waits for at its saved due time; the hero's runs, the flights of missiles and projectiles and the runs that name a spell
/// run are not written until their slices. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class SavedGoalRunTests
{
    private const string A = "npc:a";
    private const string B = "npc:b";

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool Loading => false;
        public bool GatePasses(GoalStartRequest request) => true;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalHandle Started(GameSessionOwner owner, string entity, GoalFrame frame, GoalStartPlan? plan = null)
    {
        var result = owner.StartGoal(new GoalStartRequest(entity, plan ?? GoalStarters.GenericStartsAnyway, frame), new Ports());
        Assert.IsTrue(result.Started, result.Outcome?.ToString());
        return result.Handle!.Value;
    }

    private static GameEvent[] Steps(GameSessionOwner owner) => owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.GoalStep).ToArray();

    private static GoalFrame Walking() => new(GoalType.MoveToTile, tile: new CellCoord(110, 205), range: 1)
    {
        Node = 5,
        Path = [new(101, 200), new(102, 201), new(103, 202)],
        PathIndex = 1,
        PlannedTargetTile = new CellCoord(110, 205),
        Flags = GoalFrameFlags.Playing,
        RestMs = 40,
    };

    private static GameSessionOwner Reload(GameSessionOwner owner) => Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));

    [TestMethod]
    public void A_run_with_its_goal_stack_survives_a_save_and_a_load_and_its_step_returns_at_the_saved_due_time()
    {
        using var owner = Create();
        var handle = Started(owner, A, new GoalFrame(GoalType.Attack, B, range: 1));
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle,
            [new GoalFrame(GoalType.Attack, B, range: 1) { Node = 3, Item = "item:sword", Skill = 2 }, Walking()]));
        Assert.AreEqual(SessionGoalFailure.None, owner.TurnOnRunFlags(handle, GoalRunFlags.Running));
        var saved = owner.QueryGoals()[handle.Slot]!;

        using var restored = Reload(owner);
        var run = restored.QueryGoals()[handle.Slot]!;
        Assert.AreEqual(saved.Handle, run.Handle);
        Assert.AreEqual(saved.Owner, run.Owner);
        Assert.AreEqual(saved.Flags, run.Flags);
        Assert.AreEqual(saved.DueMs, run.DueMs);
        Assert.AreEqual(saved.PaceMs, run.PaceMs);
        Assert.AreEqual(saved.Depth, run.Depth);
        for (var i = 0; i < saved.Depth; i++)
        {
            var (a, b) = (saved.Frames[i], run.Frames[i]);
            Assert.AreEqual(a with { Path = [] }, b with { Path = [] }, $"frame {i}");
            CollectionAssert.AreEqual(a.Path.ToArray(), b.Path.ToArray(), $"frame {i} path");
        }
        Assert.AreEqual(owner.QueryGoals().NextRunId, restored.QueryGoals().NextRunId);
        var step = Steps(restored).Single();
        Assert.AreEqual(saved.DueMs, step.DeadlineMs, "the step is back at the run's due time");
        Assert.AreEqual((Exterior.RuntimeMap, A), (step.Payload.Entities[0].Map, step.Payload.Entities[0].Code));
        CollectionAssert.AreEqual(new long[] { handle.Slot, handle.RunId }, step.Payload.Values.Take(2).ToArray());
    }

    [TestMethod]
    public void A_restored_step_fires_in_the_pass_at_its_due_time_and_not_before()
    {
        using var owner = Create();
        var handle = Started(owner, A, Walking());
        using var restored = Reload(owner);
        var due = restored.QueryGoalStep(handle)!.Value;
        var now = restored.QueryFrameClocks().AnimationMs;
        Assert.AreEqual(GameSessionOwner.FirstGoalStepDelayMs, due - now);
        Assert.AreEqual(0, restored.ProcessEvents(new NoWorld()).GoalSteps.Count, "nothing is due yet");
        Assert.AreEqual(ClockMutationDisposition.Changed, restored.JumpClocks(restored.QueryClockSnapshot().Token, due - now).Disposition);
        var pass = restored.ProcessEvents(new NoWorld());
        Assert.AreEqual(handle, pass.GoalSteps.Single().Handle);
    }

    [TestMethod]
    public void A_due_time_already_past_fires_in_the_next_pass_not_never()
    {
        using var owner = Create();
        var handle = Started(owner, A, Walking());
        var due = owner.QueryGoalStep(handle)!.Value;
        // The clocks moved on without a pass (a jump), so the saved due time is behind the saved animation clock.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 50).Disposition);
        Assert.IsTrue(owner.QueryFrameClocks().AnimationMs > due);
        using var restored = Reload(owner);
        var step = Steps(restored).Single();
        Assert.AreEqual(restored.QueryFrameClocks().AnimationMs + 1, step.DeadlineMs, "at least 1 ms ahead");
        Assert.AreEqual(step.DeadlineMs, restored.QueryGoals()[handle.Slot]!.DueMs, "the run records the new due time");
        Assert.AreEqual(ClockMutationDisposition.Changed, restored.JumpClocks(restored.QueryClockSnapshot().Token, 1).Disposition);
        Assert.AreEqual(1, restored.ProcessEvents(new NoWorld()).GoalSteps.Count);
    }

    [TestMethod]
    public void Runs_keep_their_slots_and_a_gap_between_them_stays_a_gap()
    {
        using var owner = Create();
        var first = Started(owner, A, Walking());
        var second = Started(owner, B, Walking());
        var third = Started(owner, "npc:c", Walking());
        owner.KillGoals(B);
        using var restored = Reload(owner);
        var table = restored.QueryGoals();
        Assert.AreEqual(2, table.InUseCount);
        Assert.IsNotNull(table[first.Slot]);
        Assert.IsNull(table[second.Slot]);
        Assert.IsNotNull(table[third.Slot]);
        Assert.AreEqual(2, Steps(restored).Length);
        Assert.IsFalse(table.ExhaustionPending);
        var next = Started(restored, "npc:d", Walking());
        Assert.AreEqual(second.Slot, next.Slot, "the lowest free slot, as before the save");
        Assert.AreEqual(owner.QueryGoals().NextRunId, next.RunId, "the run ids go on from the saved counter");
    }

    [TestMethod]
    public void The_kinds_the_build_does_not_save_yet_are_left_out_of_the_text()
    {
        using var owner = Create();
        var kept = Started(owner, A, Walking());
        Started(owner, "pc", Walking()); // ARC-DEV-290: the hero's walk is saved only with a hero (this session has none)
        Started(owner, "missile:1", new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(1, 1)));
        Started(owner, "projectile:2", new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(1, 1)));
        Started(owner, "npc:cast", new GoalFrame(GoalType.MoveToTile) { SpellRun = 3 });
        Started(owner, "npc:glow", new GoalFrame(GoalType.EyeCandy) { EyeCandy = new GoalEyeCandy(5, 1, Tag: 4) }, GoalStarters.NoInterrupt);
        Started(owner, "npc:plain", new GoalFrame(GoalType.EyeCandy) { EyeCandy = new GoalEyeCandy(5, 1, Tag: -1) }, GoalStarters.NoInterrupt);
        Started(owner, "npc:hover", new GoalFrame(GoalType.Floating), GoalStarters.NoInterrupt);
        Started(owner, "npc:pushed", new GoalFrame(GoalType.Knockback), GoalStarters.NoInterrupt);
        Started(owner, "npc:shot", new GoalFrame(GoalType.ShootSpell), GoalStarters.NoInterrupt);
        Started(owner, "npc:thrown", new GoalFrame(GoalType.Projectile), GoalStarters.NoInterrupt);
        var saved = owner.Capture().Goals;
        // ARC-DEV-289: a frame may name a spell run; one whose slot is empty (the run freed meanwhile) is saved as it is, since its goal's end then finds nothing, as live.
        CollectionAssert.AreEquivalent(new[] { A, "npc:cast" }, saved.Runs.Select(r => r.Owner).ToArray(),
            "the hero's with no hero, the flights without their objects, the overlays' and the knockbacks' runs have no host a load rebuilds yet");
        using var restored = Reload(owner);
        Assert.AreEqual(2, restored.QueryGoals().InUseCount);
        Assert.IsNotNull(restored.QueryGoals()[kept.Slot]);
        Assert.AreEqual(2, Steps(restored).Length, "no step for a run that was not saved");
    }

    [TestMethod]
    public void A_done_run_and_the_run_being_stepped_are_not_saved()
    {
        using var owner = Create();
        var done = Started(owner, A, Walking());
        var stepped = Started(owner, B, Walking());
        Assert.IsTrue(owner.KillGoal(stepped).Succeeded);
        var open = owner.BeginGoalStep(done);
        Assert.IsNotNull(open);
        Assert.AreEqual(0, owner.Capture().Goals.Runs.Count, "the run in its step is not written (a save is taken between steps)");
        owner.EndGoalStep(done, SessionGoalStepAnswer.End);
    }

    [TestMethod]
    public void A_snapshot_without_runs_loads_with_an_empty_table_and_a_fresh_counter()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, NoGoals + NoSpells + NoNewspaper + NoFlights);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(0, restored.QueryGoals().InUseCount);
        Assert.AreEqual(1, restored.QueryGoals().NextRunId);
        Assert.AreEqual(0, Steps(restored).Length);
    }

    [TestMethod]
    public void A_schema_71_save_is_upgraded_with_an_empty_goals_section_and_refuses_one_already_there()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema71(current);
        StringAssert.DoesNotMatch(older, new System.Text.RegularExpressions.Regex("\"goals\""));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot!));
        var twice = GameSessionSnapshotCodec.TryDecode(current.Replace(NoHeroKeys, "").Replace(NoTownMapCells, "").Replace("\"schemaVersion\":85,\"world\"", "\"schemaVersion\":71,\"world\""));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, twice.Failure);
        Assert.AreEqual("$.goals", twice.Path);
    }

    private static string WithRun(string run, int next = 5) => GoldenJson.Replace(NoGoals, $",\"goals\":{{\"nextRunId\":{next},\"runs\":[{run}]}}");

    private const string Frame = "{\"goal\":3,\"node\":1,\"target\":null,\"item\":null,\"tile\":[110,205],\"range\":0,\"sound\":null,\"skill\":null,\"spellRun\":null,\"path\":[[1,1],[2,2]]," +
        "\"pathIndex\":1,\"plannedTarget\":null,\"restMs\":0,\"flags\":0,\"animation\":0,\"frameIndex\":0,\"count\":0,\"pathPending\":false,\"near\":false,\"eyeCandy\":null," +
        "\"fireRound\":0,\"rotation\":0}";

    private static string Run(int slot = 4, string frames = Frame, string owner = "npc:a", string extra = "") =>
        $"{{\"slot\":{slot},\"id\":3,\"owner\":\"{owner}\",\"flags\":0,\"pathFlags\":0,\"dueMs\":{Start.Map.RuntimeMap * 0 + 54_000_050},\"paceMs\":100,\"frames\":[{frames}]{extra}}}";

    [TestMethod]
    public void A_hand_written_run_decodes_and_restores()
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(WithRun(Run()));
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        using var restored = Restore(decoded.Snapshot!);
        Assert.AreEqual(GoalType.MoveToTile, restored.QueryGoals()[4]!.Top.Goal);
        Assert.AreEqual(new GoalHandle(4, 3), restored.QueryGoals()[4]!.Handle);
    }

    [TestMethod]
    public void A_damaged_goals_section_is_refused_with_a_typed_failure_and_a_path()
    {
        void Fails(string text, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text);
            Assert.AreEqual(failure, result.Failure, text[..Math.Min(80, text.Length)]);
            Assert.AreEqual(path, result.Path);
        }
        Fails(WithRun(Run(slot: 216)), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].slot");
        Fails(WithRun(Run(frames: Frame.Replace("\"goal\":3", "\"goal\":99999"))), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].frames[0].goal");
        Fails(WithRun(Run(frames: "")), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].frames");
        Fails(WithRun(Run(frames: string.Join(",", Enumerable.Repeat(Frame, 9)))), SnapshotDecodeFailure.BudgetExceeded, "$.goals.runs[0].frames");
        Fails(WithRun(Run(frames: Frame.Replace("\"rotation\":0", "\"rotation\":8"))), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].frames[0].rotation");
        Fails(WithRun(Run(frames: Frame.Replace("\"target\":null", "\"target\":\"\""))), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].frames[0].target");
        Fails(WithRun(Run(frames: Frame.Replace("\"target\":null", "\"target\":\"a b\""))), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].frames[0].target");
        Fails(WithRun(Run(owner: "")), SnapshotDecodeFailure.OutOfRange, "$.goals.runs[0].owner");
        Fails(WithRun(Run(extra: ",\"extra\":1")), SnapshotDecodeFailure.UnknownField, "$.goals.runs[0].extra");
        Fails(WithRun(Run(frames: Frame.Replace("\"near\":false,", ""))), SnapshotDecodeFailure.MissingField, "$.goals.runs[0].frames[0].near");
        Fails(WithRun(Run() + "," + Run()), SnapshotDecodeFailure.NotCanonical, "$.goals.runs[1].slot");
        Fails(WithRun(Run(slot: 5) + "," + Run(slot: 4)), SnapshotDecodeFailure.NotCanonical, "$.goals.runs[1].slot");
        Fails(WithRun(Run(), next: 0), SnapshotDecodeFailure.OutOfRange, "$.goals.nextRunId");
    }

    [TestMethod]
    public void Flag_bits_the_build_does_not_define_for_a_saved_run_and_cells_off_the_map_are_refused()
    {
        void Fails(string run, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(WithRun(run));
            Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, result.Failure, path);
            Assert.AreEqual(path, result.Path);
        }
        Fails(Run().Replace("\"flags\":0,\"pathFlags\"", "\"flags\":32768,\"pathFlags\""), "$.goals.runs[0].flags");
        Fails(Run().Replace("\"pathFlags\":0", "\"pathFlags\":8"), "$.goals.runs[0].pathFlags");
        Fails(Run(frames: Frame.Replace("\"flags\":0", "\"flags\":65536")), "$.goals.runs[0].frames[0].flags");
        Fails(Run(frames: Frame.Replace("[[1,1],[2,2]]", "[[1,1],[-1,2]]")), "$.goals.runs[0].frames[0].path[1][0]");
        Fails(Run(frames: Frame.Replace("\"tile\":[110,205]", "\"tile\":[110,999999]")), "$.goals.runs[0].frames[0].tile[1]");
    }

    [TestMethod]
    public void A_due_time_further_off_than_a_step_can_wait_is_refused_on_restore()
    {
        var animation = 54_000_000L;
        var near = GameSessionSnapshotCodec.TryDecode(WithRun(Run().Replace("54000050", (animation + GameSessionOwner.MaxGoalStepDelayMs * GameSessionOwner.GoalStepClockRate).ToString())));
        Assert.AreEqual(SnapshotDecodeFailure.None, near.Failure, near.Path);
        Assert.IsNotNull(GameSessionOwner.Restore(near.Snapshot!, RestoreOptions()).Owner, "the longest wait a step may ask for is accepted");
        var far = GameSessionSnapshotCodec.TryDecode(WithRun(Run().Replace("54000050", (animation + GameSessionOwner.MaxGoalStepDelayMs * GameSessionOwner.GoalStepClockRate + 1).ToString())));
        Assert.AreEqual(SnapshotDecodeFailure.None, far.Failure);
        var result = GameSessionOwner.Restore(far.Snapshot!, RestoreOptions());
        Assert.IsNull(result.Owner);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, result.Failure);
    }

    [TestMethod]
    public void A_restore_refuses_a_run_that_cannot_be_in_a_save()
    {
        void Refused(string run, GameSessionFailure failure)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(WithRun(run));
            Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
            var result = GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions());
            Assert.IsNull(result.Owner);
            Assert.AreEqual(failure, result.Failure);
        }
        Refused(Run(owner: "pc"), GameSessionFailure.InvalidSnapshot); // ARC-DEV-290: a hero's run in a save without a hero
        Refused(Run(owner: "missile:7"), GameSessionFailure.InvalidSnapshot);
        Refused(Run(frames: Frame.Replace("\"goal\":3", "\"goal\":43")), GameSessionFailure.InvalidSnapshot);
        // ARC-DEV-289: a frame may name a spell run, but only a slot of the table.
        Refused(Run(frames: Frame.Replace("\"spellRun\":null", "\"spellRun\":512")), GameSessionFailure.InvalidSnapshot);
        Refused(Run(frames: Frame.Replace("\"pathIndex\":1", "\"pathIndex\":3")), GameSessionFailure.InvalidSnapshot);
    }
}
