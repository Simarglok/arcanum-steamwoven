using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-157 (bug B23): the hero's stats by the original's numbers, as the script command get stat reads them. Numbers are invented.
[TestClass]
public sealed class ScriptStatTests
{
    [TestMethod]
    public void The_hero_answers_the_primary_stats_the_aptitude_the_level_and_the_alignment_by_the_originals_numbers()
    {
        using var owner = CreateWith(HalfElf() with { Level = 7, Alignment = -120 });
        // The half-elf's race effect: dexterity +1, constitution −1 (the hero tests); the stat numbers 0..7 are the character stat order.
        Assert.AreEqual(9, owner.QueryHeroScriptStat(1));
        Assert.AreEqual(7, owner.QueryHeroScriptStat(2));
        Assert.AreEqual(8, owner.QueryHeroScriptStat(0));
        Assert.AreEqual(7, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatLevel));
        Assert.AreEqual(-120, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment), "the stored alignment, negative as it is");
        Assert.IsNotNull(owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAptitude));
    }

    [TestMethod]
    public void A_stat_the_session_does_not_hold_and_a_session_without_a_hero_answer_nothing()
    {
        using var owner = CreateWith(HalfElf());
        foreach (var stat in new[] { -1, 8, 15, 18, 20, 21, 24, 26, 27, 1000 }) Assert.IsNull(owner.QueryHeroScriptStat(stat), $"stat {stat}");
        using var none = Create();
        foreach (var stat in new[] { 0, 4, 17, 19 }) Assert.IsNull(none.QueryHeroScriptStat(stat));
        Assert.IsFalse(none.QueryHeroDumbBackground());
    }

    [TestMethod]
    public void The_dumb_test_passes_for_the_backgrounds_55_74_and_75_only()
    {
        foreach (var (background, dumb) in new[] { (0, false), (54, false), (55, true), (56, false), (73, false), (74, true), (75, true), (76, false) })
        {
            using var owner = CreateWith(HalfElf() with { Background = background });
            Assert.AreEqual(dumb, owner.QueryHeroDumbBackground(), $"background {background}");
        }
    }
}
