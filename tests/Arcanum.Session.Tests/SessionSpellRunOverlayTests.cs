using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1280 (saved-runs.md, the overlay runs; no schema step: the goals section already writes every eye-candy field): the overlay runs (eye candy 45-48, 58, 59, 75, 76 and the hover 44) are part of a save,
// so a maintained Illuminate (a looping eye candy of the hero, a map critter or a created critter, tied to its spell run by the tag) survives a save and a load. Objects, records and the host are invented.
public sealed partial class SessionSpellRunTests
{
    private sealed class OverlayPorts : IGoalStartPorts
    {
        public bool Loading => false;
        public bool GatePasses(GoalStartRequest request) => true;
        public bool IsDestroyed(string owner) => false;
    }

    // A looping eye candy run of the spell run <paramref name="tag"/> (the add 0x4CCF70 as the AreaEyeCandyHost builds it), begun, at frame 3.
    private static GoalHandle Glow(GameSessionOwner owner, string entity, int tag, int entry = 196, GoalType goal = GoalType.EyeCandy)
    {
        var frame = new GoalFrame(goal) { Flags = GoalFrameFlags.NotCounted | GoalFrameFlags.Begun, EyeCandy = new(0x0A000001u, entry, tag, null, Frame: 3, BackgroundFrame: 1) };
        var result = owner.StartGoal(new GoalStartRequest(entity, GoalStarters.NoInterrupt, frame), new OverlayPorts());
        Assert.IsTrue(result.Started, result.Outcome?.ToString());
        return result.Handle!.Value;
    }

    private static GameSessionOwner Reloaded(GameSessionOwner owner) => Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));

    [TestMethod]
    public void A_maintained_runs_eye_candy_on_the_hero_a_map_critter_and_a_created_critter_survives_a_save_and_a_load()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var world = new Spells(Timed);
        var run = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, run)).Disposition);
        string[] owners = ["pc", "npc:a", "oid:critter:3"];
        var handles = owners.Select(entity => Glow(owner, entity, run)).ToArray();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        for (var i = 0; i < owners.Length; i++)
        {
            var (live, back) = (owner.QueryGoals()[handles[i].Slot]!, restored.QueryGoals()[handles[i].Slot]!);
            Assert.AreEqual(live.Handle, back.Handle);
            Assert.AreEqual(owners[i], back.Owner);
            Assert.AreEqual(live.Frames.Single() with { Path = [] }, back.Frames.Single() with { Path = [] }, "the eye candy keeps its art, entry, tag, frames and its begun mark");
            Assert.AreEqual(run, back.Top.EyeCandy!.Tag, "the tag names the spell run, which is saved with it");
            Assert.AreEqual(live.DueMs, back.DueMs);
        }
        Assert.IsNotNull(restored.QuerySpellRuns()[run], "the maintained run itself is restored");
        Assert.AreEqual(owners.Length, restored.QueryEvents().Count(e => e.Kind == SessionEventKinds.GoalStep), "each overlay run waits for its one step");
        Assert.AreEqual(Uncounted(text), Uncounted(GameSessionSnapshotCodec.Encode(restored.Capture())), "a second save of the loaded session is the same text");
    }

    [TestMethod]
    public void A_stack_of_eye_candies_the_hover_and_several_overlays_of_one_hero_are_saved()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var first = Glow(owner, "pc", -1, entry: 5);
        var second = Glow(owner, "pc", -1, entry: 6, goal: GoalType.EyeCandyReverseCallback);
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(first, [owner.QueryGoals()[first.Slot]!.Bottom with { EyeCandy = new(1u, 5, -1, "npc:b", 0, 0) }, owner.QueryGoals()[first.Slot]!.Bottom]));
        var hover = owner.StartGoal(new GoalStartRequest("oid:item:9", GoalStarters.NoInterrupt, AiExecutorRules.HoverFrame()), new OverlayPorts());
        Assert.IsTrue(hover.Started);
        using var restored = Reloaded(owner);
        Assert.AreEqual(2, restored.QueryGoals()[first.Slot]!.Frames.Length, "a queued eye candy keeps its stack");
        Assert.AreEqual("npc:b", restored.QueryGoals()[first.Slot]!.Bottom.EyeCandy!.Source);
        Assert.AreEqual(GoalType.EyeCandyReverseCallback, restored.QueryGoals()[second.Slot]!.Bottom.Goal);
        Assert.AreEqual(GoalType.Floating, restored.QueryGoals()[hover.Handle!.Value.Slot]!.Bottom.Goal);
        Assert.AreEqual(3, restored.QueryGoals().Runs.Count(r => r.Owner == "pc" || r.Owner == "oid:item:9"));
    }

    [TestMethod]
    public void A_tag_of_a_freed_spell_run_is_written_as_none_and_one_of_a_run_that_is_cut_cuts_the_overlay()
    {
        using var owner = Create();
        var world = new Spells(Timed);
        var kept = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, world, new(SessionSpellRunActionKind.Begin, kept)).Disposition);
        // A run that has not begun and that nothing names is cut from the save, so the eye candy tied to it is cut too.
        var cut = Start(owner, new(33, Hero, Hero, Orc, SpellLocation.None), Timed).Creation!.Run!.Id;
        Assert.IsFalse(GameSessionOwner.SpellRunIsSaved(owner.QuerySpellRuns()[cut]!));
        var freed = (kept + cut + 7) % SpellRunTable.Slots;
        Assert.IsNull(owner.QuerySpellRuns()[freed], "an empty slot");
        var withKept = Glow(owner, "npc:a", kept); var withCut = Glow(owner, "npc:b", cut); var withFreed = Glow(owner, "npc:c", freed);
        var saved = owner.Capture().Goals.Runs.ToDictionary(r => r.Owner);
        Assert.AreEqual(kept, saved["npc:a"].Top.EyeCandy!.Tag);
        Assert.IsFalse(saved.ContainsKey("npc:b"), "the eye candy of a spell run that is not saved is not saved");
        Assert.AreEqual(-1, saved["npc:c"].Top.EyeCandy!.Tag, "the spell run is gone: nobody is called back");
        Assert.AreEqual(freed, owner.QueryGoals()[withFreed.Slot]!.Top.EyeCandy!.Tag, "the capture changes nothing of the live run");
        using var restored = Reloaded(owner);
        Assert.IsNull(restored.QueryGoals()[withCut.Slot]);
        Assert.AreEqual(-1, restored.QueryGoals()[withFreed.Slot]!.Top.EyeCandy!.Tag);
        Assert.IsNotNull(restored.QueryGoals()[withKept.Slot]);
    }

    [TestMethod]
    public void An_overlay_run_of_a_hero_in_a_save_without_a_hero_a_flight_owner_or_a_frame_that_names_a_target_is_not_saved_and_not_restored()
    {
        using var owner = Create(); // no hero
        Glow(owner, "pc", -1);
        Glow(owner, "missile:4", -1);
        Glow(owner, "projectile:2", -1);
        Glow(owner, "npc:a", -1);
        CollectionAssert.AreEqual(new[] { "npc:a" }, owner.Capture().Goals.Runs.Select(r => r.Owner).ToArray());
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        string Refuse(string changed, GameSessionFailure failure)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(changed);
            Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
            var result = GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions());
            Assert.IsNull(result.Owner, changed);
            Assert.AreEqual(failure, result.Failure);
            return changed;
        }
        StringAssert.Contains(text, "\"owner\":\"npc:a\"");
        Refuse(text.Replace("\"owner\":\"npc:a\"", "\"owner\":\"pc\""), GameSessionFailure.InvalidSnapshot);
        Refuse(text.Replace("\"owner\":\"npc:a\"", "\"owner\":\"missile:4\""), GameSessionFailure.InvalidSnapshot);
        Refuse(text.Replace("\"target\":null", "\"target\":\"npc:b\""), GameSessionFailure.InvalidSnapshot);
        Refuse(text.Replace("\"tag\":-1", "\"tag\":9"), GameSessionFailure.InvalidSnapshot); // a spell run slot that is empty
        Refuse(text.Replace("\"tag\":-1", "\"tag\":512"), GameSessionFailure.InvalidSnapshot); // not a slot of the table
    }

    [TestMethod]
    public void A_save_without_overlay_runs_loads_as_before_and_a_session_with_none_writes_none()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.Capture().Goals.Runs.Count);
        using var restored = Reloaded(owner);
        Assert.AreEqual(0, restored.QueryGoals().InUseCount);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, restored.Capture().SchemaVersion, "no schema step: a save before this change is read as it is");
    }
}
