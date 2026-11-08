using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-290 (MIG14 S9a): the hero's walk, turn and states in a save; ARC-DEV-317 (S9b): its attack and throw; ARC-DEV-318 (S9c): its cast with the casts queued beneath it; ARC-DEV-319 (S9d): its talk, use, pickup and lock pick with their approaches; ARC-DEV-321 (S9e): its pick pocket. Cells and entities are invented.</summary>
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
        Refused(text[..runs] + run.Replace("\"goal\":3,", "\"goal\":86,") + text[end..], "a hero's pick pocket without its victim and item");
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
    public void A_cast_is_saved_with_the_casts_queued_beneath_it_and_a_queue_that_started_or_names_one_run_twice_is_not()
    {
        GoalFrame Queued(int run) => new(GoalType.CastSpell, "npc:a") { SpellRun = run };
        var playing = new GoalFrame(GoalType.AttemptSpell, "npc:a") { SpellRun = 4, Node = 2, RestMs = 90 };
        foreach (var (frames, saved, why) in new (GoalFrame[], bool, string)[]
        {
            ([Queued(6), Queued(5), playing], true, "two queued casts beneath the playing one"),
            ([Queued(5) with { Node = 2 }, playing], false, "a queued goal that started"),
            ([new GoalFrame(GoalType.AttemptSpell) { SpellRun = 5 }, playing], false, "a goal 27 beneath the top"),
            ([Queued(4), playing], false, "the playing run queued again"),
            ([new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(5, 5)), playing], false, "a walk beneath a cast"),
        })
        {
            using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
            var handle = Started(owner, frames[^1]);
            Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle, [.. frames]), why);
            var runs = owner.Capture().Goals.Runs;
            Assert.AreEqual(saved ? 1 : 0, runs.Count, why);
            if (!saved) continue;
            var text = GameSessionSnapshotCodec.Encode(owner.Capture());
            using var restored = Restore(Decode(text));
            var back = restored.QueryGoals()[handle.Slot]!;
            CollectionAssert.AreEqual(frames.Select(f => (f.Goal, f.Node, f.SpellRun, f.Target)).ToArray(), back.Frames.Select(f => (f.Goal, f.Node, f.SpellRun, f.Target)).ToArray(), why);
            Assert.AreEqual(Uncounted(text), Uncounted(GameSessionSnapshotCodec.Encode(restored.Capture())));
        }
    }

    [TestMethod]
    public void An_action_is_saved_with_its_approach_and_the_walk_the_approach_pushed_and_one_whose_frames_name_another_target_is_not()
    {
        GoalFrame Walk(string? target = null) => new(GoalType.MoveToTile, target, new CellCoord(104, 100)) { Node = 6, Path = [new(101, 100)] };
        var step = new GoalFrame(GoalType.AttemptMove, tile: new CellCoord(104, 100)) { Path = [new(101, 100)] };
        foreach (var (frames, saved, why) in new (GoalFrame[], bool, string)[]
        {
            ([new(GoalType.Talk, "npc:a") { Node = 3 }, new(GoalType.MoveNear, "npc:a") { Range = 1, Node = 2 }, Walk(), step], true, "a talk walking to its speaker"),
            ([new(GoalType.PickupItem, "inv:1:2:3") { Node = 4 }, new(GoalType.MoveNear, "inv:1:2:3") { Node = 2 }, Walk()], true, "a pickup walking to its item"),
            ([new(GoalType.PickupItem, "inv:1:2:3") { Node = 4 }, new(GoalType.AttemptPickup, "inv:1:2:3")], true, "a pickup taking its item"),
            ([new(GoalType.UseSkill, "door:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:picks", Node = 2 }, new(GoalType.MoveNear, "door:a") { Node = 2 }, Walk()], true, "a lock pick walking to its door"),
            ([new(GoalType.UseSkill, "npc:a") { Skill = AiExecutorRules.PickpocketSkill, Item = "item:coin", Flags = GoalFrameFlags.Plant, Node = 2 }, new(GoalType.MoveNear, "npc:a") { Node = 2 }, Walk()], true, "a plant walking to its victim"),
            ([new(GoalType.UseSkill, "npc:a") { Skill = AiExecutorRules.PickpocketSkill, Item = "item:coin", Flags = GoalFrameFlags.Plant }, new(GoalType.AttemptPickpocket, "npc:a") { Skill = AiExecutorRules.PickpocketSkill, Item = "item:coin" }], false, "a steal over a plant"),
            ([new(GoalType.Talk, "npc:a") { Node = 3 }, new(GoalType.MoveNear, "npc:b") { Node = 2 }], false, "an approach to another target"),
            ([new(GoalType.Talk, "npc:a") { Node = 3 }, new(GoalType.MoveNear, "npc:a") { Node = 2 }, Walk("npc:a")], false, "a walk naming the target (an attack's, not an approach's)"),
            ([new(GoalType.UseObject, "obj:1:2:3") { Node = 3 }, new(GoalType.Talk, "obj:1:2:3")], false, "another action's goal on the target"),
            ([new(GoalType.UseSkill, "door:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:picks" }, new(GoalType.AttemptPickLocks, "door:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:other" }], false, "another item"),
        })
        {
            using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
            var handle = Started(owner, frames[0]);
            Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle, [.. frames]), why);
            var runs = owner.Capture().Goals.Runs;
            Assert.AreEqual(saved ? 1 : 0, runs.Count, why);
            if (!saved) continue;
            Assert.AreEqual(frames.Any(f => f.Goal == GoalType.MoveToTile), runs[0].PathFlags.HasFlag(GoalPathFlags.Replan), why + ": an approach's walk plans again after the load");
            var text = GameSessionSnapshotCodec.Encode(owner.Capture());
            using var restored = Restore(Decode(text));
            CollectionAssert.AreEqual(frames.Select(f => (f.Goal, f.Target, f.Item)).ToArray(), restored.QueryGoals()[handle.Slot]!.Frames.Select(f => (f.Goal, f.Target, f.Item)).ToArray(), why);
            Assert.AreEqual(Uncounted(text), Uncounted(GameSessionSnapshotCodec.Encode(restored.Capture())), why);
        }
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
            // ARC-DEV-319 (S9d): the talk, the use (and the door goal it became), the pickup and the lock pick; ARC-DEV-321 (S9e): the pick pocket; an action naming no target, or a skill without its item, is not.
            (new GoalFrame(GoalType.Talk, "npc:a"), true),
            (new GoalFrame(GoalType.UseObject, "obj:1:2:3"), true),
            (new GoalFrame(GoalType.AttemptOpenDoor, "door:a"), true),
            (new GoalFrame(GoalType.PickupItem, "inv:1:2:3"), true),
            (new GoalFrame(GoalType.UseSkill, "door:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:picks" }, true),
            (new GoalFrame(GoalType.AttemptPickLocks, "door:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:picks" }, true),
            (new GoalFrame(GoalType.UseSkill, "npc:a") { Skill = AiExecutorRules.PickpocketSkill, Item = "item:coin" }, true),
            (new GoalFrame(GoalType.AttemptPickpocket, "npc:a") { Skill = AiExecutorRules.PickpocketSkill, Item = "item:coin", Flags = GoalFrameFlags.Plant }, true),
            (new GoalFrame(GoalType.AttemptPickpocket, "npc:a") { Skill = AiExecutorRules.PickLocksSkill, Item = "item:coin" }, false),
            (new GoalFrame(GoalType.UseSkill, "npc:a") { Skill = 34, Item = "item:coin" }, false),
            (new GoalFrame(GoalType.UseSkill, "door:a") { Skill = AiExecutorRules.PickLocksSkill }, false),
            (new GoalFrame(GoalType.Talk), false),
            // ARC-DEV-317 (S9b): the attack and the throw are saved; a throw that names a target, or an attack whose walk names another, is not a frame either starter makes.
            (new GoalFrame(GoalType.Attack, "atk:npc:a"), true),
            (new GoalFrame(GoalType.AttemptAttack, "atk:npc:a") { Node = 2, RestMs = 300 }, true),
            (new GoalFrame(GoalType.ThrowItem, tile: new CellCoord(5, 5)) { Item = "item:rock" }, true),
            (new GoalFrame(GoalType.ThrowItem, "npc:a", new CellCoord(5, 5)) { Item = "item:rock" }, false),
            // ARC-DEV-318 (S9c): the cast (26, or 27 once it replaced it); one without its spell run or with an item is not a frame the cast host makes.
            (new GoalFrame(GoalType.CastSpell) { SpellRun = 3 }, true),
            (new GoalFrame(GoalType.AttemptSpell, "npc:a") { SpellRun = 3, Node = 2, RestMs = 120 }, true),
            (new GoalFrame(GoalType.CastSpell), false),
            (new GoalFrame(GoalType.CastSpell) { SpellRun = 3, Item = "item:scroll" }, false),
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
