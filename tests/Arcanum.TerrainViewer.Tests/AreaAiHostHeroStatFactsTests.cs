using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-2750 (stat getter 0x4B0490, stats 17 and 19, the PC included): the hero's level and alignment in the host's hostility facts are the stored values with the
/// hero's effect list laid over them, as the script getter reads them. Invented open map, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostHeroStatFactsTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;

    [TestMethod]
    public void A_hero_effect_on_the_level_or_the_alignment_reaches_the_hostility_facts()
    {
        var f = AreaAiHostRebindTests.Open(heroEdit: hero => hero with { Level = 4, Alignment = 100 });
        using var _ = f;
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var level = host.Find(Pc)!.Level;
        var alignment = host.Find(Pc)!.Alignment;
        Assert.AreEqual(4, level);
        Assert.AreEqual(100, alignment, "the stored alignment, not 0");
        var rule = new SessionEffectDefinition(140, [
            new(EffectAttributeKind.Stat, GameSessionOwner.ScriptStatLevel, EffectChangeOperator.Add, 2),
            new(EffectAttributeKind.Stat, GameSessionOwner.ScriptStatAlignment, EffectChangeOperator.Add, -30)]);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.AddHeroEffect(f.Session.Version, rule, EffectCause.Spell).Disposition);
        Assert.AreEqual(level + 2, host.Find(Pc)!.Level);
        Assert.AreEqual(Math.Clamp(alignment - 30, SessionHeroLimits.MinAlignment, SessionHeroLimits.MaxAlignment), host.Find(Pc)!.Alignment);
        Assert.AreEqual(f.Session.QueryHeroScriptStat(GameSessionOwner.ScriptStatAlignment), host.Find(Pc)!.Alignment, "the same value as the script getter");
    }
}
