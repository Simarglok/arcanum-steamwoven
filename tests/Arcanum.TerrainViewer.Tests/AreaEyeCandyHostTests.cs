using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-143 (MIG13 S9b): the overlay host over a session: eye candy and the hover are runs of the table that the frame step hands to the host, on the animation clock.</summary>
[TestClass]
public sealed class AreaEyeCandyHostTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private const SpellEyeCandyFlags Fore = SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Animates;

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public AreaEyeCandyHost Host { get; }
        public FrameStep Step { get; }
        public EyeCandyFacts? Facts = new(4, 2, 100, 0, true, true);
        public Dictionary<int, SpellEyeCandyEntry> Entries = new()
        {
            [10] = new(0x1000, Fore, -1, null, (0, 0, 0), 0),
            [11] = new(0x1100, Fore | SpellEyeCandyFlags.Forever, -1, null, (0, 0, 0), 0),
            [12] = new(0x1200, SpellEyeCandyFlags.Foreground, -1, null, (0, 0, 0), 0),
            [13] = new(0x1300, SpellEyeCandyFlags.Background | SpellEyeCandyFlags.Animates, -1, null, (0, 0, 0), 0),
            [14] = new(0x1400, SpellEyeCandyFlags.Underlay | SpellEyeCandyFlags.Animates, -1, null, (0, 0, 0), 0),
        };

        public Fixture()
        {
            Host = new AreaEyeCandyHost(Session, i => Entries.GetValueOrDefault(i), (art, parts) => Facts, (low, high) => low);
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, null, () => Host) };
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public int Runs => Session.QueryGoals().InUseCount;
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void An_eye_candy_is_an_overlay_run_that_shows_after_its_begin_and_is_gone_at_frames_times_pace()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 10, 0, 7));
        Assert.AreEqual(GoalType.EyeCandy, f.Session.QueryGoals().RunsOf(Hero).Single().Bottom.Goal);
        Assert.AreEqual(0, f.Host.Shown().Count(), "not begun yet");
        f.Run(40);
        var shown = f.Host.Shown().Single();
        Assert.AreEqual((Hero, 0, 7, 0x1000u), (shown.Owner, shown.Eye.Frame, shown.Eye.Tag, shown.Eye.Art));
        f.Run(200);
        Assert.IsTrue(f.Host.Shown().Single().Eye.Frame is >= 1 and <= 3, "one frame a pace");
        f.Run(400);
        Assert.AreEqual(0, f.Runs, "the goal popped");
        Assert.AreEqual(0, f.Host.Shown().Count());
        Assert.IsTrue(f.Host.Steps >= 5);
    }

    [TestMethod]
    public void An_entry_that_does_not_animate_or_has_no_foreground_or_underlay_plays_nothing()
    {
        using var f = new Fixture();
        Assert.IsFalse(f.Host.Play(Hero, 13, 0, 1), "a background only");
        Assert.IsFalse(f.Host.Play(Hero, 99, 0, 1), "no entry");
        f.Entries[15] = new(0x1500, SpellEyeCandyFlags.Foreground, -1, null, (0, 0, 0), 0);
        Assert.IsFalse(f.Host.Play(Hero, 15, 0, 1), "does not animate");
        Assert.AreEqual(0, f.Runs);
        Assert.IsTrue(f.Host.Play(Hero, 14, 0, 1), "an underlay");
    }

    [TestMethod]
    public void The_goal_follows_the_play_flags_and_a_looping_entry_loops_until_a_remove_ends_its_goal_45()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 11, 0, 5));
        Assert.IsTrue(f.Host.Play(Hero, 10, AiExecutorRules.PlayCallback, 5));
        f.Run(2_000);
        Assert.AreEqual(1, f.Runs, "the callback one ended, the looping one goes on");
        Assert.AreEqual(1, f.Host.Shown().Count());
        Assert.AreEqual(0, f.Host.Remove(Hero, 11, 6, false), "another tag is left");
        Assert.AreEqual(0, f.Host.Remove(Hero, 12, 5, false), "another entry is left");
        Assert.AreEqual(1, f.Host.Remove(Hero, 11, 6, true), "no_id takes any tag");
        Assert.AreEqual(0, f.Host.Shown().Count());
        Assert.AreEqual((5, 3), f.Host.Callbacks.Single(), "the callback eye candy called back once (tag 5, phase 3)");
        Assert.IsTrue(f.Host.Play(Hero, 11, 0, 5));
        Assert.AreEqual(1, f.Host.Remove(Hero, 11, 5, false));
    }

    [TestMethod]
    public void A_looping_entry_played_as_a_callback_goal_is_not_found_by_a_remove()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 11, AiExecutorRules.PlayCallback, 5));
        f.Run(500);
        Assert.AreEqual(0, f.Host.Remove(Hero, 11, 5, true), "the remove walks goal-45 runs only (GT71)");
        Assert.AreEqual(1, f.Host.Shown().Count());
        Assert.AreEqual(1, f.Host.EndTagged(5), "the host's own end by tag still reaches it");
    }

    [TestMethod]
    public void A_queued_eye_candy_begins_in_the_places_the_one_before_it_freed()
    {
        using var f = new Fixture();
        for (var i = 0; i < 6; i++) Assert.IsTrue(f.Host.Play(Hero, 11, 0, 30 + i));
        f.Run(60);
        Assert.AreEqual(6, f.Host.Shown().Count());
        Assert.IsTrue(f.Host.Play(Hero, 10, 0, 40));
        Assert.IsTrue(f.Host.Play(Hero, 10, AiExecutorRules.PlayStack, 41));
        f.Run(60);
        Assert.AreEqual(7, f.Host.Shown().Count(), "the first of the pair is the seventh place");
        f.Run(500);
        Assert.AreEqual(7, f.Host.Shown().Count(), "the queued one took the place its predecessor freed (it did not count it)");
        Assert.AreEqual(1, f.Host.Shown().Count(s => s.Eye.Tag == 41));
    }

    [TestMethod]
    public void Places_count_the_other_shown_eye_candies_of_the_owner()
    {
        using var f = new Fixture();
        var cast = Enumerable.Repeat(new EyeCandyParts(true, false, false), 7).ToArray();
        var host = new AreaEyeCandyHost(f.Session, i => f.Entries.GetValueOrDefault(i), (art, parts) => f.Facts, null, owner => owner == Hero ? cast : []);
        Assert.IsFalse(host.Play(Hero, 10, 0, 1), "seven cast parts fill the foreground list");
        Assert.IsTrue(host.Play("sector:1:1", 10, 0, 1));
    }

    [TestMethod]
    public void The_goal_46_and_the_fire_goal_run_through_the_same_host()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 10, AiExecutorRules.PlayReverse, 1));
        Assert.IsTrue(f.Host.Play(Hero, 10, AiExecutorRules.PlayFireDamage, 2));
        f.Run(60);
        var frames = f.Host.Shown().ToDictionary(s => s.Goal, s => s.Eye.Frame);
        Assert.AreEqual(3, frames[GoalType.EyeCandyReverse], "a reverse eye candy starts on its last frame");
        Assert.AreEqual(0, frames[GoalType.EyeCandyFireDamage]);
        Assert.IsTrue(f.Host.FireDamages >= 1, "the begin burns");
    }

    [TestMethod]
    public void An_add_with_a_limit_is_refused_once_more_than_that_many_foreground_eye_candies_stand_on_the_owner()
    {
        // ARC-RENDER-1794 (0x4CCF70, the damage effect's limit 3): the loop over the owner's foreground list gives up at the first eye candy that goes past the limit, so a fourth one plays and a fifth does not.
        using var f = new Fixture();
        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(f.Host.Play(Hero, 11, 0, -1, null, 3), $"copy {i}: {i} stand on the owner");
            f.Run(60);
        }
        Assert.AreEqual(4, f.Host.Shown().Count());
        var skipped = f.Host.Skipped;
        Assert.IsFalse(f.Host.Play(Hero, 11, 0, -1, null, 3), "four stand: past the limit");
        Assert.AreEqual(skipped + 1, f.Host.Skipped);
        Assert.IsTrue(f.Host.Play(Hero, 11, 0, -1), "no limit: a copy is allowed up to the seven places");
        Assert.IsTrue(f.Host.Play("sector:1:1", 11, 0, -1, null, 3), "another object's list is its own");
        f.Run(60);
        Assert.IsTrue(f.Host.Play(Hero, 14, 0, -1, null, 3), "an entry with no foreground part is not counted against the limit");
        // The count is of the same art number (0x502710): four eye candies of another art do not stop an entry of a new one (the foreground list holds 7).
        using var g = new Fixture();
        g.Entries[15] = new(0x1500 | 0x80000, Fore, -1, null, (0, 0, 0), 0);
        for (var i = 0; i < 4; i++) Assert.IsTrue(g.Host.Play(Hero, 11, 0, -1), $"other art {i}");
        g.Run(60);
        for (var i = 0; i < 3; i++) { Assert.IsTrue(g.Host.Play(Hero, 15, 0, -1, null, 3), $"own art {i}"); g.Run(60); }
    }

    [TestMethod]
    public void A_duplicate_is_refused_with_check_already_and_a_full_list_places_nothing()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 11, AiExecutorRules.PlayCheckAlready, 3));
        Assert.IsFalse(f.Host.Play(Hero, 11, AiExecutorRules.PlayCheckAlready, 3), "the same art and tag");
        Assert.IsTrue(f.Host.Play(Hero, 11, AiExecutorRules.PlayCheckAlready, 4), "another tag");
        Assert.IsTrue(f.Host.Play(Hero, 11, 0, 3), "without the flag a copy is allowed");
        f.Run(60);
        for (var i = 0; i < 4; i++) Assert.IsTrue(f.Host.Play(Hero, 11, 0, 10 + i), $"copy {i}");
        f.Run(60);
        Assert.AreEqual(7, f.Host.Shown().Count());
        Assert.IsFalse(f.Host.Play(Hero, 11, 0, 99), "7 foreground places are taken");
        Assert.IsFalse(f.Host.PlacesFree(Hero, new EyeCandyParts(true, false, false)));
        Assert.IsTrue(f.Host.PlacesFree(Hero, new EyeCandyParts(false, false, true)), "the underlay list is its own");
        Assert.IsTrue(f.Host.PlacesFree("sector:1:1", new EyeCandyParts(true, false, false)), "another object's");
        Assert.IsTrue(f.Host.Skipped >= 2);
    }

    [TestMethod]
    public void An_eye_candy_with_the_stack_flag_plays_after_the_previous_one()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Play(Hero, 10, 0, 1));
        Assert.IsTrue(f.Host.Play(Hero, 14, AiExecutorRules.PlayStack, 1));
        var run = f.Session.QueryGoals().RunsOf(Hero).Single();
        Assert.AreEqual(2, run.Frames.Length, "one run holds both");
        f.Run(60);
        Assert.AreEqual(10, f.Host.Shown().Single().Eye.Entry, "the first plays first");
        f.Run(500);
        Assert.AreEqual(14, f.Host.Shown().Single().Eye.Entry, "then the queued one begins");
        f.Run(600);
        Assert.AreEqual(0, f.Runs);
    }

    [TestMethod]
    public void Ending_by_tag_and_losing_all_end_the_runs_at_level_6()
    {
        using var f = new Fixture();
        f.Host.Play(Hero, 11, 0, 21);
        f.Host.Play("sector:1:1", 11, 0, 22);
        f.Host.StartHover(Hero);
        f.Run(60);
        Assert.AreEqual(3, f.Runs);
        Assert.AreEqual(1, f.Host.EndTagged(21));
        Assert.AreEqual(2, f.Runs);
        f.Host.LoseAll();
        Assert.AreEqual(0, f.Runs);
        Assert.AreEqual(0, f.Host.Shown().Count());
    }

    [TestMethod]
    public void The_hover_bobs_ten_pixels_and_ending_it_leaves_the_object_where_it_is()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.StartHover(Hero));
        Assert.IsFalse(f.Host.StartHover(Hero), "an object hovers once");
        f.Run(40);
        Assert.IsTrue(f.Host.HoverOffset(Hero) is -15 or -13, "raised by 15 at the begin; the first bob step follows at once");
        f.Run(1_000);
        var offset = f.Host.HoverOffset(Hero);
        Assert.IsTrue(offset is >= -15 and <= -5, $"the bob stays within 10 pixels: {offset}");
        Assert.AreEqual(1, f.Host.EndHover(Hero));
        f.Run(500);
        Assert.AreEqual(offset, f.Host.HoverOffset(Hero), "an interrupt runs no end node: the offset stays");
        Assert.AreEqual(0, f.Runs);
        Assert.AreEqual(0, f.Host.HoverOffset("sector:1:1"));
    }

    [TestMethod]
    public void An_overlay_run_with_no_host_ends_and_a_foreign_one_is_not_a_scenery_or_door()
    {
        using var f = new Fixture();
        f.Host.Play(Hero, 11, 0, 1);
        var run = f.Session.QueryGoals().RunsOf(Hero).Single();
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, null, () => null);
        Assert.AreEqual(SessionGoalStepAnswer.End, router.StepGoal(run, default));
        Assert.AreEqual(1, router.OverlaysLost);
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(run.Bottom.Goal));
    }
}
