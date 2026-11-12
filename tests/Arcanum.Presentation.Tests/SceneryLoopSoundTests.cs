using Arcanum.Presentation.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-RENDER-1126: the looping sound of an animated scenery decided at the loop's begin node (0x42DA50).</summary>
[TestClass]
public sealed class SceneryLoopSoundTests
{
    private static readonly PositionalProfile Profile = new(new(10, 400, 100), new(10, 600, 100), new(10, 1000, 100), new(10, 1399, 100), 20, 300);

    [TestMethod]
    public void The_range_is_the_size_classs_maximum_radius_over_40_truncated()
    {
        Assert.AreEqual(10, SceneryLoopSoundRules.RangeTiles(Profile, PositionalSize.Small));
        Assert.AreEqual(15, SceneryLoopSoundRules.RangeTiles(Profile, PositionalSize.Medium));
        Assert.AreEqual(25, SceneryLoopSoundRules.RangeTiles(Profile, PositionalSize.Large));
        Assert.AreEqual(34, SceneryLoopSoundRules.RangeTiles(Profile, PositionalSize.ExtraLarge), "1399 / 40 truncates");
    }

    [TestMethod]
    public void The_distance_is_the_larger_coordinate_difference()
    {
        Assert.AreEqual(7, SceneryLoopSoundRules.Distance(10, 10, 3, 8));
        Assert.AreEqual(4, SceneryLoopSoundRules.Distance(0, 0, -1, 4));
        Assert.AreEqual(0, SceneryLoopSoundRules.Distance(5, 5, 5, 5));
    }

    [TestMethod]
    public void The_begin_node_starts_the_sound_within_the_range_with_no_voice_and_stops_a_voice_beyond_it()
    {
        Assert.AreEqual(SceneryLoopSoundDecision.Start, SceneryLoopSoundRules.Decide(25, 25, hasVoice: false), "the range is inclusive");
        Assert.AreEqual(SceneryLoopSoundDecision.Start, SceneryLoopSoundRules.Decide(25, 0, hasVoice: false));
        Assert.AreEqual(SceneryLoopSoundDecision.None, SceneryLoopSoundRules.Decide(25, 26, hasVoice: false), "beyond the range with no voice: nothing");
        Assert.AreEqual(SceneryLoopSoundDecision.Stop, SceneryLoopSoundRules.Decide(25, 26, hasVoice: true));
        Assert.AreEqual(SceneryLoopSoundDecision.None, SceneryLoopSoundRules.Decide(25, 10, hasVoice: true), "a voice that sounds is never doubled");
        Assert.AreEqual(SceneryLoopSoundDecision.None, SceneryLoopSoundRules.Decide(25, 25, hasVoice: true));
    }

    [TestMethod]
    public void A_missing_profile_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => SceneryLoopSoundRules.RangeTiles(null!, PositionalSize.Large));
    }
}
