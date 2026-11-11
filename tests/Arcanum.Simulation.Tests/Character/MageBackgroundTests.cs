using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>
/// ARC-MAGIC-028 (FR-CHAR-020): the Day, Night, Sky and Nature Mage backgrounds move magick points by plus or minus 4 before
/// effects, as the stat getter 0x4B0490 does (ledger BG6); aptitude reads the moved value. Synthetic characters only.
/// </summary>
[TestClass]
public sealed class MageBackgroundTests
{
    private static CharacterStatsSnapshot Query(CharacterStatsOwner owner, CharacterStatContext context)
    {
        var result = owner.Query(context);
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
        return result.Snapshot!;
    }

    private static CharacterStatsOwner Mage(int magick = 20, int tech = 0)
    {
        var owner = Owner();
        Accepted(owner.SetBase(CharacterStat.MagickPoints, magick));
        Accepted(owner.SetBase(CharacterStat.TechPoints, tech));
        return owner;
    }

    [TestMethod]
    public void TheDayMageGainsFourFromSixToSeventeenAndLosesFourAtNight()
    {
        var owner = Mage();
        foreach (var (hour, expected) in new[] { (0, 16), (5, 16), (6, 24), (12, 24), (17, 24), (18, 16), (23, 16) })
            Assert.AreEqual(expected, Query(owner, new(0, Background: 62, Hour: hour)).GetEffective(CharacterStat.MagickPoints), $"hour {hour}");
    }

    [TestMethod]
    public void TheNightMageIsTheReverseOfTheDayMage()
    {
        var owner = Mage();
        foreach (var (hour, expected) in new[] { (5, 24), (6, 16), (17, 16), (18, 24), (23, 24) })
            Assert.AreEqual(expected, Query(owner, new(0, Background: 63, Hour: hour)).GetEffective(CharacterStat.MagickPoints), $"hour {hour}");
    }

    [TestMethod]
    public void TheSkyMageReadsTheOutdoorTileAndTheNatureMageTheNatureTile()
    {
        var owner = Mage();
        Assert.AreEqual(24, Query(owner, new(0, Background: 64, OutdoorTile: true)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(16, Query(owner, new(0, Background: 64, OutdoorTile: false, NatureTile: true)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(24, Query(owner, new(0, Background: 65, NatureTile: true)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(16, Query(owner, new(0, Background: 65, OutdoorTile: true)).GetEffective(CharacterStat.MagickPoints));
    }

    [TestMethod]
    public void NoBackgroundAnotherBackgroundOrAnNpcAddsNothing()
    {
        var owner = Mage();
        foreach (var background in new[] { 0, 1, 61, 66, 69, 78 })
            Assert.AreEqual(20, Query(owner, new(0, Background: background, Hour: 12, OutdoorTile: true, NatureTile: true)).GetEffective(CharacterStat.MagickPoints), $"background {background}");
        // The stored base is untouched and only magick points move.
        var moved = Query(owner, new(0, Background: 62, Hour: 12));
        Assert.AreEqual(20, moved.GetBase(CharacterStat.MagickPoints));
        Assert.AreEqual(8, moved.GetEffective(CharacterStat.Intelligence));
        Assert.AreEqual(0, moved.GetEffective(CharacterStat.TechPoints));
    }

    [TestMethod]
    public void TheMovedValueComesBeforeEffectsAndTheRangeClamp()
    {
        // At 0 the night penalty clamps to the stat's minimum 0; at 210 the day bonus clamps to 210.
        Assert.AreEqual(0, Query(Mage(0), new(0, Background: 62, Hour: 0)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(210, Query(Mage(210), new(0, Background: 62, Hour: 12)).GetEffective(CharacterStat.MagickPoints));
        // An effect of +10 on top: 20 - 4 + 10 = 26 at night, 20 + 4 + 10 = 34 by day.
        var owner = Mage();
        Assert.IsNotNull(owner.AddEffect(Effect(1, Add(CharacterStat.MagickPoints, 10)), EffectCause.Item).AddedEffect);
        Assert.AreEqual(26, Query(owner, new(0, Background: 62, Hour: 0)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(34, Query(owner, new(0, Background: 62, Hour: 12)).GetEffective(CharacterStat.MagickPoints));
        // The clamp runs once after the sum: 207 + 4 = 211, then -10 gives 201, not 210 - 10 = 200.
        var high = Mage(207);
        Assert.IsNotNull(high.AddEffect(Effect(1, Add(CharacterStat.MagickPoints, -10)), EffectCause.Item).AddedEffect);
        Assert.AreEqual(201, Query(high, new(0, Background: 62, Hour: 12)).GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(193, Query(high, new(0, Background: 62, Hour: 0)).GetEffective(CharacterStat.MagickPoints));
    }

    [TestMethod]
    public void AptitudeReadsTheMovedMagickPoints()
    {
        // (50 x MP - 55 x TP) / 10 + sector, read from the effective (moved) magick points.
        var owner = Mage(10, 0);
        Assert.AreEqual(50, Query(owner, new(0)).GetEffective(CharacterStat.MagickTechAptitude));
        Assert.AreEqual(70, Query(owner, new(0, Background: 62, Hour: 12)).GetEffective(CharacterStat.MagickTechAptitude), "MP 14: 140 / 10 x 5");
        Assert.AreEqual(30, Query(owner, new(0, Background: 62, Hour: 0)).GetEffective(CharacterStat.MagickTechAptitude), "MP 6");
        Assert.AreEqual(67, Query(owner, new(-3, Background: 63, Hour: 0)).GetEffective(CharacterStat.MagickTechAptitude), "night mage at night: MP 14 = 70, sector -3");
    }

    [TestMethod]
    public void AnHourOutsideTheClockIsAnInvalidContext()
    {
        var owner = Mage();
        Assert.AreEqual(CharacterStatsFailure.InvalidContext, owner.Query(new(0, Hour: -1)).Failure);
        Assert.AreEqual(CharacterStatsFailure.InvalidContext, owner.Query(new(0, Hour: 24)).Failure);
        Assert.AreEqual(CharacterStatsFailure.None, owner.Query(new(0, Hour: 23)).Failure);
    }
}
