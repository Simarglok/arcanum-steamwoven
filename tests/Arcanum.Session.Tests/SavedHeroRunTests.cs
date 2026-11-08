using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-290 (MIG14 S9a): the hero's walk, turn and states in a save; ARC-DEV-317 (S9b): its attack and throw; its other runs wait for their slices. Cells and entities are invented.</summary>
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
    public void An_attack_with_its_approach_walk_is_saved_with_the_walks_replan_flag_and_one_whose_walk_names_another_target_is_not()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var handle = Started(owner, new GoalFrame(GoalType.Attack, "atk:npc:a"));
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle,
            [new GoalFrame(GoalType.Attack, "atk:npc:a") { Node = 7 }, new GoalFrame(GoalType.MoveToTile, "atk:npc:a", new CellCoord(104, 100)) { Node = 6, Path = [new(101, 100)] }]));
        var written = owner.Capture().Goals.Runs.Single();
        Assert.IsTrue(written.PathFlags.HasFlag(GoalPathFlags.Replan), "the approach walk plans again after the load");
        using (var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture()))))
            CollectionAssert.AreEqual(new[] { GoalType.Attack, GoalType.MoveToTile }, restored.QueryGoals()[handle.Slot]!.Frames.Select(f => f.Goal).ToArray());
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle,
            [new GoalFrame(GoalType.Attack, "atk:npc:a") { Node = 7 }, new GoalFrame(GoalType.MoveToTile, "atk:npc:b", new CellCoord(104, 100))]));
        Assert.AreEqual(0, owner.Capture().Goals.Runs.Count, "a walk toward another target is no attack's approach");
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
        // ARC-DEV-317: the same for the attack (goals 18 and 19 are one kind): one restores, two are refused.
        string Attack(string t) => t.Replace("\"goal\":3,", "\"goal\":18,").Replace("\"target\":null", "\"target\":\"atk:npc:a\"");
        var one = text[..runs] + Attack(run) + text[end..];
        Assert.IsNotNull(GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(one).Snapshot!, RestoreOptions()).Owner, "one attack of the hero restores");
        Refused(System.Text.RegularExpressions.Regex.Replace(text[..end] + "," + Attack(second) + text[end..], "\"goals\":\\{\"nextRunId\":\\d+,", "\"goals\":{\"nextRunId\":100,")
            .Replace(run, Attack(run)), "two attacks of the hero");
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
            // ARC-DEV-317 (S9b): the attack and the throw are saved; a throw that names a target, or an attack whose walk names another, is not a frame either starter makes.
            (new GoalFrame(GoalType.Attack, "atk:npc:a"), true),
            (new GoalFrame(GoalType.AttemptAttack, "atk:npc:a") { Node = 2, RestMs = 300 }, true),
            (new GoalFrame(GoalType.ThrowItem, tile: new CellCoord(5, 5)) { Item = "item:rock" }, true),
            (new GoalFrame(GoalType.ThrowItem, "npc:a", new CellCoord(5, 5)) { Item = "item:rock" }, false),
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
