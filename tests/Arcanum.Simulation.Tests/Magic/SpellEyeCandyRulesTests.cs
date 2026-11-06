using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-017: the casting goal's eye candy (goal 27) and an eye candy's own goal (ag_eye_candy), ledger EC rows.
[TestClass]
public sealed class SpellEyeCandyRulesTests
{
    private static (List<EyeCandyStep> Steps, List<long> Releases) Run(CastEyeCandyAnimation animation, long until, bool passes = true)
    {
        var steps = new List<EyeCandyStep>();
        var releases = new List<long>();
        for (var now = animation.StartMs; now <= until && !animation.Ended; now++)
            steps.AddRange(animation.Advance(now, () => { releases.Add(now); return passes; }));
        return (steps, releases);
    }

    [TestMethod]
    public void The_pace_is_a_thousand_over_the_frame_rate_truncated()
    {
        Assert.AreEqual(66, new EyeCandyClip(4, 3, 15).PaceMs);
        Assert.AreEqual(33, new EyeCandyClip(32, 31, 30).PaceMs);
        Assert.IsNull(new EyeCandyClip(4, 3, 0).PaceMs);
        Assert.AreEqual(4 * 6 + 2, SpellEyeCandyRules.EntryIndex(4, SpellEyeCandyRules.Destination));
    }

    [TestMethod]
    public void The_release_comes_at_the_action_frame_and_the_end_one_pace_after_the_last_frame()
    {
        var animation = new CastEyeCandyAnimation(new(4, 3, 15), null, true, 1000);
        var (steps, releases) = Run(animation, 5000);
        // Frame k shows at (k - 1) pace up to the release (k = A = 3 at 132 ms); the last frame is then reached and the goal ends a pace later.
        CollectionAssert.AreEqual(new long[] { 1132 }, releases);
        CollectionAssert.AreEqual(new[] { (1000L, 1), (1066L, 2), (1132L, 3) },
            steps.Where(s => s.Kind == EyeCandyStepKind.Frame).Select(s => (s.AtMs, s.Frame)).ToArray());
        Assert.AreEqual(1198, steps.Single(s => s.Kind == EyeCandyStepKind.Ended).AtMs);
        Assert.IsTrue(animation.Released);
        Assert.IsTrue(steps.Single(s => s.Kind == EyeCandyStepKind.Release).Passed);
    }

    [TestMethod]
    public void After_a_release_the_goal_steps_again_at_once()
    {
        // A = 2 of 6 frames, pace 100: frame 2 and the release at 100, frame 3 at once with it, then one frame a pace.
        var (steps, releases) = Run(new CastEyeCandyAnimation(new(6, 2, 10), null, true, 0), 2000);
        CollectionAssert.AreEqual(new long[] { 100 }, releases);
        CollectionAssert.AreEqual(new[] { (0L, 1), (100L, 2), (100L, 3), (200L, 4), (300L, 5) },
            steps.Where(s => s.Kind == EyeCandyStepKind.Frame).Select(s => (s.AtMs, s.Frame)).ToArray());
        Assert.AreEqual(500, steps.Last().AtMs);
        Assert.AreEqual(EyeCandyStepKind.Ended, steps.Last().Kind);
    }

    [TestMethod]
    public void After_a_missile_launch_the_goal_waits_one_pace()
    {
        // ARC-MAGIC-018: the launch's true step restarts goal 27 with a pace's delay (0x4246E0): frame 3 comes at 200, not with the
        // release at 100, and the end one pace later than without a missile.
        var animation = new CastEyeCandyAnimation(new(6, 2, 10), null, true, 0);
        var steps = new List<EyeCandyStep>();
        for (var now = 0L; now <= 2000 && !animation.Ended; now++)
            steps.AddRange(animation.Advance(now, () => { animation.PaceAfterRelease = true; return true; }));
        CollectionAssert.AreEqual(new[] { (0L, 1), (100L, 2), (200L, 3), (300L, 4), (400L, 5) },
            steps.Where(s => s.Kind == EyeCandyStepKind.Frame).Select(s => (s.AtMs, s.Frame)).ToArray());
        Assert.AreEqual((EyeCandyStepKind.Ended, 600L), (steps.Last().Kind, steps.Last().AtMs));
    }

    [TestMethod]
    public void A_failed_release_ends_the_goal_at_once()
    {
        var animation = new CastEyeCandyAnimation(new(10, 3, 10), null, true, 0);
        var (steps, releases) = Run(animation, 5000, passes: false);
        CollectionAssert.AreEqual(new long[] { 200 }, releases);
        Assert.AreEqual(200, steps.Single(s => s.Kind == EyeCandyStepKind.Ended).AtMs);
        Assert.IsFalse(steps.Single(s => s.Kind == EyeCandyStepKind.Release).Passed);
    }

    [TestMethod]
    public void An_action_frame_never_reached_or_a_background_timing_part_never_releases()
    {
        foreach (var animation in new[] { new CastEyeCandyAnimation(new(4, 0, 15), null, true, 0), new CastEyeCandyAnimation(new(4, 9, 15), null, true, 0),
            new CastEyeCandyAnimation(new(4, 3, 15), null, false, 0) })
        {
            var (steps, releases) = Run(animation, 5000);
            Assert.AreEqual(0, releases.Count);
            Assert.IsFalse(animation.Released);
            // Without the release the last frame comes at (F - 2) pace and the end at F pace.
            Assert.AreEqual(4 * 66, steps.Single(s => s.Kind == EyeCandyStepKind.Ended).AtMs);
        }
    }

    [TestMethod]
    public void A_background_at_its_last_frame_ends_the_goal_after_the_timing_part_moved()
    {
        var (steps, releases) = Run(new CastEyeCandyAnimation(new(10, 8, 10), new(3, 0, 10), true, 0), 5000);
        Assert.AreEqual(0, releases.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, steps.Where(s => s.Kind == EyeCandyStepKind.Frame).Select(s => s.Frame).ToArray());
        Assert.AreEqual(300, steps.Single(s => s.Kind == EyeCandyStepKind.Ended).AtMs);
    }

    [TestMethod]
    public void Without_cast_eye_candy_the_release_runs_at_the_start_and_the_goal_ends()
    {
        foreach (var animation in new[] { new CastEyeCandyAnimation(null, null, true, 50), new CastEyeCandyAnimation(new(4, 3, 0), null, true, 50) })
        {
            Assert.IsTrue(animation.Immediate);
            var steps = animation.Advance(50, () => true);
            CollectionAssert.AreEqual(new[] { EyeCandyStepKind.Release, EyeCandyStepKind.Ended }, steps.Select(s => s.Kind).ToArray());
            Assert.IsTrue(steps.All(s => s.AtMs == 50));
            Assert.AreEqual(0, animation.Advance(5000, () => throw new AssertFailedException("no second release")).Count);
        }
    }

    [TestMethod]
    public void The_cast_pace_comes_from_the_art_the_play_built_last_when_it_is_another_parts()
    {
        Assert.AreEqual(100, Run(new CastEyeCandyAnimation(new(4, 3, 10), null, true, 0, pace: new(4, 3, 20)), 5000).Releases.Single());
    }

    [TestMethod]
    public void Only_an_animating_entry_with_a_foreground_or_underlay_plays_as_its_own_goal()
    {
        Assert.IsTrue(SpellEyeCandyRules.Plays(true, new(true, false, false)));
        Assert.IsTrue(SpellEyeCandyRules.Plays(true, new(false, false, true)));
        Assert.IsFalse(SpellEyeCandyRules.Plays(true, new(false, true, false)));
        Assert.IsFalse(SpellEyeCandyRules.Plays(false, new(true, true, true)));
        var parts = new EyeCandyParts(true, true, true);
        Assert.IsFalse(parts.AnimatesForeground);
        Assert.IsFalse(parts.AnimatesBackground);
        Assert.IsTrue(parts.TimesRelease);
        Assert.IsFalse(new EyeCandyParts(false, true, false).TimesRelease);
    }
}
