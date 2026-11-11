using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-031 (FR-CHAR-020): the hero's stat context from the session: the sector adjustment and tile facts of the viewer's ground, the hero's background and the clock's hour reach the stat query
// (the ARC-MAGIC-028 rules); invented numbers over the half-elf fixture hero.
[TestClass]
public sealed class HeroStatContextTests
{
    private static GameSessionOwner Owner(int background = 0) =>
        GameSessionOwner.Create(Descriptor() with { Hero = HalfElf([8, 8, 8, 8, 8, 8, 8, 8, 10, 0]) with { Background = background } }).Owner!;

    private static int Stat(GameSessionOwner owner, CharacterStat stat, CharacterStatContext context) => owner.QueryHeroStats(context)!.Value.Snapshot!.GetEffective(stat);

    [TestMethod]
    public void Without_a_ground_the_context_has_the_background_and_the_hour_and_nothing_else()
    {
        using var owner = Owner(62);
        var context = owner.HeroStatContext(150, true);
        Assert.AreEqual((0, 150, true, 62, owner.QueryClockSnapshot().Hour, false, false), (context.SectorAptitudeAdjustment, context.PoisonLevel, context.CrippledLegs,
            context.Background, context.Hour, context.OutdoorTile, context.NatureTile));
        Assert.AreEqual(0, Owner().HeroStatContext().Background, "no background: the none value");
    }

    [TestMethod]
    public void The_viewers_ground_reaches_the_context_and_the_adjustment_stays_within_its_bound()
    {
        using var owner = Owner();
        owner.HeroGround = () => new(40, true, true);
        var context = owner.HeroStatContext();
        Assert.AreEqual((40, true, true), (context.SectorAptitudeAdjustment, context.OutdoorTile, context.NatureTile));
        owner.HeroGround = () => new(-500, false, true);
        Assert.AreEqual(-100, owner.HeroStatContext().SectorAptitudeAdjustment);
        Assert.AreEqual(CharacterStatsFailure.None, owner.QueryHeroStats(owner.HeroStatContext())!.Value.Failure, "a bounded context is a valid query");
    }

    [TestMethod]
    public void The_sector_adjustment_moves_aptitude_and_the_mage_backgrounds_move_magick_points_by_four()
    {
        using var plain = Owner();
        var baseline = Stat(plain, CharacterStat.MagickTechAptitude, new(0));
        plain.HeroGround = () => new(10);
        Assert.AreEqual(baseline + 10, Stat(plain, CharacterStat.MagickTechAptitude, plain.HeroStatContext()), "sector adjustment 10");
        Assert.AreEqual(baseline, Stat(plain, CharacterStat.MagickTechAptitude, new(0)), "a plain context ignores it");

        // Sky Mage (64): +4 on an outdoor tile, -4 elsewhere, around the magick points the plain hero has.
        using var sky = Owner(64);
        var magick = Stat(plain, CharacterStat.MagickPoints, new(0));
        sky.HeroGround = () => new(0, true, false);
        Assert.AreEqual(magick + 4, Stat(sky, CharacterStat.MagickPoints, sky.HeroStatContext()));
        sky.HeroGround = () => new(0, false, false);
        Assert.AreEqual(magick - 4, Stat(sky, CharacterStat.MagickPoints, sky.HeroStatContext()));

        // Nature Mage (65) reads the nature flag, not the outdoor one.
        using var nature = Owner(65);
        nature.HeroGround = () => new(0, true, false);
        Assert.AreEqual(magick - 4, Stat(nature, CharacterStat.MagickPoints, nature.HeroStatContext()));
        nature.HeroGround = () => new(0, false, true);
        Assert.AreEqual(magick + 4, Stat(nature, CharacterStat.MagickPoints, nature.HeroStatContext()));

        // Day Mage (62) and Night Mage (63) read the clock's hour: opposite swings whatever the hour is.
        using var day = Owner(62); using var night = Owner(63);
        var hour = day.QueryClockSnapshot().Hour;
        var daytime = hour is >= 6 and < 18;
        Assert.AreEqual(magick + (daytime ? 4 : -4), Stat(day, CharacterStat.MagickPoints, day.HeroStatContext()));
        Assert.AreEqual(magick + (daytime ? -4 : 4), Stat(night, CharacterStat.MagickPoints, night.HeroStatContext()));
    }
}
