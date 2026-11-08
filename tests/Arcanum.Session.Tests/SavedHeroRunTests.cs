using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-290 (MIG14 S9a): the hero's walk, turn and states in a save; its other runs wait for their slices. Cells and entities are invented.</summary>
[TestClass]
public sealed class SavedHeroRunTests
{
    private const string Pc = "pc";

    private sealed class Ports : IGoalStartPorts
    {
        public bool Loading => false;
        public bool GatePasses(GoalStartRequest request) => true;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalHandle Started(GameSessionOwner owner, GoalFrame frame, GoalStartPlan? plan = null)
    {
        var result = owner.StartGoal(new GoalStartRequest(Pc, plan ?? GoalStarters.GenericStartsAnyway, frame), new Ports());
        Assert.IsTrue(result.Started, result.Outcome?.ToString());
        return result.Handle!.Value;
    }

    // A walk in its step: goal 3 with the step goal 5 above it, two steps of three given.
    private static GoalFrame[] MidStep(CellCoord destination) =>
    [
        new(GoalType.MoveToTile, tile: destination) { Node = 6, Path = [new(101, 100), new(102, 100), new(103, 100)] },
        new(GoalType.AttemptMove, tile: destination) { Path = [new(101, 100), new(102, 100), new(103, 100)], PathIndex = 2 },
    ];

    [TestMethod]
    public void The_heros_walk_is_saved_with_its_replan_flag_and_a_second_save_of_the_loaded_walk_is_the_same_text()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var destination = new CellCoord(103, 100);
        var frames = MidStep(destination);
        var handle = Started(owner, frames[0], GoalStarters.Walk);
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle, [.. frames]));
        var live = owner.QueryGoals()[handle.Slot]!;
        Assert.IsFalse(live.PathFlags.HasFlag(GoalPathFlags.Replan), "the live walk plans nothing again");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var written = owner.Capture().Goals.Runs.Single(r => r.Owner == Pc);
        Assert.IsTrue(written.PathFlags.HasFlag(GoalPathFlags.Replan), "the written walk plans again from the saved cell (the step under way is not saved)");
        Assert.IsFalse(owner.QueryGoals()[handle.Slot]!.PathFlags.HasFlag(GoalPathFlags.Replan), "the capture changes nothing of the live run");
        using var restored = Restore(Decode(text));
        var back = restored.QueryGoals()[handle.Slot]!;
        Assert.AreEqual(handle, back.Handle);
        Assert.AreEqual(destination, back.Bottom.Tile);
        Assert.IsTrue(back.PathFlags.HasFlag(GoalPathFlags.Replan));
        Assert.AreEqual(Uncounted(text), Uncounted(GameSessionSnapshotCodec.Encode(restored.Capture())));
        Assert.AreEqual(1, restored.QueryEvents().Count(e => e.Kind == SessionEventKinds.GoalStep), "its step is queued");
    }

    [TestMethod]
    public void A_restore_refuses_a_heros_run_of_a_kind_not_saved_yet_and_a_second_walk()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Started(owner, new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(103, 100)), GoalStarters.Walk);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Refused(string edited, string why)
        {
            Assert.AreNotEqual(text, edited, "the edit applies: " + why);
            var decoded = GameSessionSnapshotCodec.TryDecode(edited);
            Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure, why);
        }
        var start = text.IndexOf("\"goals\":{\"nextRunId\":", StringComparison.Ordinal);
        var runs = text.IndexOf("\"runs\":[", start, StringComparison.Ordinal) + "\"runs\":[".Length;
        var end = text.IndexOf("]}", text.IndexOf("\"frames\":[", runs, StringComparison.Ordinal), StringComparison.Ordinal) + 2;
        var run = text[runs..end];
        StringAssert.StartsWith(run, "{\"slot\":");
        Refused(text[..runs] + run.Replace("\"goal\":3,", "\"goal\":21,") + text[end..], "a hero's talk");
        var slot = System.Text.RegularExpressions.Regex.Match(run, "^\\{\"slot\":(\\d+),\"id\":(\\d+),").Groups;
        var second = run.Replace($"{{\"slot\":{slot[1].Value},\"id\":{slot[2].Value},", $"{{\"slot\":{int.Parse(slot[1].Value) + 1},\"id\":{int.Parse(slot[2].Value) + 1},");
        var two = text[..end] + "," + second + text[end..];
        two = System.Text.RegularExpressions.Regex.Replace(two, "\"goals\":\\{\"nextRunId\":\\d+,", "\"goals\":{\"nextRunId\":100,");
        Refused(two, "two walks of the hero");
        // The same text with the second run an NPC's restores: the refusal is the hero's own rule.
        var npc = System.Text.RegularExpressions.Regex.Replace(text[..end] + "," + second.Replace("\"owner\":\"pc\"", "\"owner\":\"npc:a\"") + text[end..],
            "\"goals\":\\{\"nextRunId\":\\d+,", "\"goals\":{\"nextRunId\":100,");
        Assert.IsNotNull(GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(npc).Snapshot!, RestoreOptions()).Owner, "an NPC's walk next to the hero's restores");
    }

    [TestMethod]
    public void The_heros_turn_and_states_are_saved_and_its_runs_that_need_viewer_holders_are_not()
    {
        foreach (var (frame, saved) in new (GoalFrame, bool)[]
        {
            (new GoalFrame(GoalType.Rotate) { Rotation = 3 }, true),
            (new GoalFrame(GoalType.AnimateStunned), true),
            (new GoalFrame(GoalType.KnockDown), true),
            (new GoalFrame(GoalType.GetUp), true),
            (new GoalFrame(GoalType.Dying), true),
            (new GoalFrame(GoalType.Talk, "npc:a"), false),
            (new GoalFrame(GoalType.UseObject, "npc:a"), false),
            (new GoalFrame(GoalType.Attack, "npc:a"), false),
            (new GoalFrame(GoalType.ThrowItem, "npc:a") { Item = "item:rock" }, false),
            (new GoalFrame(GoalType.CastSpell) { SpellRun = 3 }, false),
            (new GoalFrame(GoalType.UseSkill, "npc:a"), false),
            // A walk an approach or an attack pushed names its target: it is the approach's, not the hero's walk alone.
            (new GoalFrame(GoalType.MoveToTile, "npc:a", new CellCoord(5, 5)), false),
        })
        {
            using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
            Started(owner, frame);
            var runs = owner.Capture().Goals.Runs;
            Assert.AreEqual(saved ? 1 : 0, runs.Count, frame.Goal.ToString());
            if (!saved) continue;
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(frame.Goal, restored.QueryGoals().RunsOf(Pc).Single().Bottom.Goal);
        }
    }
}
