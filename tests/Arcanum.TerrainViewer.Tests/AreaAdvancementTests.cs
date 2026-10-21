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
    private static CombatFields Follower(int scheme = 62, int race = 0, int mastery = -1, int fireRank = 1)
    {
        var stats = new int[28];
        for (var i = 0; i < 8; i++) stats[i] = 8;
        stats[17] = 1; stats[27] = race; stats[25] = 20;
        var spellTech = new int[25];
        spellTech[12] = fireRank; spellTech[16] = mastery;
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
        Assert.AreEqual(-1, loaded.Sheet.MasteredCollege);
        Assert.AreEqual(12, AreaAdvancement.Npc("x", Follower(mastery: 12, fireRank: 5), Effect)!.Value.Loaded.Sheet.MasteredCollege,
            "ARC-MAGIC-004: the mastered college comes from the field's index 16");
        Assert.IsNull(AreaAdvancement.Npc("x", Follower(mastery: 16), Effect), "a mastery outside −1..15 gives no sheet");
        Assert.IsNull(AreaAdvancement.Npc("x", Follower(fireRank: 6), Effect), "a rank above 5 gives no sheet");
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

    private static GameSessionOwner Session(RuleMode rules = RuleMode.Steamwoven)
    {
        var flags = AreaStartingHero.NewGameFlags(new(new(Guid.NewGuid()), new(Guid.NewGuid())), new(Guid.NewGuid()), null);
        return GameSessionOwner.Create(new(ConversationSessionFixture.Clock, flags,
            new(new(1), new(1, 1), Arcanum.Simulation.Movement.NeighborDirection.XPlus), AutoLevel: Rules()) { Rules = rules }).Owner!;
    }

    private static (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)? Sheet(string entity) =>
        entity is "virgil" or "far" or "near" or "dead" ? AreaAdvancement.Npc(entity, Follower(), Effect) : null;

    [TestMethod]
    public void AnAwardsFollowerStepLevelsTheHerosFollowers()
    {
        using var session = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "nobody").Disposition);
        var award = new SessionExperienceAward(2100, 1, 1, new(1, 2));
        var report = AreaAdvancement.LevelFollowers(session, award, Sheet, _ => new(false, null));
        Assert.AreEqual(1, report.Raised.Count);
        var record = session.QueryNpcAdvancement("virgil")!;
        // One point: dexterity 9 completes nothing more (intelligence 9 cannot be paid), so rule 70 does not run.
        Assert.AreEqual((2, 9, 8, 0), (record.Sheet.Level, record.Sheet.Stats[1], record.Sheet.Stats[4], record.Sheet.UnspentPoints));
        Assert.AreEqual(2, AreaAdvancement.Level(session, "virgil", 1));
        Assert.AreEqual(7, AreaAdvancement.Level(session, "nobody", 7));
        Assert.AreEqual(0, AreaAdvancement.LevelFollowers(session, award with { Followers = null }, Sheet, _ => new(false, null)).Raised.Count);
    }

    [TestMethod]
    public void WaitingFollowersGetTheStepByTheRuleSetting()
    {
        // ARC-WORLD-047 (OL29, owner decision D7). Four waiting followers: "near" on the loaded map 5 tiles away, "far" on the loaded map
        // 20 tiles away, "away" on another map (no sheet readable here), "dead" next to the PC.
        AreaWaitingPlace Place(string entity) => entity switch
        {
            "near" => new(false, (5, -3)), "far" => new(false, (20, 0)), "dead" => new(true, (1, 0)), _ => new(false, null),
        };
        GameSessionOwner Waiting(RuleMode rules)
        {
            var session = Session(rules);
            foreach (var entity in new[] { "near", "far", "away", "dead" })
            {
                Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
                Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, entity).Disposition);
            }
            return session;
        }
        var award = new SessionExperienceAward(2100, 1, 1, new(1, 2));

        // Original: only the one inside the window's rectangle; nothing is kept for later.
        using (var original = Waiting(RuleMode.Original))
        {
            var report = AreaAdvancement.LevelFollowers(original, award, Sheet, Place);
            CollectionAssert.AreEqual(new[] { "near" }, report.Raised.Select(r => r.Record!.Entity).ToArray());
            Assert.AreEqual(0, report.Deferred.Count);
            Assert.IsNull(original.QueryNpcAdvancement("far"));
            Assert.AreEqual(0, original.QueryMissedLevelEntities().Count);
        }

        // Steamwoven: every one that is not dead; the one whose sheet cannot be read keeps the step until its map loads.
        using var steamwoven = Waiting(RuleMode.Steamwoven);
        var levelled = AreaAdvancement.LevelFollowers(steamwoven, award, Sheet, Place);
        CollectionAssert.AreEquivalent(new[] { "near", "far" }, levelled.Raised.Select(r => r.Record!.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "away" }, levelled.Deferred.ToArray());
        Assert.IsNull(steamwoven.QueryNpcAdvancement("dead"));
        Assert.AreEqual(1, AreaAdvancement.LevelFollowers(steamwoven, award with { Followers = new(2, 3) }, Sheet, Place).Deferred.Count);
        CollectionAssert.AreEqual(new MissedLevelStep[] { new(new(1, 3), 1) }, steamwoven.QueryMissedLevelSteps("away")!.ToArray());
        // Nothing to apply while its map data cannot be read; once it can, both steps land at once (level 1 → 3).
        Assert.AreEqual(0, AreaAdvancement.ApplyMissed(steamwoven, Sheet).Count);
        var applied = AreaAdvancement.ApplyMissed(steamwoven, entity => entity == "away" ? AreaAdvancement.Npc(entity, Follower(), Effect) : null);
        Assert.AreEqual(3, applied.Single().Record!.Sheet.Level);
        Assert.AreEqual(0, steamwoven.QueryMissedLevelEntities().Count);
    }

    [TestMethod]
    public void MissedStepsComeBeforeTheNextStepAlsoAfterRejoining()
    {
        // A waiting follower keeps a step (1 → 3) it missed, then rejoins the array; the next step (3 → 5) applies the missed one
        // first: level 1 → 3 → 5 in turn.
        using var session = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, "virgil").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.DeferFollowerLevel(session.Version, "virgil", new(1, 3)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "virgil").Disposition);
        var report = AreaAdvancement.LevelFollowers(session, new SessionExperienceAward(2100, 2, 2, new(3, 5)), Sheet, _ => new(false, null));
        Assert.AreEqual(5, session.QueryNpcAdvancement("virgil")!.Sheet.Level);
        Assert.AreEqual(2, report.Raised.Count, "the missed steps and the new one each raised the level");
        Assert.AreEqual(0, session.QueryMissedLevelEntities().Count);
    }
}
