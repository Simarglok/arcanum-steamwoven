using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
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

    private static EffectChange Change(int attribute, int value, EffectChangeOperator op = EffectChangeOperator.Add) => new(EffectAttributeKind.Stat, attribute, op, value);

    // ARC-AI-2750 (AC1, AC2): the stat getter 0x4B0490 lays the effect list's changes of stats 17 and 19 over the stored values for the PC as for an NPC.
    [TestMethod]
    public void The_level_and_the_alignment_are_the_stored_values_with_the_effect_lists_changes_laid_over_them()
    {
        using var owner = CreateWith(HalfElf() with { Level = 7, Alignment = -120 });
        var higher = new SessionEffectDefinition(130, [Change(GameSessionOwner.ScriptStatLevel, 3), Change(GameSessionOwner.ScriptStatAlignment, 50)]);
        var doubled = new SessionEffectDefinition(131, [Change(GameSessionOwner.ScriptStatAlignment, 2, EffectChangeOperator.Multiply)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, higher, EffectCause.Spell).Disposition);
        Assert.AreEqual(10, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatLevel));
        Assert.AreEqual(-70, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment));
        // Multiply comes before add in the aggregation: (-120 * 2) + 50.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, doubled, EffectCause.Spell).Disposition);
        Assert.AreEqual(-190, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment));
        // The stored values stay as they were.
        Assert.AreEqual(7, owner.QueryHero()!.Level);
        Assert.AreEqual(-120, owner.QueryHero()!.Alignment);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Spell, all: true).Disposition);
        Assert.AreEqual(7, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatLevel));
        Assert.AreEqual(-120, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment));
    }

    [TestMethod]
    public void The_level_and_the_alignment_under_effects_are_clamped_to_the_stats_range()
    {
        using var owner = CreateWith(HalfElf() with { Level = 50, Alignment = 900 });
        var big = new SessionEffectDefinition(132, [Change(GameSessionOwner.ScriptStatLevel, 40), Change(GameSessionOwner.ScriptStatAlignment, 500)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, big, EffectCause.Spell).Disposition);
        Assert.AreEqual(SessionHeroLimits.MaxLevel, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatLevel));
        Assert.AreEqual(SessionHeroLimits.MaxAlignment, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment));
        var low = new SessionEffectDefinition(133, [Change(GameSessionOwner.ScriptStatLevel, -100), Change(GameSessionOwner.ScriptStatAlignment, -5000)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, low, EffectCause.Spell).Disposition);
        Assert.AreEqual(SessionHeroLimits.MinLevel, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatLevel));
        Assert.AreEqual(SessionHeroLimits.MinAlignment, owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment));
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
