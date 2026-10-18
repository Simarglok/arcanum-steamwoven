using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-036: the viewer's scheme data and follower levels on synthetic tables and fields.
[TestClass]
public sealed class AreaAdvancementTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    // A follower like Virgil, invented: human, every primary stat 8, level 1, nothing to spend, one hit point bought, scheme 62,
    // effects 300 (strength +1, intelligence −1) and 301 (no rule).
    private static CombatFields Follower(int scheme = 62, int race = 0)
    {
        var stats = new int[28];
        for (var i = 0; i < 8; i++) stats[i] = 8;
        stats[17] = 1; stats[27] = race; stats[25] = 20;
        var spellTech = new int[25];
        spellTech[12] = 1; spellTech[16] = -1;
        return new(CombatFieldOrigin.AreaObject, new CombatObjectFields(16, 17102, 0, 0, 0, 0, 0, 1, 2, 0, 4, [0, 0, 0, 0, 0], 0),
            null, null, null, null, null,
            new CombatCritterFields(0, 0, stats, new int[12], new int[4], spellTech, 0, 0, 0, 1, 0, scheme, [new(300, 1), new(301, 1)]), null);
    }

    private static EffectRule? Effect(int number) => number == 300
        ? new(300, true, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Stat, 4, EffectChangeOperator.Add, -1),
            new(EffectAttributeKind.BasicSkill, 10, EffectChangeOperator.Add, 4), new(EffectAttributeKind.Special, 0, EffectChangeOperator.Percent, 10)])
        : null;

    private static SessionAutoLevelRules Rules() => AreaAdvancement.Rules(Mes("{62}{dx 9, in 9}\n{70}{cn 9}\n"), Mes(""), Mes(""), out _)!;

    [TestMethod]
    public void SchemeDataComesFromTheThreeTables()
    {
        var rules = AreaAdvancement.Rules(Mes("{50}{st 10, melee 4}\n{62}{dx 9}\n"), Mes("{3}{in 9}\n"), Mes("{1001}{IQ: 7, Cost: 1}\n"), out var detail);
        Assert.IsNull(detail);
        Assert.AreEqual(2, rules!.Scheme(50)!.Steps.Length);
        Assert.AreEqual(1, rules.Scheme(3)!.Steps.Length);
        Assert.IsNull(rules.Scheme(4));
        Assert.AreEqual(7, rules.SpellWillpower[0]);
        Assert.IsNull(AreaAdvancement.Rules(Mes("{50}{st}\n"), Mes(""), Mes(""), out detail));
        Assert.AreEqual("rules/gamelevel.mes scheme 50 MissingTarget", detail);
        Assert.IsNull(AreaAdvancement.Rules(Mes(""), Mes("{4}{fire 6}\n"), Mes(""), out detail));
        Assert.AreEqual("rules/userlevel.mes scheme 4 InvalidTarget", detail);
        Assert.IsNull(AreaAdvancement.Rules(Mes(""), Mes(""), Mes("{1001}{IQ: x}\n"), out detail));
        Assert.AreEqual("rules/spelllist.mes InvalidNumber 1001", detail);
    }

    [TestMethod]
    public void AFollowersSheetAndEffectsComeFromItsFields()
    {
        var (loaded, modifiers) = AreaAdvancement.Npc("virgil", Follower(), Effect)!.Value;
        Assert.AreEqual(("virgil", 62, CharacterRace.Human, 1, 1, 2), (loaded.Entity, loaded.AutoLevelScheme, loaded.Sheet.Race, loaded.Sheet.Level,
            loaded.Sheet.HitPointPoints, loaded.Sheet.HitPointAdjustment));
        Assert.AreEqual(1, loaded.Sheet.Colleges[12]);
        Assert.AreEqual(0, loaded.Sheet.TechDegrees[0], "the mastery slot is not a degree");
        CollectionAssert.AreEqual(new StatModifier[] { new(CharacterStat.Strength, StatModifierOperator.Add, 1), new(CharacterStat.Intelligence, StatModifierOperator.Add, -1) },
            modifiers.Stats.ToArray());
        Assert.AreEqual((1, 1), (modifiers.BasicSkills.Length, modifiers.MaxHitPoints.Length));
        Assert.IsNull(AreaAdvancement.Npc("x", Follower(race: 11), Effect));
        Assert.IsNull(AreaAdvancement.Npc("x", Follower(scheme: 300), Effect));
    }

    [TestMethod]
    public void EveryEffectOperatorKeepsItsMeaning()
    {
        EffectRule? Scaled(int number) => number == 300
            ? new(300, true, [new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Multiply, 2), new(EffectAttributeKind.Stat, 3, EffectChangeOperator.Divide, 2),
                new(EffectAttributeKind.Stat, 5, EffectChangeOperator.Maximum, 6), new(EffectAttributeKind.Stat, 6, EffectChangeOperator.Minimum, 9)])
            : null;
        var (_, modifiers) = AreaAdvancement.Npc("virgil", Follower(), Scaled)!.Value;
        CollectionAssert.AreEqual(new StatModifier[]
        {
            new(CharacterStat.Constitution, StatModifierOperator.Multiply, 2), new(CharacterStat.Beauty, StatModifierOperator.Divide, 2),
            new(CharacterStat.Perception, StatModifierOperator.Maximum, 6), new(CharacterStat.Willpower, StatModifierOperator.Minimum, 9),
        }, modifiers.Stats.ToArray());
    }

    [TestMethod]
    public void AnAwardsFollowerStepLevelsTheHerosFollowers()
    {
        var flags = AreaStartingHero.NewGameFlags(new(new(Guid.NewGuid()), new(Guid.NewGuid())), new(Guid.NewGuid()), null);
        using var session = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, flags,
            new(new(1), new(1, 1), Arcanum.Simulation.Movement.NeighborDirection.XPlus), AutoLevel: Rules())).Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "nobody").Disposition);
        var award = new SessionExperienceAward(2100, 1, 1, new(1, 2));
        var raised = AreaAdvancement.LevelFollowers(session, award, entity => entity == "virgil" ? AreaAdvancement.Npc(entity, Follower(), Effect) : null);
        Assert.AreEqual(1, raised.Count);
        var record = session.QueryNpcAdvancement("virgil")!;
        // One point: dexterity 9 completes nothing more (intelligence 9 cannot be paid), so rule 70 does not run.
        Assert.AreEqual((2, 9, 8, 0), (record.Sheet.Level, record.Sheet.Stats[1], record.Sheet.Stats[4], record.Sheet.UnspentPoints));
        Assert.AreEqual(2, AreaAdvancement.Level(session, "virgil", 1));
        Assert.AreEqual(7, AreaAdvancement.Level(session, "nobody", 7));
        // A follower is with the hero on any map; another NPC counts only on the map it stands on (here its home map).
        Assert.IsTrue(AreaAdvancement.Nearby(session, "virgil", 5, 79));
        Assert.IsTrue(AreaAdvancement.Nearby(session, "stranger", 79, 79));
        Assert.IsFalse(AreaAdvancement.Nearby(session, "stranger", 5, 79));
        Assert.AreEqual(0, AreaAdvancement.LevelFollowers(session, award with { Followers = null }, _ => null).Count);
    }
}
