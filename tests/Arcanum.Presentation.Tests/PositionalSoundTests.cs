using Arcanum.Presentation.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-042: the positional rules against the behaviour recorded in docs/research/audio-evidence.md SN4–SN7 (the
/// original's positional parameters, size and projection); radii and points are invented, expected values worked by hand.</summary>
[TestClass]
public sealed class PositionalSoundTests
{
    private static readonly PositionalRange Large = new(150, 800, 100);
    private static PositionalProfile Profile(PositionalRange? large = null) =>
        new(new(50, 150, 40), new(50, 400, 70), large ?? Large, new(50, 1500, 100), 150, 400);

    private static PositionalLevel At(long x, long y, PositionalProfile? profile = null, PositionalSize size = PositionalSize.Large) =>
        PositionalSoundRules.Level(new(x, y), new(0, 0), profile ?? Profile(), size);

    [TestMethod]
    public void Volume_falls_linearly_between_the_radii_over_a_distance_that_counts_y_twice()
    {
        Assert.AreEqual(new PositionalLevel(127, 64), At(0, 0));
        Assert.AreEqual(127, At(149, 0).Volume, "inside the minimum radius");
        Assert.AreEqual(127, At(150, 0).Volume, "at the minimum radius: 127 × 0 / 650 + 127");
        Assert.AreEqual(59, At(500, 0).Volume, "127 × (150 − 500) / 650 = −68.4 → −68, + 127");
        Assert.AreEqual(118, At(0, 100).Volume, "y counts twice: distance 200, 127 × −50 / 650 = −9.8 → −9");
        Assert.AreEqual(0, At(800, 0).Volume, "at the maximum radius: −127 + 127");
        Assert.AreEqual(0, At(0, 401).Volume, "beyond the maximum radius");
        Assert.AreEqual(127 * 40 / 100, At(0, 0, size: PositionalSize.Small).Volume, "the small size keeps 40 %");
        Assert.AreEqual(51, At(0, 100, size: PositionalSize.Medium).Volume, "medium: 127 × −150 / 350 = −54.4 → −54, + 127 = 73, × 70 / 100");
    }

    [TestMethod]
    public void The_distance_is_rounded_in_single_precision_to_the_nearest_even()
    {
        // √(57² + (2·1624)²) = 3248.50012: single precision makes it 3248.5, which rounds to the even 3248 (double: 3249).
        var edge = Profile(new(3248, 3248, 100));
        Assert.AreEqual(127, At(57, 1624, edge).Volume, "3248 is not beyond a maximum of 3248");
        Assert.AreEqual(0, At(57, 1625, edge).Volume);
        Assert.AreEqual(127, At(1, 1, Profile(new(2, 2, 100))).Volume, "√5 = 2.24 → 2");
        Assert.AreEqual(0, At(1, 1, Profile(new(1, 1, 100))).Volume);
    }

    [TestMethod]
    public void Balance_follows_the_horizontal_offset_between_the_pan_distances()
    {
        Assert.AreEqual(64, At(150, 300).Balance, "within the minimum pan distance; y does not pan");
        Assert.AreEqual(26, At(-300, 0).Balance, "left: 64 × (150 − 300) / 250 = −38.4 → −38, + 64");
        Assert.AreEqual(102, At(300, 0).Balance, "right: 64 × 150 / 250 = 38.4 → 38, + 64");
        Assert.AreEqual(127, At(400, 0).Balance, "at the maximum: 64 + 64 clamps to 127");
        Assert.AreEqual(0, At(-400, 0).Balance, "left at the maximum: 64 − 64");
        Assert.AreEqual(127, At(401, 0).Balance, "hard right beyond it");
        Assert.AreEqual(0, At(-401, 0).Balance, "hard left");
        Assert.AreEqual(64, At(0, 5000).Balance, "straight below the listener: centre, however far");
        var narrow = Profile() with { MinPanDistance = 200, MaxPanDistance = 200 };
        Assert.AreEqual((64, 0, 127), (At(200, 0, narrow).Balance, At(-201, 0, narrow).Balance, At(201, 0, narrow).Balance), "equal pan distances: centre, then hard");
        Assert.AreEqual(1.0, PositionalSoundRules.Pan(128), "the backend pan clamps");
        Assert.AreEqual(-1.0, PositionalSoundRules.Pan(0));
        Assert.AreEqual(0.0, PositionalSoundRules.Pan(64));
    }

    [TestMethod]
    public void Tiles_project_isometrically_and_scenery_flags_give_the_size()
    {
        Assert.AreEqual(new SoundPoint(-40, 0), PositionalSoundRules.Point(0, 0));
        Assert.AreEqual(new SoundPoint(40 * (7 - 3 - 1), 20 * 10), PositionalSoundRules.Point(3, 7));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PositionalSoundRules.Point(PositionalSoundRules.MaxCoordinate + 1, 0));
        Assert.AreEqual(PositionalSize.Small, PositionalSoundRules.Size(3, 0x1C0), "small wins");
        Assert.AreEqual(PositionalSize.Medium, PositionalSoundRules.Size(3, 0x180));
        Assert.AreEqual(PositionalSize.ExtraLarge, PositionalSoundRules.Size(3, 0x100));
        Assert.AreEqual(PositionalSize.Large, PositionalSoundRules.Size(3, 0x20));
        Assert.AreEqual(PositionalSize.Large, PositionalSoundRules.Size(16, 0x40), "only scenery has a size");
        Assert.AreEqual(127, At(0, 0, Profile(new(800, 150, 100))).Volume, "an inverted range: inside the minimum");
        Assert.AreEqual(127, At(500, 0, Profile(new(800, 150, 100))).Volume, "below the minimum still");
    }
}
